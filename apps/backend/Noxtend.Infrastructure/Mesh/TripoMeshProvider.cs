using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// Tripo P Series 어댑터.
///
/// Design Ref: §7.1~7.6 · Plan D-03·D-04·D-06
///
/// **공급자 어휘가 이 클래스 밖으로 나가지 않는다.** `file_token`, `task_id`,
/// `queued/running` 같은 문자열은 여기서 중립 타입으로 바뀐다.
///
/// **제출과 나머지의 재시도 규칙이 다르다.** 업로드와 조회는 무료라 429·5xx 에 다시
/// 보내지만, 제출은 유료라 429 외에는 자동 재시도하지 않는다 — 5xx 는 작업이
/// 만들어졌는지 알 수 없는 응답이고, 다시 보내면 같은 파츠에 두 번 과금될 수 있다.
/// </summary>
public sealed class TripoMeshProvider(
    HttpClient http,
    string apiKey,
    MeshGenerationOptions options,
    ILogger<TripoMeshProvider> logger,
    TimeSpan? retryDelay = null,
    Action<TimeSpan>? onDelay = null) : IMeshProvider
{
    /// <summary>
    /// P Series 고정 품질 옵션 (Plan D-04).
    ///
    /// **사용자에게 노출하지 않는다.** 조합을 바꿀 수 있게 하면 결과가 왜 달라졌는지
    /// 나중에 재현할 수 없다. 작업에 저장되는 것은 모델 스냅숏 이름 하나뿐이다.
    /// </summary>
    private const int FaceLimit = 5000;

    private readonly TimeSpan retryBase = retryDelay ?? TimeSpan.FromSeconds(1);

    // ─── 업로드 (§7.2) ───

    public async Task<MeshInputHandle> UploadInputAsync(MeshInputUpload input, CancellationToken ct)
    {
        // **바이트로 들고 있어야 한다.** `MultipartFormDataContent` 를 버리면 안에 든
        // `StreamContent` 도 함께 닫혀서, 스트림 하나를 재사용하면 두 번째 시도가
        // 닫힌 스트림을 읽는다
        using var buffer = new MemoryStream();
        await input.Content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var response = await SendWithRetryAsync(
            () =>
            {
                var file = new StreamContent(new MemoryStream(bytes, writable: false));
                file.Headers.ContentType = new MediaTypeHeaderValue(input.ContentType);

                // 필드 이름은 `file`, 파일명은 방향뿐이다 (§7.2)
                var form = new MultipartFormDataContent { { file, "file", input.SafeFileName } };

                return new HttpRequestMessage(HttpMethod.Post, "files") { Content = form };
            },
            ct);

        using (response)
        {
            var body = await ReadAsync(response, ct);
            EnsureSuccess(response, body, submitting: false);

            return body.RootElement.TryGetProperty("data", out var data)
                   && data.TryGetProperty("file_token", out var token)
                   && token.GetString() is { Length: > 0 } value
                // **내구적이다** — 공급자 쪽에 실제 파일이 올라갔으므로 저장해 두고 다시 쓴다.
                // 재기동해도 같은 장을 두 번 올리지 않는 것이 이 값의 값어치다 (D-10)
                ? new MeshInputHandle(value, IsDurable: true)
                : throw new MeshProviderException(
                    "MESH_INPUT_REJECTED", "업로드 응답에 파일 참조가 없습니다", canRetry: false);
        }
    }

    // ─── 제출 (§7.3) ───

    public async Task<MeshSubmission> SubmitAsync(MultiviewMeshRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "generation/multiview-to-model")
        {
            Content = new StringContent(Payload(request), Encoding.UTF8, "application/json"),
        };

        Authorize(message);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 전송이 끊기면 서버에 닿았는지 알 수 없다 — 작업이 이미 생겼을 수 있다
            logger.LogWarning(ex, "Tripo submit transport failure");
            throw Unknown();
        }

        using (response)
        {
            var body = await ReadAsync(response, ct);

            // **429 만 다르다.** 작업이 만들어지지 않았음이 확실한 유일한 응답이다
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw Mapped(response, body, canRetryOverride: true);
            }

            // 4xx 는 요청 자체가 틀렸다는 뜻이라 작업이 만들어지지 않았다
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                throw Mapped(response, body, canRetryOverride: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Unknown(ProviderCode(body), RequestId(response, body));
            }

            return body.RootElement.TryGetProperty("data", out var data)
                   && data.TryGetProperty("task_id", out var id)
                   && id.GetString() is { Length: > 0 } taskId
                ? new MeshSubmission(taskId)
                // 성공 응답인데 작업 ID 가 없다 — 만들어졌는지 알 수 없다
                : throw Unknown(ProviderCode(body), RequestId(response, body));
        }
    }

    /// <summary>
    /// Tripo P1 공식 문서(2026-09-17 조회,
    /// https://docs.tripo3d.ai/model-generation/multiview-to-model-p1-20260311.html):
    /// "'files': ... The list must contain exactly 4 items in the order [front, left, back,
    /// right]. You may omit certain input files by omitting the file_token, but the front
    /// input cannot be omitted. Do not use less than two images to generate."
    ///
    /// 그래서 배열은 방향 존재 여부와 무관하게 **항상 4칸**이고, 방향은 위치로만 정해진다
    /// (지도의 키가 아니다 — 이 자리가 예전 `inputs`/방향 이름 키 구현과 다른 지점이다).
    /// 존재하지 않는 방향은 그 위치에 `file_token` 없는 객체를 넣어 슬롯만 유지한다.
    ///
    /// `type` 필드를 "jpg" 로 고정한 이유는 아래 <see cref="Payload"/> 안의 상세 주석 참고.
    /// </summary>
    private static readonly ViewDirection[] PositionalOrder =
    [
        ViewDirection.Front,
        ViewDirection.Left,
        ViewDirection.Back,
        ViewDirection.Right,
    ];

    private static string Payload(MultiviewMeshRequest request)
    {
        if (!request.Inputs.ContainsKey(ViewDirection.Front))
        {
            throw new MeshProviderException(
                "MESH_INPUT_REJECTED", "정면 이미지 없이 제출할 수 없습니다", canRetry: false);
        }

        if (request.Inputs.Count < 2)
        {
            throw new MeshProviderException(
                "MESH_INPUT_REJECTED", "Tripo 는 최소 2장 이상의 이미지가 필요합니다", canRetry: false);
        }

        // `type` 을 "jpg" 로 고정하는 이유.
        //
        // 출처(2026-09-17 조회): https://docs.tripo3d.ai/model-generation/multiview-to-model-p1-20260311.html
        // 원문: "'type': Indicates the file type. Although currently not validated,
        // specifying the correct file type is strongly advised."
        //
        // 즉 (1) 지금 Tripo 서버는 이 값을 검증하지 않고, (2) 허용 값 목록이나 예시가 문서에
        // 전혀 없다 — MIME 타입인지("image/jpeg") 확장자인지("jpg")조차 문서로는 확정 불가.
        // (3) "정확히 넣는 걸 권장"이라는 문구는 앞으로 서버가 이 값으로 실제 디코딩 방식을
        // 정할 수도 있다는 뜻이라, 지금 검증이 없다고 아무 값이나 넣어도 안전하다는 보장은
        // 아니다.
        //
        // "jpg" 로 고정한 근거는 이 API가 아니라 **우리 업로드 경로**에 있다:
        // `TripoMeshProviderTests.Upload_SendsTheFileNameItWasGiven` 이 못박듯 업로드
        // 파일명이 항상 `{direction}.jpg` 이고(§7.2, "방향 이름만 쓴다"), 이 프로젝트가
        // 파츠 이미지를 jpg 로만 만든다는 전제가 유지되는 동안에만 유효한 임시값이다.
        //
        // 이 전제가 깨지면(예: png 업로드 지원 추가) 여기를 고쳐야 한다 — `MeshInputHandle`
        // 이나 `MultiviewMeshRequest` 가 지금 콘텐츠 타입 정보를 안 들고 있어서, 그때는
        // 이 타입들에 콘텐츠 타입을 실어 나르는 확장이 함께 필요하다.
        const string FixedFileType = "jpg";

        var files = PositionalOrder
            .Select(direction => request.Inputs.TryGetValue(direction, out var handle)
                ? new Dictionary<string, string> { ["type"] = FixedFileType, ["file_token"] = handle.Value }
                : new Dictionary<string, string> { ["type"] = FixedFileType })
            .ToArray();

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["files"] = files,
            ["model"] = request.Model,
            ["model_seed"] = request.ModelSeed,
            ["texture_seed"] = request.TextureSeed,
            ["face_limit"] = FaceLimit,
            ["texture"] = true,
            ["pbr"] = true,
            ["texture_quality"] = "standard",
            ["texture_alignment"] = "original_image",
            ["orientation"] = "default",
            ["auto_size"] = false,
            ["export_uv"] = true,

            // `compress` 는 보내지 않는다 — 첫 GLB 호환성을 확인하기 전에 meshopt 를
            // 강제하면 뷰어에 따라 열리지 않는 파일이 나온다
        });
    }

    // ─── 조회 (§7.4) ───

    public async Task<MeshTaskSnapshot> GetTaskAsync(string providerTaskId, CancellationToken ct)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"tasks/{providerTaskId}"), ct);

        using (response)
        {
            var body = await ReadAsync(response, ct);
            EnsureSuccess(response, body, submitting: false);

            // **`data` 가 없을 수 있다.** 프록시가 200 에 HTML 오류 페이지를 실어 보내면
            // 여기가 KeyNotFoundException 으로 죽고, 분류되지 않은 예외라 공정이 알 수 없는
            // 이유로 실패한다. 값이 없으면 "아직 모른다" 로 읽고 다음 조회에 맡긴다
            var hasData = body.RootElement.TryGetProperty("data", out var data)
                          && data.ValueKind == JsonValueKind.Object;

            var status = hasData && data.TryGetProperty("status", out var s) ? s.GetString() : null;

            var state = status switch
            {
                "queued" => MeshTaskState.Pending,
                "running" => MeshTaskState.Running,
                "success" => MeshTaskState.Succeeded,
                "failed" => MeshTaskState.Failed,
                "cancelled" => MeshTaskState.Canceled,
                _ => MeshTaskState.Pending,
            };

            return new MeshTaskSnapshot(
                state,
                Progress: hasData && data.TryGetProperty("progress", out var p) && p.TryGetInt32(out var progress)
                    ? Math.Clamp(progress, 0, 100)
                    : 0,
                CreditsConsumed: hasData && data.TryGetProperty("credits_consumed", out var c)
                                 && c.TryGetInt32(out var credits)
                    ? credits
                    : null,
                // 원문 메시지는 넣지 않는다 — 키가 되비쳐 오는 경우가 있다 (§13.1)
                FailureCode: state switch
                {
                    MeshTaskState.Failed => "MESH_TASK_FAILED",
                    MeshTaskState.Canceled => "MESH_TASK_CANCELED",
                    _ => null,
                },
                ProviderCode: ProviderCode(body),
                ProviderRequestId: RequestId(response, body));
        }
    }

    public Task<IMeshResultDownload> OpenResultAsync(string providerTaskId, CancellationToken ct)
        => TripoResultDownload.OpenAsync(http, apiKey, providerTaskId, options, ct);

    // ─── 공통 ───

    private void Authorize(HttpRequestMessage message)
        => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    /// <summary>
    /// 무료 호출의 재시도. **제출에는 쓰지 않는다** — 유료라 규칙이 다르다.
    ///
    /// 요청 메시지를 팩토리로 받는 이유는 <c>HttpRequestMessage</c> 를 재사용할 수
    /// 없어서다. 한 번 보낸 것을 다시 보내면 EF 가 아니라 HttpClient 가 거절한다.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> create, CancellationToken ct)
    {
        HttpResponseMessage? last = null;

        for (var attempt = 0; attempt <= options.MaxHttpRetries; attempt++)
        {
            last?.Dispose();

            using var message = create();
            Authorize(message);

            try
            {
                last = await http.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       && attempt < options.MaxHttpRetries)
            {
                logger.LogWarning(ex, "Tripo request failed, retrying");
                await DelayAsync(attempt, null, ct);
                continue;
            }

            if (!ProviderHttp.IsTransient(last.StatusCode) || attempt == options.MaxHttpRetries)
            {
                return last;
            }

            await DelayAsync(attempt, last, ct);
        }

        return last!;
    }

    /// <summary>
    /// 공급자가 알려 준 대기 시간을 먼저 쓴다 (§7.4).
    ///
    /// 우리 지수 backoff 보다 그쪽 값이 정확하다 — 언제 창이 열리는지는 공급자만 안다.
    /// </summary>
    private async Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken ct)
    {
        var delay = response?.Headers.RetryAfter?.Delta
                    ?? RateLimitReset(response)
                    ?? retryBase * Math.Pow(2, attempt);

        // **테스트가 여기를 관측한다.** 실제로 기다리게 두면 429 백오프 검증 하나가
        // 7초씩 멈춘다 — 확인하려는 것은 기다린다는 사실이 아니라 얼마를 고르는가다
        onDelay?.Invoke(delay);

        if (onDelay is null && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct);
        }
    }

    private static TimeSpan? RateLimitReset(HttpResponseMessage? response)
        => response is not null
           && response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
           && int.TryParse(values.FirstOrDefault(), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>
    /// 본문은 상한을 두고 읽는다 (§7.1). 공급자가 거대한 오류 본문을 보내면
    /// 파드 메모리가 워커 수만큼 배로 든다.
    /// </summary>
    private static async Task<JsonDocument> ReadAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        const int maxBody = 64 * 1024;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var limited = new MemoryStream();

        var buffer = new byte[8 * 1024];
        int read;
        while (limited.Length < maxBody && (read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await limited.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        limited.Position = 0;

        try
        {
            return JsonDocument.Parse(limited);
        }
        catch (JsonException)
        {
            // 본문이 JSON 이 아닌 것은 그 자체로는 실패가 아니다 — 상태 코드가 정본이다
            return JsonDocument.Parse("{}");
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response, JsonDocument body, bool submitting)
    {
        if (response.IsSuccessStatusCode && ProviderCode(body) is null or 0)
        {
            return;
        }

        throw submitting ? Unknown(ProviderCode(body), RequestId(response, body)) : Mapped(response, body);
    }

    /// <summary>Design Ref: §7.6 — 상태 코드와 공급자 코드를 우리 코드로 옮긴다.</summary>
    private static MeshProviderException Mapped(
        HttpResponseMessage response, JsonDocument body, bool? canRetryOverride = null)
    {
        var providerCode = ProviderCode(body);
        var requestId = RequestId(response, body);

        var (failureCode, canRetry) = (response.StatusCode, providerCode) switch
        {
            (HttpStatusCode.Unauthorized, _) or (_, 1000) or (_, 1001)
                => ("MESH_AUTH_FAILED", false),
            (HttpStatusCode.Forbidden, 2010) => ("MESH_CREDITS_INSUFFICIENT", false),
            (_, 2008) => ("MESH_CONTENT_REJECTED", false),
            (_, 2015) => ("MESH_MODEL_DEPRECATED", false),
            (_, 2018) => ("MESH_TOO_COMPLEX", false),
            (HttpStatusCode.BadRequest, _) or (_, 2002) or (_, 2003) or (_, 2004)
                => ("MESH_INPUT_REJECTED", false),
            (HttpStatusCode.TooManyRequests, _) or (_, 1007) or (_, 2000)
                => ("MESH_RATE_LIMITED", true),
            _ when (int)response.StatusCode >= 500 => ("MESH_PROVIDER_UNAVAILABLE", true),
            _ => ("MESH_PROVIDER_UNAVAILABLE", true),
        };

        return new MeshProviderException(
            failureCode,
            $"Tripo 호출에 실패했습니다. HTTP {(int)response.StatusCode}",
            canRetryOverride ?? canRetry,
            providerCode,
            requestId);
    }

    /// <summary>
    /// 제출 결과가 불명확하다 — **자동 재시도를 막는 것이 이 예외의 전부다** (Plan D-06).
    /// </summary>
    private static MeshProviderException Unknown(int? providerCode = null, string? requestId = null)
        => new(
            "MESH_SUBMISSION_UNKNOWN",
            "제출 결과를 확인할 수 없습니다",
            canRetry: false,
            providerCode,
            requestId);

    private static int? ProviderCode(JsonDocument body)
        => body.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value)
            ? value
            : null;

    private static string? RequestId(HttpResponseMessage response, JsonDocument body)
    {
        if (body.RootElement.TryGetProperty("request_id", out var id)
            && id.GetString() is { Length: > 0 } fromBody)
        {
            return fromBody;
        }

        return response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;
    }
}
