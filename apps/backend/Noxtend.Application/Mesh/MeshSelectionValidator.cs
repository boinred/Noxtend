using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Mesh;

/// <summary>
/// 3D 공급자·모델 선택이 쓸 수 있는 것인가.
///
/// Design Ref: §5.1 · D-11
///
/// **접수와 뒤늦은 지정이 같은 규칙을 써야 한다.** 나눠 두면 한쪽에서만 막히는 조합이
/// 생기고, 그 차이는 사용자가 경로를 바꿔 볼 때에야 드러난다.
///
/// **"이미지도 골랐는가" 는 여기 없다.** 접수에서는 필요하지만(3D 의 입력이 이미지라
/// 함께 골라야 한다), 뒤늦은 지정에서는 이미지가 **이미 존재하는지**를 도메인이 본다 —
/// 조건이 다르므로 호출자가 각자 확인한다.
/// </summary>
public sealed class MeshSelectionValidator(
    IProviderConfigRepository providers,
    IModelCatalog catalog)
{
    /// <summary>쓸 수 있으면 <c>null</c>, 아니면 실패 이유.</summary>
    public async Task<MeshSelectionError?> ValidateAsync(
        Guid providerConfigId, string? model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return new MeshSelectionError(
                ErrorCode.JobMeshModelUnavailable, "3D 공급자와 모델을 함께 선택해야 합니다");
        }

        var provider = await providers.GetAsync(providerConfigId, ct);

        if (provider is null)
        {
            return new MeshSelectionError(
                ErrorCode.JobMeshProviderNotFound, "3D 공급자를 찾을 수 없습니다");
        }

        if (!provider.IsEnabled)
        {
            return new MeshSelectionError(
                ErrorCode.JobMeshProviderDisabled, "사용 중지된 3D 공급자입니다");
        }

        IReadOnlyList<ProviderModel> available;
        try
        {
            available = await catalog.ListMeshModelsAsync(providerConfigId, ct);
        }
        catch (ProviderCallFailedException ex)
        {
            return new MeshSelectionError(ErrorCode.ProviderCallFailed, ex.Message);
        }

        return available.Any(candidate => candidate.Id == model.Trim())
            ? null
            : new MeshSelectionError(
                ErrorCode.JobMeshModelUnavailable,
                "선택한 3D 모델을 쓸 수 없습니다. 목록을 새로 불러오세요");
    }
}

/// <summary>
/// 검증 실패.
///
/// <c>Result&lt;T&gt;</c> 를 돌려주지 않는 이유는 호출자마다 <c>T</c> 가 다르기 때문이다 —
/// 접수는 작업을, 뒤늦은 지정은 다른 것을 돌려준다.
/// </summary>
public sealed record MeshSelectionError(string Code, string Message);
