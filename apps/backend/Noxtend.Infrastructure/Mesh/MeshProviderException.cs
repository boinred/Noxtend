using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// 3D 공급자 호출 실패.
///
/// Design Ref: §7.6 · §13.1
///
/// **원문 메시지를 나르지 않는다.** Tripo 의 `message` 와 `suggestion` 에는 키나 URL 이
/// 섞여 오는 경우가 있다. 여기 담기는 것은 우리가 정한 코드와, 공급자 문의에 쓸
/// 숫자 코드·요청 ID 뿐이다.
///
/// <see cref="CanRetry"/> 는 **호출자가 물어볼 수 있어야 하는 값**이다. 같은 실패라도
/// 429 는 다시 보내도 되고 credit 부족은 아니다. 메시지를 문자열로 뒤져서 판단하게
/// 두면 언젠가 틀린다.
/// </summary>
public sealed class MeshProviderException(
    string failureCode,
    string message,
    bool canRetry,
    int? providerCode = null,
    string? providerRequestId = null) : Exception(message), IMeshFailure
{
    public string FailureCode { get; } = failureCode;

    /// <summary>같은 요청을 다시 보내도 되는가.</summary>
    public bool CanRetry { get; } = canRetry;

    /// <summary>공급자 진단 코드. 로그에만 남는다.</summary>
    public int? ProviderCode { get; } = providerCode;

    /// <summary>공급자 문의용 요청 ID.</summary>
    public string? ProviderRequestId { get; } = providerRequestId;
}
