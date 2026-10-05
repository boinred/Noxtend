using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Blob;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 실제 Tripo 로 한 건.
///
/// Design Ref: §14.9 · Plan NFR-08
///
/// **기본 테스트에서는 돌지 않는다.** 나머지 어댑터 테스트는 전부 가짜 HTTP 핸들러라
/// 크레딧을 쓰지 않지만, 이것 하나는 진짜 유료 작업을 만든다. 환경 변수 둘이 모두
/// 있을 때만 도는 이유가 그것이다.
///
/// <code>
/// TRIPO_SMOKE_TEST=1 TRIPO_API_KEY=... dotnet test --filter TripoSmokeTests
/// </code>
///
/// **왜 그래도 두는가.** 가짜 핸들러는 우리가 짐작한 응답 모양을 확인할 뿐이다. 공급자가
/// 실제로 무엇을 돌려주는지, 5분 만료가 정말 5분인지, GLB 가 정말 파싱되는지는 한 번은
/// 진짜로 눌러 봐야 안다. 그 한 번을 재현 가능한 형태로 남긴다.
/// </summary>
public sealed class TripoSmokeTests
{
    private const string EnabledVariable = "TRIPO_SMOKE_TEST";
    private const string KeyVariable = "TRIPO_API_KEY";

    /// <summary>
    /// 4방향 이미지 → GLB 관통.
    ///
    /// 확인하는 것은 여섯이다.
    /// ① 작업이 실제로 성공한다
    /// ② GLB 2.0 헤더와 길이가 맞는다
    /// ③ 미리보기가 있으면 저장된다
    /// ④ 자체 저장소에서 다시 읽힌다 — 공급자 URL 만료와 무관하다 (NFR-07)
    /// ⑤ 공급자 URL 문자열이 결과 어디에도 없다 (FR-10)
    /// ⑥ 실제 소모 크레딧이 기록된다
    /// </summary>
    [SkippableFact]
    public async Task RealTripoRun_ProducesAStoredGlb()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyVariable);

        // **건너뛰는 것이 기본이다.** 실패로 두면 CI 가 붉어지고, 그것을 무시하는 습관이
        // 생기면 진짜 실패도 함께 묻힌다
        Skip.If(
            Environment.GetEnvironmentVariable(EnabledVariable) != "1"
            || string.IsNullOrWhiteSpace(apiKey),
            $"{EnabledVariable}=1 과 {KeyVariable} 가 있을 때만 돕니다 — 실제 크레딧을 씁니다");

        var options = new MeshGenerationOptions();
        using var http = new HttpClient
        {
            BaseAddress = new Uri("https://openapi.tripo3d.ai/v3/"),
            Timeout = TimeSpan.FromSeconds(120),
        };

        var provider = new TripoMeshProvider(
            http, apiKey!, options, NullLogger<TripoMeshProvider>.Instance);

        // ① 네 방향을 올린다
        var handles = new Dictionary<ViewDirection, MeshInputHandle>();

        foreach (var direction in Directions)
        {
            using var image = TestImages.Jpeg(512, 512);

            handles[direction] = await provider.UploadInputAsync(
                new MeshInputUpload(
                    direction,
                    image,
                    "image/jpeg",
                    $"{direction.ToString().ToLowerInvariant()}.jpg",
                    image.Length),
                default);
        }

        // ② 유료 제출 — 여기서 크레딧이 나간다
        var submission = await provider.SubmitAsync(
            new MultiviewMeshRequest(handles, "P1-20260311", ModelSeed: 42, TextureSeed: 43),
            default);

        Assert.NotEmpty(submission.ProviderTaskId);

        // ③ 완료까지 조회
        var snapshot = await PollUntilDoneAsync(provider, submission.ProviderTaskId, options);

        Assert.Equal(MeshTaskState.Succeeded, snapshot.State);
        Assert.NotNull(snapshot.CreditsConsumed);

        // ④ 5분 안에 자체 저장소로
        var storage = new InMemoryMeshArtifactStorage();
        var runId = Guid.NewGuid();

        await using var result = await provider.OpenResultAsync(submission.ProviderTaskId, default);

        var model = await storage.SaveAsync(
            runId, MeshArtifactKind.Glb, result.Part(MeshArtifactKind.Glb)!.Content,
            null, options.MaxModelBytes, default);

        // ⑤ 저장된 바이트가 정말 glTF 2.0 이다
        var bytes = storage.Blobs[model.BlobKey];

        Assert.Equal("glTF"u8.ToArray(), bytes[..4]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)));
        Assert.Equal((uint)bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)));

        // ⑥ 공급자 링크가 결과에 남지 않았다 — 남으면 5분 뒤에 죽은 값을 들게 된다
        Assert.DoesNotContain("tripo3d.ai", model.BlobKey);
        Assert.StartsWith($"meshes/{runId:n}/", model.BlobKey);

        if (result.Part(MeshArtifactKind.Preview)?.Content is { } preview)
        {
            var stored = await storage.SaveAsync(
                runId, MeshArtifactKind.Preview, preview,
                result.Part(MeshArtifactKind.Preview)?.ContentType, options.MaxPreviewBytes, default);

            Assert.True(storage.Blobs.ContainsKey(stored.BlobKey));
        }
    }

    private static readonly ViewDirection[] Directions =
        [ViewDirection.Front, ViewDirection.Right, ViewDirection.Back, ViewDirection.Left];

    /// <summary>
    /// 공급자 권장 간격으로 조회한다.
    ///
    /// 상한을 두는 이유는 테스트가 영원히 매달리지 않게 하기 위해서다 — 넘으면 외부
    /// 작업은 계속 돌지만 우리는 실패로 본다.
    /// </summary>
    private static async Task<MeshTaskSnapshot> PollUntilDoneAsync(
        IMeshProvider provider, string providerTaskId, MeshGenerationOptions options)
    {
        var deadline = DateTimeOffset.UtcNow + options.RunTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = await provider.GetTaskAsync(providerTaskId, default);

            if (snapshot.State is not (MeshTaskState.Pending or MeshTaskState.Running))
            {
                return snapshot;
            }

            await Task.Delay(options.PollInterval);
        }

        throw new TimeoutException($"{options.RunTimeoutSeconds}초 안에 끝나지 않았습니다");
    }
}
