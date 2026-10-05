using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 단계의 활성 프롬프트를 준다.
///
/// Design Ref: §3.3 · §2.4 — **Port 가 소비자 쪽에 있는 것이 Option B 의 장치다.**
///
/// 파이프라인은 프롬프트를 써야 하고, 튜닝은 파이프라인을 알아야 한다. 둘 다 상대를
/// 참조하면 순환이다. 인터페이스를 여기(파이프라인)에 두고 튜닝이 구현하면 방향이
/// 한쪽으로 정리된다 — 파이프라인은 튜닝이라는 것이 존재하는지도 모른다.
/// </summary>
public interface IPromptCatalog
{
    /// <summary>
    /// [목적] 작업 실행·접수 차단이 "이 단계·카테고리로 지금 쓸 프롬프트"를 얻는 유일한 통로입니다.
    /// [핵심 동작] 그 카테고리 전용 활성이 있으면 그것을, 없으면 기본으로 폴백합니다(폴백 규칙이 사는 단 한 곳).
    /// <paramref name="category"/> 는 non-nullable — 소비 측 작업은 항상 카테고리를 가지므로 "카테고리 없음"이 없습니다.
    /// [반환] 실행에 쓸 프롬프트 스냅숏, 전용·기본 둘 다 없으면 <c>null</c>(호출자가 <c>PROMPT_NOT_ACTIVE</c> 로 바꿉니다).
    ///
    /// Design Ref: §3.1(폴백 위치).
    /// </summary>
    Task<PromptSnapshot?> GetActiveAsync(LlmOperationKind kind, AssetCategory category, CancellationToken ct);
}

/// <summary>
/// 실행 시점에 고정된 프롬프트 한 벌.
///
/// **<paramref name="VersionId"/> 가 함께 오는 것이 계약이다.** 이것을 내역에 남겨야
/// "이 결과가 어느 프롬프트에서 나왔나" 에 답할 수 있고, 그래야 비교가 성립한다 (FR-10).
/// 실행 도중 활성 버전이 바뀌어도 이 스냅숏은 흔들리지 않는다.
///
/// <paramref name="System"/>·<paramref name="User"/> 는 **아직 렌더되지 않은** 원문이다.
/// 변수에 무엇을 끼울지는 단계가 안다.
/// </summary>
public sealed record PromptSnapshot(
    Guid VersionId,
    int Version,
    string System,
    string User,
    string JsonSchema);
