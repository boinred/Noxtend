using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 설정 id → (종류, 평문 키).
///
/// Design Ref: §2.2 키의 수명
///
/// **평문 키가 Infrastructure 를 벗어나지 않는 지점이 여기 하나다.** 어댑터 생성
/// (<see cref="LlmProviderFactory"/>) 과 모델 목록 조회 (<see cref="ModelCatalog"/>) 가
/// 둘 다 복호화를 필요로 하는데, 각자 하면 "사용 중지 확인" 이나 "복호화 실패 메시지" 같은
/// 규칙이 두 곳으로 나뉜다. <c>internal</c> 이므로 이 어셈블리 밖에서는 보이지 않는다.
/// </summary>
internal sealed class ProviderCredentialResolver(
    IProviderConfigRepository configs,
    ISecretProtector protector)
{
    public async Task<ProviderCredential> ResolveAsync(Guid providerConfigId, CancellationToken ct)
    {
        var config = await configs.GetAsync(providerConfigId, ct)
                     ?? throw new ProviderCallFailedException("공급자 설정을 찾을 수 없습니다");

        if (!config.IsEnabled)
        {
            throw new ProviderCallFailedException("사용 중지된 공급자입니다");
        }

        return new ProviderCredential(config.Kind, Unprotect(config));
    }

    /// <summary>
    /// 복호화 실패는 대개 Data Protection 키 링이 사라진 경우다 (§7). 원문 예외를 그대로
    /// 올리면 암호학 스택 트레이스가 나오는데, 실제 원인은 볼륨 설정이다.
    /// </summary>
    private string Unprotect(ProviderConfig config)
    {
        try
        {
            return protector.Unprotect(config.ApiKeyCipher);
        }
        catch (Exception)
        {
            throw new ProviderCallFailedException(
                "저장된 키를 복호화할 수 없습니다. 보호 키가 유실되었을 수 있습니다");
        }
    }
}

internal sealed record ProviderCredential(ProviderKind Kind, string ApiKey);
