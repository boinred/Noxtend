using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// Design Ref: §6.2 · §12.2
///
/// **`Llm:UseFake` 가 여기도 가른다.** 텍스트만 가짜이고 3D 만 실제로 과금되는 조합을
/// 만들지 않는다 — 로컬에서 파이프라인을 한 번 돌릴 때마다 돈이 나가면 아무도 돌리지 않는다.
///
/// 지원하지 않는 공급자 종류는 확정 실패다. 빈 결과로 흡수하면 워커가 집어 들고
/// 알 수 없는 이유로 실패한다.
/// </summary>
internal sealed class MeshProviderFactory(
    ProviderCredentialResolver credentials,
    IHttpClientFactory httpClients,
    MeshGenerationOptions options,
    ILoggerFactory loggers,
    bool useFake) : IMeshProviderFactory
{
    /// <summary>외부 작업이 길어 기본 100초로는 정상 호출이 끊긴다 (§7.1).</summary>
    public const string HttpClientName = "tripo";

    /// <summary>Meshy 는 base address 가 달라 클라이언트를 따로 둔다.</summary>
    public const string MeshyHttpClientName = "meshy";

    public async Task<IMeshProvider> CreateAsync(
        Guid providerConfigId, string model, CancellationToken ct)
    {
        var credential = await credentials.ResolveAsync(providerConfigId, ct);

        if (useFake)
        {
            // **가짜도 공급자 차이를 지킨다** — Meshy 만 FBX 를 낸다 (Plan D-05).
            // 여기서 뭉개면 "FBX 는 있을 때만" 규칙을 E2E 가 검증하지 못한다
            return new FakeMeshProvider(producesFbx: credential.Kind == ProviderKind.Meshy);
        }

        return credential.Kind switch
        {
            ProviderKind.Tripo => new TripoMeshProvider(
                httpClients.CreateClient(HttpClientName),
                credential.ApiKey,
                options,
                loggers.CreateLogger<TripoMeshProvider>()),

            ProviderKind.Meshy => new MeshyMeshProvider(
                httpClients.CreateClient(MeshyHttpClientName),
                credential.ApiKey,
                options,
                loggers.CreateLogger<MeshyMeshProvider>()),

            _ => throw new ProviderCallFailedException(
                $"3D 를 만들 수 없는 공급자입니다: {credential.Kind}"),
        };
    }
}
