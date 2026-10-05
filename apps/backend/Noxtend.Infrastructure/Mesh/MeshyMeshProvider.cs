using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// Meshy multi-image-to-3d 어댑터.
///
/// Design Ref: §5 · Plan D-02·D-03·D-04
///
/// **Tripo 와 다른 것은 둘뿐이다.** 방향을 이름이 아니라 **위치**로 받고, 입력을 업로드가
/// 아니라 **요청 본문**에 싣는다. 나머지 — 중립 상태·오류 코드·유료 제출 규칙 — 는 전부
/// 같다. 그것이 포트가 둘째 공급자를 견딘다는 증거다.
///
/// **제출과 나머지의 재시도 규칙이 다르다.** 조회는 무료라 429·5xx 에 다시 보내지만,
/// 제출은 유료라 429 외에는 자동 재시도하지 않는다 — 5xx 는 작업이 만들어졌는지 알 수
/// 없는 응답이고, 다시 보내면 같은 파츠에 두 번 과금될 수 있다.
/// </summary>
public sealed class MeshyMeshProvider(
    HttpClient http,
    string apiKey,
    MeshGenerationOptions options,
    ILogger<MeshyMeshProvider> logger,
    TimeSpan? retryDelay = null,
    Action<TimeSpan>? onDelay = null) : IMeshProvider
{
    private const string SubmitPath = "multi-image-to-3d";

    /// <summary>
    /// **방향을 위치로 푸는 유일한 자리** (Plan D-02 · D-12).
    ///
    /// Meshy 는 배열 순서로 방향을 해석하는데 문서가 보장하는 것은 첫 장이 정면이라는
    /// 것뿐이다. 나머지 셋은 물체를 한 방향으로 도는 순서로 가정한다.
    ///
    /// **<see cref="ViewDirection"/> 선언 순서를 쓰면 안 된다.** 그쪽은
    /// <c>Front · Right · Back · Left</c> 라 둘째와 넷째가 뒤바뀐다. <c>(int)direction</c>
    /// 이나 <c>Enum.GetValues()</c> 로 인덱스를 만드는 코드는 컴파일도 되고 예외도 안 나면서
    /// **좌우가 뒤집힌 mesh** 를 낸다 — 결과물을 열어 봐야만 아는 오류다.
    /// </summary>
    private static readonly ViewDirection[] PositionalOrder =
    [
        ViewDirection.Front,
        ViewDirection.Left,
        ViewDirection.Back,
        ViewDirection.Right,
    ];

    /// <summary>Tripo 와 같은 조건이어야 두 결과를 비교할 수 있다 (Plan D-04).</summary>
    private const int TargetPolycount = 5000;

    private readonly TimeSpan retryBase = retryDelay ?? TimeSpan.FromSeconds(1);

    // ─── 입력 준비 (§5.2) ───

    /// <summary>
    /// **아무것도 올리지 않는다.** Meshy 는 업로드 단계가 없어 입력이 요청 본문에 실린다.
    ///
    /// 그래서 여기 나오는 핸들은 <see cref="MeshInputHandle.IsDurable"/> 이 <c>false</c> 다
    /// — 저장하면 열 폭을 넘고, DB 가 이미지 저장소가 되며, 본문을 남기지 않는다는
    /// 규칙(NFR-02)과도 어긋난다 (D-10).
    /// </summary>
    public async Task<MeshInputHandle> UploadInputAsync(MeshInputUpload input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await input.Content.CopyToAsync(buffer, ct);

        var bytes = buffer.ToArray();

        if (bytes.Length == 0)
        {
            throw new MeshProviderException(
                "MESH_INPUT_REJECTED", "입력 이미지가 비어 있습니다", canRetry: false);
        }

        // base64 는 여기서 만든다 — Meshy 의 전송 형식이지 우리 도메인의 개념이 아니다 (§5.2)
        return new MeshInputHandle(
            $"data:{input.ContentType};base64,{Convert.ToBase64String(bytes)}",
            IsDurable: false);
    }

    // ─── 제출 (§5.3~5.4) ───

    public async Task<MeshSubmission> SubmitAsync(MultiviewMeshRequest request, CancellationToken ct)
    {
        var payload = Payload(request);

        // **호출 전에 잰다** (D-13). 뒤에서 재면 이미 돈이 나간 뒤다
        var size = Encoding.UTF8.GetByteCount(payload);
        if (size > options.MaxRequestBodyBytes)
        {
            throw new MeshProviderException(
                "MESH_REQUEST_TOO_LARGE",
                $"요청 본문이 상한을 넘습니다. {size} > {options.MaxRequestBodyBytes}",
                canRetry: false);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, SubmitPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
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
            logger.LogWarning(ex, "Meshy submit transport failure");
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
                throw Unknown(requestId: RequestId(response));
            }

            return TaskId(body) is { Length: > 0 } taskId
                ? new MeshSubmission(taskId)
                // 성공 응답인데 작업 ID 가 없다 — 만들어졌는지 알 수 없다
                : throw Unknown(requestId: RequestId(response));
        }
    }

    /// <summary>
    /// **첫 장이 정면이다** (Plan FR-04).
    ///
    /// 이 함수 하나가 좌우 뒤집힘을 막는 전부이고, 틀려도 예외가 나지 않는다.
    ///
    /// meshy-7 은 첫 장만 정면이면 되고 나머지 이미지의 순서·개수는 무관하다 (2026-09-17 조회,
    /// https://docs.meshy.ai/en/api/multi-image-to-3d: "the first image is used as the primary
    /// (front) view. The order of the remaining images doesn't matter."). 그래서 실제로 있는
    /// 이미지만 보내고, 없는 방향을 정면으로 채우지 않는다.
    /// </summary>
    private static string Payload(MultiviewMeshRequest request)
    {
        if (!request.Inputs.ContainsKey(ViewDirection.Front))
        {
            throw new MeshProviderException(
                "MESH_INPUT_REJECTED", "정면 이미지 없이 제출할 수 없습니다", canRetry: false);
        }

        // PositionalOrder (0=Front, 1=Left, 2=Back, 3=Right) 로 존재하는 이미지만 순서대로 담는다
        var images = PositionalOrder
            .Where(request.Inputs.ContainsKey)
            .Select(direction => request.Inputs[direction].Value)
            .ToArray();

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["image_urls"] = images,
            ["ai_model"] = request.Model,
            ["should_texture"] = true,
            ["enable_pbr"] = true,
            ["texture_image_resolution"] = 2048,
            ["topology"] = "triangle",
            ["target_polycount"] = TargetPolycount,
            ["should_remesh"] = true,
        });
    }

    // ─── 조회 (§5.5) ───

    public async Task<MeshTaskSnapshot> GetTaskAsync(string providerTaskId, CancellationToken ct)
    {
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{SubmitPath}/{providerTaskId}"), ct);

        using (response)
        {
            var body = await ReadAsync(response, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw Mapped(response, body);
            }

            // **본문이 기대한 모양이 아닐 수 있다.** 프록시가 200 에 HTML 오류 페이지를
            // 실어 보내면 여기가 예외로 죽고, 분류되지 않은 실패라 공정이 알 수 없는
            // 이유로 끝난다. 모르면 "아직 모른다" 로 읽고 다음 조회에 맡긴다
            var root = body.RootElement.ValueKind == JsonValueKind.Object
                ? body.RootElement
                : default;

            var status = Text(root, "status");

            var state = status switch
            {
                "PENDING" => MeshTaskState.Pending,
                "IN_PROGRESS" => MeshTaskState.Running,
                "SUCCEEDED" => MeshTaskState.Succeeded,
                "FAILED" => MeshTaskState.Failed,
                "CANCELED" => MeshTaskState.Canceled,
                _ => MeshTaskState.Pending,
            };

            return new MeshTaskSnapshot(
                state,
                Progress: Number(root, "progress") is { } progress
                    ? Math.Clamp(progress, 0, 100)
                    : 0,
                CreditsConsumed: Number(root, "consumed_credits"),
                // 원문 메시지는 넣지 않는다 — 키가 되비쳐 오는 경우가 있다 (§13.1)
                FailureCode: state switch
                {
                    MeshTaskState.Failed => "MESH_TASK_FAILED",
                    MeshTaskState.Canceled => "MESH_TASK_CANCELED",
                    _ => null,
                },
                ProviderCode: null,
                ProviderRequestId: RequestId(response));
        }
    }

    // ─── 결과 (§5.6) ───

    public Task<IMeshResultDownload> OpenResultAsync(string providerTaskId, CancellationToken ct)
        => MeshyResultDownload.OpenAsync(http, apiKey, providerTaskId, options, ct);

    // ─── 공통 ───

    private void Authorize(HttpRequestMessage message)
        => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    /// <summary>무료 호출의 재시도. **제출에는 쓰지 않는다** — 유료라 규칙이 다르다.</summary>
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
                logger.LogWarning(ex, "Meshy request failed, retrying");
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

    /// <summary>공급자가 알려 준 대기 시간을 먼저 쓴다 — 언제 창이 열리는지는 그쪽만 안다.</summary>
    private async Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken ct)
    {
        var delay = response?.Headers.RetryAfter?.Delta
                    ?? retryBase * Math.Pow(2, attempt);

        // 테스트가 여기를 관측한다 — 실제로 기다리면 백오프 검증 하나가 몇 초씩 멈춘다
        onDelay?.Invoke(delay);

        if (onDelay is null && delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, ct);
        }
    }

    /// <summary>본문은 상한을 두고 읽는다 — 거대한 오류 본문이 워커 수만큼 메모리를 먹는다.</summary>
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

    /// <summary>
    /// Design Ref: §6 — 두 공급자가 **같은 중립 코드**로 모인다.
    ///
    /// Meshy 는 Tripo 같은 숫자 코드를 주지 않으므로 상태 코드와 오류 문구가 근거다.
    /// </summary>
    private static MeshProviderException Mapped(
        HttpResponseMessage response, JsonDocument body, bool? canRetryOverride = null)
    {
        var reason = ErrorText(body);

        var (failureCode, canRetry) = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ("MESH_AUTH_FAILED", false),
            HttpStatusCode.PaymentRequired => ("MESH_CREDITS_INSUFFICIENT", false),
            HttpStatusCode.NotFound => ("MESH_MODEL_DEPRECATED", false),
            HttpStatusCode.TooManyRequests => ("MESH_RATE_LIMITED", true),

            // 400 은 무엇이 거부됐는지 문구로만 다르다. **모르면 입력 탓으로 두지 않는다**
            HttpStatusCode.BadRequest when Mentions(reason, "credit")
                => ("MESH_CREDITS_INSUFFICIENT", false),
            HttpStatusCode.BadRequest when Mentions(reason, "policy", "moderation", "nsfw")
                => ("MESH_CONTENT_REJECTED", false),
            HttpStatusCode.BadRequest => ("MESH_INPUT_REJECTED", false),

            _ when (int)response.StatusCode >= 500 => ("MESH_PROVIDER_UNAVAILABLE", true),
            _ => ("MESH_PROVIDER_UNAVAILABLE", true),
        };

        return new MeshProviderException(
            failureCode,
            $"Meshy 호출에 실패했습니다. HTTP {(int)response.StatusCode}",
            canRetryOverride ?? canRetry,
            providerCode: null,
            RequestId(response));
    }

    private static bool Mentions(string? reason, params string[] needles)
        => reason is not null
           && needles.Any(needle => reason.Contains(needle, StringComparison.OrdinalIgnoreCase));

    /// <summary>제출 결과가 불명확하다 — **자동 재시도를 막는 것이 이 예외의 전부다**.</summary>
    private static MeshProviderException Unknown(string? requestId = null)
        => new(
            "MESH_SUBMISSION_UNKNOWN",
            "제출 결과를 확인할 수 없습니다",
            canRetry: false,
            providerCode: null,
            requestId);

    private static string? TaskId(JsonDocument body)
    {
        if (body.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // 응답이 `{"result":"<id>"}` 이거나 `{"id":"<id>"}` 둘 다 관측된다
        return Text(body.RootElement, "result") ?? Text(body.RootElement, "id");
    }

    /// <summary>
    /// 오류 문구. **사용자에게 나가지 않는다** — 분류에만 쓰고 그 자리에서 버린다.
    /// </summary>
    private static string? ErrorText(JsonDocument body)
        => body.RootElement.ValueKind == JsonValueKind.Object
            ? Text(body.RootElement, "message") ?? Text(body.RootElement, "error")
              ?? Text(body.RootElement, "task_error")
            : null;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? RequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues("X-Request-Id", out var values)
            ? values.FirstOrDefault()
            : null;
}
