using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// Tripo 어댑터의 HTTP 계약.
///
/// Design Ref: §7.1~7.6 · Plan NFR-08
///
/// **credit 을 쓰지 않고 확인한다.** 가짜 <c>HttpMessageHandler</c> 로 요청을 가로채
/// 무엇을 보내는지 본다. 실제 호출로 확인하려면 여기 있는 검사 하나마다 유료 작업이
/// 하나씩 생긴다.
/// </summary>
public sealed class TripoMeshProviderTests
{
    // ─── 업로드 (§7.2) ───

    [Fact]
    public async Task Upload_PostsMultipartToTheFilesEndpoint()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{"file_token":"file_abc"}}"""));
        var provider = Provider(handler);

        var handle = await provider.UploadInputAsync(Upload(), default);

        Assert.Equal("https://openapi.tripo3d.ai/v3/files", handler.LastUri);
        Assert.Equal("Bearer test-key", handler.LastAuthorization);
        Assert.StartsWith("multipart/form-data", handler.LastContentType);
        Assert.Equal("file_abc", handle.Value);
    }

    /// <summary>
    /// 파일명은 **정규화가 정해 준 것 그대로** 나간다 (§7.2).
    ///
    /// 어댑터가 자기 나름의 이름을 지으면 정규화 쪽에서 세운 "방향 이름만 쓴다" 는
    /// 보장(<see cref="MeshInputNormalizerTests"/>)이 무의미해진다.
    /// </summary>
    [Fact]
    public async Task Upload_SendsTheFileNameItWasGiven()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{"file_token":"file_abc"}}"""));

        await Provider(handler).UploadInputAsync(Upload(ViewDirection.Left), default);

        // .NET 은 따옴표 없이 쓰고 UTF-8 사본을 덧붙인다
        Assert.Contains("name=file", handler.LastBody);
        Assert.Contains("filename=left.jpg", handler.LastBody);
    }

    [Fact]
    public async Task Upload_RetriesOnServerError()
    {
        var handler = RespondsInOrder(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            Ok("""{"code":0,"data":{"file_token":"file_abc"}}"""));

        var handle = await Provider(handler).UploadInputAsync(Upload(), default);

        // 파일 업로드는 아직 유료 작업이 아니다 — 재시도해도 과금이 늘지 않는다
        Assert.Equal("file_abc", handle.Value);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Upload_FailsWhenTheKeyIsRejected()
    {
        var handler = Responds(HttpStatusCode.Unauthorized);

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).UploadInputAsync(Upload(), default));

        Assert.Equal("MESH_AUTH_FAILED", failure.FailureCode);
        Assert.False(failure.CanRetry);
    }

    // ─── 제출 (§7.3) ───

    /// <summary>
    /// Tripo 공식 문서(2026-09-17 조회,
    /// https://docs.tripo3d.ai/model-generation/multiview-to-model-p1-20260311.html):
    /// "'files': ... The list must contain exactly 4 items in the order [front, left, back,
    /// right]." 파라미터명은 `inputs`가 아니라 `files`이고, 배열은 방향 존재 여부와 무관하게
    /// 항상 4칸 고정이며, 원소는 방향 이름 키가 아니라 {type, file_token} 객체다.
    /// </summary>
    [Fact]
    public async Task Submit_SendsFixedFourSlotFiles()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{"task_id":"task_1"}}"""));

        var submission = await Provider(handler).SubmitAsync(Request(), default);

        Assert.Equal("https://openapi.tripo3d.ai/v3/generation/multiview-to-model", handler.LastUri);
        Assert.Equal("task_1", submission.ProviderTaskId);

        var payload = JsonDocument.Parse(handler.LastBody!).RootElement;
        var files = payload.GetProperty("files");

        // 위치가 [front, left, back, right] 고정이다 — 우리 내부 순서와 좌우가 다르다
        Assert.Equal(4, files.GetArrayLength());
        Assert.Equal("file_f", files[0].GetProperty("file_token").GetString());
        Assert.Equal("file_l", files[1].GetProperty("file_token").GetString());
        Assert.Equal("file_b", files[2].GetProperty("file_token").GetString());
        Assert.Equal("file_r", files[3].GetProperty("file_token").GetString());
    }

    /// <summary>
    /// 생략은 배열 길이를 줄이는 게 아니라 그 위치의 file_token 만 비우는 것이다
    /// ("You may omit certain input files by omitting the file_token, but the front input
    /// cannot be omitted.").
    /// </summary>
    [Fact]
    public async Task Submit_OmittedDirections_KeepTheSlotButDropTheToken()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{"task_id":"task_1"}}"""));

        var inputs = new Dictionary<ViewDirection, MeshInputHandle>
        {
            [ViewDirection.Front] = new("file_f", IsDurable: true),
            [ViewDirection.Back] = new("file_b", IsDurable: true),
        };

        await Provider(handler).SubmitAsync(
            new MultiviewMeshRequest(inputs, "P1-20260311", 123456, 654321), default);

        var files = JsonDocument.Parse(handler.LastBody!).RootElement.GetProperty("files");

        Assert.Equal(4, files.GetArrayLength());
        Assert.Equal("file_f", files[0].GetProperty("file_token").GetString());
        Assert.False(files[1].TryGetProperty("file_token", out _));
        Assert.Equal("file_b", files[2].GetProperty("file_token").GetString());
        Assert.False(files[3].TryGetProperty("file_token", out _));
    }

    /// <summary>
    /// Tripo 공식 문서: "Do not use less than two images to generate." 정면 포함 2장 미만이면
    /// 유료 호출 전에 거부한다 (MeshyMeshProvider 의 정면 누락 검사와 같은 자리·같은 패턴).
    /// </summary>
    [Fact]
    public async Task Submit_FewerThanTwoImages_IsRejectedWithoutCallingTheProvider()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{"task_id":"task_1"}}"""));

        var inputs = new Dictionary<ViewDirection, MeshInputHandle>
        {
            [ViewDirection.Front] = new("file_f", IsDurable: true),
        };

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(
                new MultiviewMeshRequest(inputs, "P1-20260311", 123456, 654321), default));

        Assert.Equal("MESH_INPUT_REJECTED", failure.FailureCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Submit_PinsTheQualityOptions()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{"task_id":"task_1"}}"""));

        await Provider(handler).SubmitAsync(Request(), default);

        var payload = JsonDocument.Parse(handler.LastBody!).RootElement;

        // 품질 조합을 운영자가 바꾸면 결과가 왜 달라졌는지 재현할 수 없다 (Plan D-04)
        Assert.Equal("P1-20260311", payload.GetProperty("model").GetString());
        Assert.Equal(5000, payload.GetProperty("face_limit").GetInt32());
        Assert.True(payload.GetProperty("texture").GetBoolean());
        Assert.True(payload.GetProperty("pbr").GetBoolean());
        Assert.Equal("standard", payload.GetProperty("texture_quality").GetString());
        Assert.Equal("original_image", payload.GetProperty("texture_alignment").GetString());
        Assert.True(payload.GetProperty("export_uv").GetBoolean());
        Assert.False(payload.GetProperty("auto_size").GetBoolean());

        // 첫 GLB 호환성을 확인하기 전에 meshopt 를 강제하지 않는다
        Assert.False(payload.TryGetProperty("compress", out _));

        // 씨앗을 우리가 정해야 재시도가 같은 조건에서 돈다
        Assert.Equal(123456, payload.GetProperty("model_seed").GetInt32());
        Assert.Equal(654321, payload.GetProperty("texture_seed").GetInt32());
    }

    /// <summary>
    /// **제출은 자동 재시도하지 않는다** (Plan D-06).
    ///
    /// 5xx 는 작업이 만들어졌는지 알 수 없는 응답이다. 다시 보내면 같은 파츠에 두 번
    /// 과금될 수 있다.
    /// </summary>
    [Fact]
    public async Task Submit_TreatsServerErrorAsUnknownWithoutRetrying()
    {
        var handler = Responds(HttpStatusCode.BadGateway);

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal("MESH_SUBMISSION_UNKNOWN", failure.FailureCode);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>성공 응답인데 작업 ID 가 없으면 그것도 모호한 상태다.</summary>
    [Fact]
    public async Task Submit_WithoutATaskId_IsAlsoUnknown()
    {
        var handler = Responds(() => Ok("""{"code":0,"data":{}}"""));

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal("MESH_SUBMISSION_UNKNOWN", failure.FailureCode);
    }

    /// <summary>
    /// 429 만 다르다 — 작업이 만들어지지 않았음이 확실한 유일한 응답이다 (§7.3).
    /// </summary>
    [Fact]
    public async Task Submit_RateLimited_IsRetryable()
    {
        var handler = Responds(HttpStatusCode.TooManyRequests);

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal("MESH_RATE_LIMITED", failure.FailureCode);
        Assert.True(failure.CanRetry);
    }

    [Fact]
    public async Task Submit_CreditsExhausted_IsFinal()
    {
        var handler = Responds(() => Json(HttpStatusCode.Forbidden, """{"code":2010,"message":"no credits"}"""));

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal("MESH_CREDITS_INSUFFICIENT", failure.FailureCode);
        Assert.False(failure.CanRetry);
    }

    // ─── 조회 (§7.4) ───

    [Theory]
    [InlineData("queued", MeshTaskState.Pending)]
    [InlineData("running", MeshTaskState.Running)]
    [InlineData("success", MeshTaskState.Succeeded)]
    [InlineData("failed", MeshTaskState.Failed)]
    [InlineData("cancelled", MeshTaskState.Canceled)]
    public async Task GetTask_MapsProviderStatusToNeutralState(string status, MeshTaskState expected)
    {
        var handler = Responds(() => Ok(
            $$$"""{"code":0,"data":{"status":"{{{status}}}","progress":42}}"""));

        var snapshot = await Provider(handler).GetTaskAsync("task_1", default);

        Assert.Equal(expected, snapshot.State);
        Assert.Equal("https://openapi.tripo3d.ai/v3/tasks/task_1", handler.LastUri);
    }

    [Fact]
    public async Task GetTask_CarriesProgressAndCredits()
    {
        var handler = Responds(() => Ok(
            """{"code":0,"data":{"status":"success","progress":100,"credits_consumed":50}}"""));

        var snapshot = await Provider(handler).GetTaskAsync("task_1", default);

        Assert.Equal(100, snapshot.Progress);
        Assert.Equal(50, snapshot.CreditsConsumed);
    }

    [Fact]
    public async Task GetTask_RetriesOnServerError()
    {
        var handler = RespondsInOrder(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            Ok("""{"code":0,"data":{"status":"running","progress":10}}"""));

        var snapshot = await Provider(handler).GetTaskAsync("task_1", default);

        // 조회는 유료가 아니다 — 같은 작업 ID 로 다시 물어도 과금이 늘지 않는다
        Assert.Equal(MeshTaskState.Running, snapshot.State);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>
    /// 공급자 원문은 진단 코드만 남긴다 (§13.1).
    ///
    /// `message` 와 `suggestion` 에 키나 URL 이 섞여 오는 경우가 있어 사용자 오류와
    /// 일반 로그에는 넣지 않는다.
    /// </summary>
    [Fact]
    public async Task FailedTask_KeepsOnlyTheDiagnosticCode()
    {
        var handler = Responds(() => Ok(
            """{"code":0,"data":{"status":"failed","progress":0},"message":"key sk-live-abc rejected"}"""));

        var snapshot = await Provider(handler).GetTaskAsync("task_1", default);

        Assert.Equal(MeshTaskState.Failed, snapshot.State);
        Assert.Equal("MESH_TASK_FAILED", snapshot.FailureCode);
        Assert.DoesNotContain("sk-live", snapshot.FailureCode);
    }

    // ─── 재시도와 본문 (§7.1 · §7.4) ───

    /// <summary>
    /// **공급자가 알려 준 대기 시간을 먼저 쓴다** (§7.4).
    ///
    /// 우리 지수 backoff 보다 그쪽 값이 정확하다 — 언제 창이 열리는지는 공급자만 안다.
    /// 무시하면 닫힌 창을 계속 두드려 rate limit 이 더 길어진다.
    /// </summary>
    [Fact]
    public async Task RateLimitedGet_WaitsForRetryAfter()
    {
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

        var handler = RespondsInOrder(
            limited,
            Ok("""{"code":0,"data":{"status":"running","progress":10}}"""));

        var delays = new List<TimeSpan>();
        await ProviderRecordingDelays(handler, delays).GetTaskAsync("task_1", default);

        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(delays));
    }

    /// <summary>`Retry-After` 가 없으면 rate limit 헤더를 본다.</summary>
    [Fact]
    public async Task RateLimitedGet_FallsBackToRateLimitReset()
    {
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.Add("X-RateLimit-Reset", "13");

        var handler = RespondsInOrder(
            limited,
            Ok("""{"code":0,"data":{"status":"running","progress":10}}"""));

        var delays = new List<TimeSpan>();
        await ProviderRecordingDelays(handler, delays).GetTaskAsync("task_1", default);

        Assert.Equal(TimeSpan.FromSeconds(13), Assert.Single(delays));
    }

    /// <summary>
    /// 재시도는 무한하지 않다 (§7.1).
    ///
    /// 상한이 없으면 공급자 장애가 길어질 때 워커 하나가 그 스트림을 영원히 붙들고,
    /// 리스가 만료되지 않아 스위퍼도 회수하지 못한다.
    /// </summary>
    [Fact]
    public async Task ServerErrorsStopAtTheRetryCap()
    {
        var handler = Responds(HttpStatusCode.ServiceUnavailable);
        var options = new MeshGenerationOptions { MaxHttpRetries = 3 };

        await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler, options).GetTaskAsync("task_1", default));

        // 첫 시도 + 재시도 3회
        Assert.Equal(4, handler.Calls);
    }

    /// <summary>
    /// 본문이 JSON 이 아닌 것은 그 자체로 실패가 아니다 — 상태 코드가 정본이다.
    ///
    /// 200 인데 본문이 깨져 있으면 파싱 예외로 죽는 대신 값이 없는 것으로 읽는다.
    /// </summary>
    [Fact]
    public async Task MalformedBody_DoesNotCrashTheAdapter()
    {
        var handler = Responds(() => Json(HttpStatusCode.OK, "<html>gateway timeout</html>"));

        var snapshot = await Provider(handler).GetTaskAsync("task_1", default);

        Assert.Equal(MeshTaskState.Pending, snapshot.State);
        Assert.Equal(0, snapshot.Progress);
    }

    /// <summary>
    /// 본문을 상한까지만 읽는다 (§7.1).
    ///
    /// 공급자가 거대한 오류 본문을 보내면 파드 메모리가 워커 수만큼 배로 든다.
    /// </summary>
    [Fact]
    public async Task OversizedBody_IsReadButBounded()
    {
        var huge = new string('x', 512 * 1024);
        var handler = Responds(() => Json(HttpStatusCode.OK, huge));

        var snapshot = await Provider(handler).GetTaskAsync("task_1", default);

        // 잘린 본문은 JSON 이 아니므로 값이 없는 것으로 읽힌다 — 죽지 않는 것이 요지다
        Assert.Equal(MeshTaskState.Pending, snapshot.State);
    }

    // ─── 설정 ───

    private static readonly MeshGenerationOptions Options = new();

    /// <summary>
    /// 재시도 대기를 재는 공급자.
    ///
    /// 실제로 기다리게 두면 테스트가 7초·13초씩 멈춘다. 대기 **값**을 확인하는 것이
    /// 목적이므로 그 값을 받아 적고 넘어간다.
    /// </summary>
    private static TripoMeshProvider ProviderRecordingDelays(
        RecordingHandler handler, List<TimeSpan> delays)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openapi.tripo3d.ai/v3/") },
            "test-key",
            Options,
            NullLogger<TripoMeshProvider>.Instance,
            retryDelay: TimeSpan.Zero,
            onDelay: delays.Add);

    private static TripoMeshProvider Provider(
        RecordingHandler handler, MeshGenerationOptions? options = null)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openapi.tripo3d.ai/v3/") },
            "test-key",
            options ?? Options,
            NullLogger<TripoMeshProvider>.Instance,
            // 테스트가 재시도 대기로 멈추지 않게 한다 — 여기서 보는 것은 횟수와 분기다
            retryDelay: TimeSpan.Zero);

    private static MeshInputUpload Upload(ViewDirection direction = ViewDirection.Front)
        => new(
            direction,
            new MemoryStream([1, 2, 3, 4]),
            "image/jpeg",
            $"{direction.ToString().ToLowerInvariant()}.jpg",
            4);

    private static MultiviewMeshRequest Request()
        => new(
            new Dictionary<ViewDirection, MeshInputHandle>
            {
                [ViewDirection.Front] = new("file_f", IsDurable: true),
                [ViewDirection.Right] = new("file_r", IsDurable: true),
                [ViewDirection.Back] = new("file_b", IsDurable: true),
                [ViewDirection.Left] = new("file_l", IsDurable: true),
            },
            "P1-20260311",
            ModelSeed: 123456,
            TextureSeed: 654321);

    private static HttpResponseMessage Ok(string json) => Json(HttpStatusCode.OK, json);

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// **시도마다 새 응답을 만든다.** 재시도 루프가 앞의 응답을 버리므로 같은 인스턴스를
    /// 돌려주면 두 번째 시도가 닫힌 것을 읽는다 — 실제 HttpClient 도 매번 새로 만든다.
    /// </summary>
    private static RecordingHandler Responds(Func<HttpResponseMessage> respond)
        => new(_ => respond());

    private static RecordingHandler Responds(HttpStatusCode status)
        => Responds(() => new HttpResponseMessage(status));

    private static RecordingHandler RespondsInOrder(params HttpResponseMessage[] responses)
    {
        var index = 0;
        return new RecordingHandler(_ => responses[Math.Min(index++, responses.Length - 1)]);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUri { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastContentType { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastContentType = request.Content?.Headers.ContentType?.ToString();
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return respond(request);
        }
    }
}
