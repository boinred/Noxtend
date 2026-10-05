using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Domain.Mesh;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Tests;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 결과를 내려받는 경로.
///
/// Design Ref: §7.5 · §13.2 · Plan D-08 · NFR-07
///
/// **여기가 SSRF 방어가 걸린 자리다.** 내려받을 주소를 우리가 정하지 않는다 — 공급자가
/// 응답으로 준다. 그 값이 우리 네트워크 안쪽을 가리켜도 코드는 그저 GET 할 뿐이다.
///
/// 리다이렉트마다 다시 검사하는 것이 핵심이다. 첫 URL 만 보면 **허용된 host 가 사설
/// 주소로 넘겨주는 것**을 막지 못한다.
/// </summary>
public sealed class TripoResultDownloadTests
{
    // ─── 주소 검증 (§13.2) ───

    [Fact]
    public async Task PlainHttpResult_IsRejected()
    {
        var provider = Provider(Task("http://openapi.tripo3d.ai/x/model.glb"));

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.OpenResultAsync("task_1", default));

        // HTTPS 가 아니면 중간에서 바꿔치기할 수 있다
        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    [Fact]
    public async Task ResultFromAnotherHost_IsRejected()
    {
        var provider = Provider(Task("https://evil.example.com/model.glb"));

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.OpenResultAsync("task_1", default));

        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    /// <summary>
    /// **이것이 SSRF 다.** 사설 주소를 가리키면 우리 클러스터 안의 서비스를 찌를 수 있다.
    /// </summary>
    [Theory]
    [InlineData("https://127.0.0.1/model.glb")]
    [InlineData("https://10.0.0.5/model.glb")]
    [InlineData("https://192.168.1.10/model.glb")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    public async Task ResultPointingInside_IsRejected(string url)
    {
        var provider = Provider(Task(url));

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.OpenResultAsync("task_1", default));

        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    [Fact]
    public async Task AllowedSubdomain_IsAccepted()
    {
        var provider = Provider(new RoutingHandler
        {
            TaskJson = Task("https://cdn.tripo3d.ai/model.glb").TaskJson,
            Model = Glb(),
        });

        await using var result = await provider.OpenResultAsync("task_1", default);

        // 결과 CDN 은 하위 도메인으로 온다 — 정확히 일치만 허용하면 정상 결과를 버린다
        Assert.NotNull(result.Part(MeshArtifactKind.Glb)!.Content);
    }

    /// <summary>
    /// **리다이렉트마다 다시 본다.**
    ///
    /// 허용된 host 가 302 로 사설 주소를 가리키는 것이 전형적인 우회다. 첫 URL 만
    /// 검사하면 그대로 통과한다.
    /// </summary>
    [Fact]
    public async Task RedirectToAPrivateAddress_IsRejected()
    {
        var provider = Provider(new RoutingHandler
        {
            TaskJson = Task("https://cdn.tripo3d.ai/model.glb").TaskJson,
            RedirectTo = "https://169.254.169.254/latest/meta-data/",
        });

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.OpenResultAsync("task_1", default));

        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    /// <summary>
    /// 결과 CDN 에는 우리 키를 붙이지 않는다 (§13.1).
    ///
    /// 붙이면 남의 host 로 우리 자격증명이 나간다 — 리다이렉트가 걸리면 특히 그렇다.
    /// </summary>
    [Fact]
    public async Task ResultRequest_CarriesNoCredential()
    {
        var handler = new RoutingHandler
        {
            TaskJson = Task("https://cdn.tripo3d.ai/model.glb").TaskJson,
            Model = Glb(),
        };

        await using var _ = await Provider(handler).OpenResultAsync("task_1", default);

        Assert.Null(handler.ResultAuthorization);
    }

    // ─── 만료 (Plan D-08) ───

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ExpiredLink_FailsWithoutMakingANewPaidTask(HttpStatusCode status)
    {
        var provider = Provider(new RoutingHandler
        {
            TaskJson = Task("https://cdn.tripo3d.ai/model.glb").TaskJson,
            ModelStatus = status,
        });

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.OpenResultAsync("task_1", default));

        // 5분이 지났다. 자동으로 새 유료 작업을 만들면 사용자가 모르는 사이 과금된다
        Assert.Equal("MESH_RESULT_EXPIRED", failure.FailureCode);
        Assert.False(failure.CanRetry);
    }

    [Fact]
    public async Task ResultWithoutAModelLink_IsRejected()
    {
        var provider = Provider(new RoutingHandler
        {
            TaskJson = """{"code":0,"data":{"status":"success","output":{}}}""",
        });

        var failure = await Assert.ThrowsAsync<MeshProviderException>(
            () => provider.OpenResultAsync("task_1", default));

        Assert.Equal("MESH_RESULT_INVALID", failure.FailureCode);
    }

    // ─── 미리보기 (§7.5) ───

    [Fact]
    public async Task MissingPreview_DoesNotLoseTheModel()
    {
        var provider = Provider(new RoutingHandler
        {
            TaskJson = Task("https://cdn.tripo3d.ai/model.glb", "https://cdn.tripo3d.ai/p.png").TaskJson,
            Model = Glb(),
            PreviewStatus = HttpStatusCode.NotFound,
        });

        await using var result = await provider.OpenResultAsync("task_1", default);

        // 미리보기 하나 때문에 GLB 를 버리면 가장 비싼 결과가 부수적인 이미지로 날아간다
        Assert.NotNull(result.Part(MeshArtifactKind.Glb)!.Content);
        Assert.Null(result.Part(MeshArtifactKind.Preview)?.Content);
    }

    [Fact]
    public async Task PreviewIsOpenedWhenPresent()
    {
        var provider = Provider(new RoutingHandler
        {
            TaskJson = Task("https://cdn.tripo3d.ai/model.glb", "https://cdn.tripo3d.ai/p.png").TaskJson,
            Model = Glb(),
            Preview = [0x89, 0x50, 0x4E, 0x47],
        });

        await using var result = await provider.OpenResultAsync("task_1", default);

        Assert.NotNull(result.Part(MeshArtifactKind.Preview)?.Content);
    }

    // ─── 설정 ───

    private static TripoMeshProvider Provider(RoutingHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openapi.tripo3d.ai/v3/") },
            "test-key",
            new MeshGenerationOptions(),
            NullLogger<TripoMeshProvider>.Instance,
            retryDelay: TimeSpan.Zero);

    /// <summary>
    /// 성공한 작업 조회 응답.
    ///
    /// 보간 문자열을 쓰지 않는다 — JSON 의 중괄호와 보간 구문이 계속 부딪힌다.
    /// </summary>
    private static RoutingHandler Task(string modelUrl, string? previewUrl = null)
    {
        var output = "{\"model_url\":\"" + modelUrl + "\""
            + (previewUrl is null ? "" : ",\"rendered_image_url\":\"" + previewUrl + "\"")
            + "}";

        return new RoutingHandler
        {
            TaskJson = "{\"code\":0,\"data\":{\"status\":\"success\",\"output\":" + output + "}}",
        };
    }

    private static byte[] Glb() => FakeGlb.Bytes;

    /// <summary>
    /// 작업 조회와 결과 다운로드를 경로로 나눠 응답한다.
    ///
    /// **리다이렉트를 직접 흉내낸다.** `HttpClient` 의 자동 리다이렉트를 쓰면 우리
    /// 검사가 끼어들 자리가 없어져, 검증하려던 것이 검증되지 않는다.
    /// </summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public string TaskJson { get; init; } = "{}";
        public byte[]? Model { get; init; }
        public byte[]? Preview { get; init; }
        public HttpStatusCode ModelStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode PreviewStatus { get; init; } = HttpStatusCode.OK;

        /// <summary>설정하면 결과 요청이 이 주소로 한 번 넘어간다.</summary>
        public string? RedirectTo { get; init; }

        public string? ResultAuthorization { get; private set; }

        private bool redirected;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();

            if (uri.Contains("/v3/tasks/"))
            {
                return System.Threading.Tasks.Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(TaskJson, Encoding.UTF8, "application/json"),
                    });
            }

            // 결과 요청 — 여기부터가 검증 대상이다
            ResultAuthorization = request.Headers.Authorization?.ToString();

            if (RedirectTo is not null && !redirected)
            {
                redirected = true;
                var moved = new HttpResponseMessage(HttpStatusCode.Found);
                moved.Headers.Location = new Uri(RedirectTo);
                return System.Threading.Tasks.Task.FromResult(moved);
            }

            var isPreview = uri.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            var status = isPreview ? PreviewStatus : ModelStatus;

            if (status != HttpStatusCode.OK)
            {
                return System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(status));
            }

            var bytes = isPreview ? Preview : Model;

            return System.Threading.Tasks.Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes ?? []),
                });
        }
    }
}
