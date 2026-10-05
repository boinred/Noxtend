using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// Meshy 어댑터 — 외부 호출 없이 계약만 본다.
///
/// Design Ref: §5 · §6 · §11.2 · Plan NFR-07
///
/// **가장 값어치 있는 검사는 A-01 이다.** Meshy 는 방향을 배열 위치로 받는데, 순서를
/// 틀려도 예외가 나지 않는다 — 유효한 GLB 가 나오고 좌우만 뒤집혀 있다. 그것은 결과물을
/// 열어 봐야만 아는 오류라, 여기서 배열을 직접 못박는 것이 유일한 자동 방어다.
/// </summary>
public sealed class MeshyMeshProviderTests
{
    // ─── A-01·A-02 방향과 위치 ───

    /// <summary>
    /// **첫 장이 정면이고, 순서는 물체를 한 방향으로 도는 순서다** (Plan D-02 · D-12).
    ///
    /// `ViewDirection` 선언 순서는 <c>Front · Right · Back · Left</c> 라 둘째와 넷째가
    /// 다르다. 열거형 인덱스로 배열을 만드는 코드는 컴파일도 되고 예외도 안 나면서
    /// 좌우가 뒤집힌 mesh 를 낸다.
    /// </summary>
    [Fact]
    public async Task ImageOrder_IsFrontLeftBackRight()
    {
        var handler = new RecordingHandler(Accepted());
        var provider = Provider(handler);

        await provider.SubmitAsync(Request(), default);

        var images = Images(handler.LastBody!);

        Assert.Equal(
            ["front-data", "left-data", "back-data", "right-data"],
            images);
    }

    /// <summary>열거형 선언 순서와 다르다는 사실 자체를 못박는다 — 이것이 함정의 근원이다.</summary>
    [Fact]
    public void DeclarationOrder_DiffersFromTheWireOrder()
    {
        var declared = Enum.GetValues<ViewDirection>();

        Assert.Equal(
            [ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left],
            declared);
    }

    /// <summary>
    /// meshy-7 은 첫 장만 정면이면 되고 나머지 순서·개수는 무관하다 (2026-09-17 조회,
    /// https://docs.meshy.ai/en/api/multi-image-to-3d). 빈 슬롯을 정면으로 채우던 예전
    /// 방어 코드를 제거하고, 실제로 있는 이미지만 보낸다.
    /// </summary>
    [Fact]
    public async Task PartialInputs_OnlySendsProvidedImages()
    {
        var handler = new RecordingHandler(Accepted());
        var provider = Provider(handler);

        var inputs = new Dictionary<ViewDirection, MeshInputHandle>
        {
            [ViewDirection.Front] = new("front-data", IsDurable: false),
            [ViewDirection.Back] = new("back-data", IsDurable: false),
        };

        await provider.SubmitAsync(new MultiviewMeshRequest(inputs, "meshy-7", 1, 2), default);

        Assert.Equal(["front-data", "back-data"], Images(handler.LastBody!));
    }

    [Fact]
    public async Task MissingFrontImage_IsRejectedWithoutCallingTheProvider()
    {
        var handler = new RecordingHandler(Accepted());
        var provider = Provider(handler);

        var inputs = new Dictionary<ViewDirection, MeshInputHandle>
        {
            [ViewDirection.Left] = new("left-data", IsDurable: false),
        };

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.SubmitAsync(
                new MultiviewMeshRequest(inputs, "meshy-7", 1, 2), default));

        Assert.Equal("MESH_INPUT_REJECTED", failure.FailureCode);
        Assert.Equal(0, handler.Calls);
    }

    // ─── A-03 고정 품질 ───

    [Fact]
    public async Task FixedQualityOptions_AreOnTheWire()
    {
        var handler = new RecordingHandler(Accepted());

        await Provider(handler).SubmitAsync(Request(), default);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;

        Assert.Equal("meshy-7", root.GetProperty("ai_model").GetString());
        Assert.True(root.GetProperty("should_texture").GetBoolean());
        Assert.True(root.GetProperty("enable_pbr").GetBoolean());
        Assert.Equal(2048, root.GetProperty("texture_image_resolution").GetInt32());
        Assert.Equal("triangle", root.GetProperty("topology").GetString());
        Assert.Equal(5000, root.GetProperty("target_polycount").GetInt32());
        Assert.True(root.GetProperty("should_remesh").GetBoolean());
    }

    // ─── A-04 상태 매핑 ───

    [Theory]
    [InlineData("PENDING", MeshTaskState.Pending)]
    [InlineData("IN_PROGRESS", MeshTaskState.Running)]
    [InlineData("SUCCEEDED", MeshTaskState.Succeeded)]
    [InlineData("FAILED", MeshTaskState.Failed)]
    [InlineData("CANCELED", MeshTaskState.Canceled)]
    public async Task ProviderStates_BecomeNeutralStates(string status, MeshTaskState expected)
    {
        var handler = new RecordingHandler(
            Ok($$"""{"id":"t1","status":"{{status}}","progress":42,"consumed_credits":30}"""));

        var snapshot = await Provider(handler).GetTaskAsync("t1", default);

        Assert.Equal(expected, snapshot.State);
        Assert.Equal(42, snapshot.Progress);
        Assert.Equal(30, snapshot.CreditsConsumed);
    }

    /// <summary>모르는 상태는 "아직 모른다" 다 — 실패로 읽으면 돌고 있는 유료 작업을 버린다.</summary>
    [Fact]
    public async Task UnknownState_IsTreatedAsPending()
    {
        var handler = new RecordingHandler(Ok("""{"status":"WHO_KNOWS"}"""));

        Assert.Equal(MeshTaskState.Pending, (await Provider(handler).GetTaskAsync("t1", default)).State);
    }

    // ─── A-05 오류 매핑 (§6) ───

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "MESH_AUTH_FAILED", false)]
    [InlineData(HttpStatusCode.PaymentRequired, "{}", "MESH_CREDITS_INSUFFICIENT", false)]
    [InlineData(HttpStatusCode.NotFound, "{}", "MESH_MODEL_DEPRECATED", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", "MESH_RATE_LIMITED", true)]
    [InlineData(HttpStatusCode.BadRequest, "{}", "MESH_INPUT_REJECTED", false)]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"insufficient credits"}""",
        "MESH_CREDITS_INSUFFICIENT", false)]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"content policy violation"}""",
        "MESH_CONTENT_REJECTED", false)]
    public async Task ProviderErrors_BecomeNeutralCodes(
        HttpStatusCode status, string body, string expected, bool canRetry)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal(expected, failure.FailureCode);
        Assert.Equal(canRetry, failure.CanRetry);
    }

    /// <summary>
    /// **5xx 는 제출 결과를 알 수 없다** (Plan D-06).
    ///
    /// 작업이 만들어졌는지 모르므로 자동 재제출을 막는 것이 여기서 할 일의 전부다.
    /// </summary>
    [Fact]
    public async Task ServerErrorOnSubmit_IsSubmissionUnknown()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal("MESH_SUBMISSION_UNKNOWN", failure.FailureCode);
        Assert.False(failure.CanRetry);
    }

    /// <summary>성공 응답인데 작업 ID 가 없으면 만들어졌는지 알 수 없다.</summary>
    [Fact]
    public async Task SuccessWithoutTaskId_IsSubmissionUnknown()
    {
        var handler = new RecordingHandler(Ok("""{"ok":true}"""));

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).SubmitAsync(Request(), default));

        Assert.Equal("MESH_SUBMISSION_UNKNOWN", failure.FailureCode);
    }

    // ─── A-06 본문 상한 (D-13) ───

    /// <summary>
    /// **상한을 넘으면 호출이 나가지 않는다.**
    ///
    /// 검사가 호출 뒤에 있으면 돈이 나간 뒤에 실패한다. 그리고 확정 실패다 — 같은
    /// 이미지를 다시 보내면 같은 크기다.
    /// </summary>
    [Fact]
    public async Task OversizedBody_FailsBeforeTheCallAndDoesNotRetry()
    {
        var handler = new RecordingHandler(Accepted());
        var provider = Provider(handler, new MeshGenerationOptions { MaxRequestBodyBytes = 512 });

        var big = new string('A', 4096);
        var inputs = new Dictionary<ViewDirection, MeshInputHandle>
        {
            [ViewDirection.Front] = new(big, IsDurable: false),
        };

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.SubmitAsync(new MultiviewMeshRequest(inputs, "meshy-7", 1, 2), default));

        Assert.Equal("MESH_REQUEST_TOO_LARGE", failure.FailureCode);
        Assert.False(failure.CanRetry);

        // 이것이 이 검사의 요점이다 — 유료 호출이 나가지 않았다
        Assert.Equal(0, handler.Calls);
    }

    // ─── A-07 이상한 본문 ───

    /// <summary>
    /// 프록시가 200 에 HTML 오류 페이지를 실어 보내는 일이 실제로 있었다.
    ///
    /// 여기가 예외로 죽으면 분류되지 않은 실패라 공정이 알 수 없는 이유로 끝난다.
    /// </summary>
    [Fact]
    public async Task NonJsonSuccessBody_DoesNotThrow()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>gateway</html>", Encoding.UTF8, "text/html"),
        });

        var snapshot = await Provider(handler).GetTaskAsync("t1", default);

        Assert.Equal(MeshTaskState.Pending, snapshot.State);
        Assert.Equal(0, snapshot.Progress);
    }

    // ─── A-08 핸들 내구성 (D-10) ───

    /// <summary>
    /// **아무것도 올리지 않는다.** 그래서 핸들이 비내구적이고, 저장되면 안 된다.
    /// </summary>
    [Fact]
    public async Task InputHandle_IsBase64AndNotDurable()
    {
        var handler = new RecordingHandler(Accepted());

        using var image = new MemoryStream([1, 2, 3, 4]);
        var handle = await Provider(handler).UploadInputAsync(
            new MeshInputUpload(ViewDirection.Front, image, "image/png", "front.png", 4), default);

        Assert.False(handle.IsDurable);
        Assert.StartsWith("data:image/png;base64,", handle.Value);
        Assert.Equal("AQIDBA==", handle.Value["data:image/png;base64,".Length..]);

        // 업로드 endpoint 가 없다 — 네트워크를 타면 그 자체가 결함이다
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task EmptyInput_IsRejected()
    {
        var handler = new RecordingHandler(Accepted());

        using var empty = new MemoryStream();

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).UploadInputAsync(
                new MeshInputUpload(ViewDirection.Front, empty, "image/png", "front.png", 0), default));

        Assert.Equal("MESH_INPUT_REJECTED", failure.FailureCode);
    }

    // ─── A-09·A-10 결과 ───

    [Fact]
    public async Task Result_CarriesGlbFbxAndPreview()
    {
        var handler = new ResultHandler(
            """
            {"id":"t1","status":"SUCCEEDED",
             "model_urls":{"glb":"https://assets.meshy.ai/m.glb","fbx":"https://assets.meshy.ai/m.fbx"},
             "thumbnail_url":"https://assets.meshy.ai/t.png"}
            """);

        await using var result = await Provider(handler).OpenResultAsync("t1", default);

        Assert.Equal(3, result.Parts.Count);
        Assert.NotNull(result.Part(MeshArtifactKind.Glb));
        Assert.NotNull(result.Part(MeshArtifactKind.Fbx));
        Assert.NotNull(result.Part(MeshArtifactKind.Preview));
    }

    /// <summary>FBX 나 미리보기가 없어도 GLB 는 온다 — 하나 때문에 가장 비싼 결과를 버리지 않는다.</summary>
    [Fact]
    public async Task ResultWithoutFbx_StillCarriesGlb()
    {
        var handler = new ResultHandler(
            """
            {"id":"t1","status":"SUCCEEDED",
             "model_urls":{"glb":"https://assets.meshy.ai/m.glb"}}
            """);

        await using var result = await Provider(handler).OpenResultAsync("t1", default);

        Assert.Single(result.Parts);
        Assert.NotNull(result.Part(MeshArtifactKind.Glb));
    }

    [Fact]
    public async Task ResultWithoutGlb_IsRejected()
    {
        var handler = new ResultHandler("""{"id":"t1","status":"SUCCEEDED","model_urls":{}}""");

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).OpenResultAsync("t1", default));

        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    // ─── A-11 SSRF (D-16) ───

    /// <summary>
    /// **공급자가 준 URL 이라 우리 네트워크 안쪽을 가리킬 수 있다** (§13.2).
    ///
    /// 허용 목록에 Meshy host 가 있어야 정상 결과가 통과하고, 없는 host 는 막혀야 한다.
    /// </summary>
    [Fact]
    public async Task ResultFromAnUnexpectedHost_IsRefused()
    {
        var handler = new ResultHandler(
            """
            {"id":"t1","status":"SUCCEEDED",
             "model_urls":{"glb":"https://evil.example.com/m.glb"}}
            """);

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => Provider(handler).OpenResultAsync("t1", default));

        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    [Fact]
    public void DefaultAllowedHosts_IncludeBothProviders()
    {
        var hosts = new MeshGenerationOptions().AllowedResultHosts;

        // 빠뜨리면 그 공급자의 결과를 한 건도 못 받는다
        Assert.Contains("tripo3d.ai", hosts);
        Assert.Contains("meshy.ai", hosts);
    }

    // ─── 설정 ───

    private static MeshyMeshProvider Provider(
        HttpMessageHandler handler, MeshGenerationOptions? options = null)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.meshy.ai/openapi/v1/") },
            "test-meshy-key",
            options ?? new MeshGenerationOptions(),
            NullLogger<MeshyMeshProvider>.Instance,
            retryDelay: TimeSpan.Zero,
            onDelay: _ => { });

    private static MultiviewMeshRequest Request()
        => new(
            new Dictionary<ViewDirection, MeshInputHandle>
            {
                [ViewDirection.Front] = new("front-data", IsDurable: false),
                [ViewDirection.Right] = new("right-data", IsDurable: false),
                [ViewDirection.Back] = new("back-data", IsDurable: false),
                [ViewDirection.Left] = new("left-data", IsDurable: false),
            },
            "meshy-7",
            ModelSeed: 123456,
            TextureSeed: 654321);

    private static string[] Images(string body)
    {
        using var document = JsonDocument.Parse(body);

        return [.. document.RootElement.GetProperty("image_urls")
            .EnumerateArray()
            .Select(element => element.GetString()!)];
    }

    private static HttpResponseMessage Accepted() => Ok("""{"result":"task_abc"}""");

    private static HttpResponseMessage Ok(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    /// <summary>보낸 본문과 호출 횟수를 기억하는 handler.</summary>
    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return response;
        }
    }

    /// <summary>작업 조회에는 JSON 을, 자산 URL 에는 바이트를 준다.</summary>
    private sealed class ResultHandler(string taskJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/openapi/v1/multi-image-to-3d/"))
            {
                return Task.FromResult(Ok(taskJson));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4]),
            });
        }
    }
}
