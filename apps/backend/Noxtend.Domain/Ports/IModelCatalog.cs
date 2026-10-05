namespace Noxtend.Domain.Ports;

/// <summary>
/// 공급자가 지원하는 모델 목록.
///
/// Design Ref: §2.2 키의 수명 · §3.2
///
/// <see cref="ILlmProviderFactory"/> 와 같은 이유로 설정 id 만 받는다 —
/// 조회·복호화·호출이 Infrastructure 안에서 끝나야 평문 키가 계층을 건너오지 않는다.
///
/// 실패는 <see cref="ProviderCallFailedException"/> 다 — 빈 목록으로 흡수하지 않는다.
/// "키가 틀렸다" 와 "키는 맞는데 쓸 모델이 없다" 는 사용자가 할 일이 다르므로
/// 호출자가 구별할 수 있어야 한다. 두 경우를 모두 빈 배열로 만들면 그 정보가 사라진다.
///
/// **어댑터가 이미 걸러서 준다.** 추출 단계는 이미지를 읽으므로 비전이 없는 모델은
/// 고를 수 있어도 실행 후에야 실패한다. 걸러내는 판단은 공급자마다 다르므로
/// (Anthropic 은 capability 를 노출하고 OpenAI 는 아니다) 어댑터의 몫이다.
/// </summary>
public interface IModelCatalog
{
    Task<IReadOnlyList<ProviderModel>> ListAsync(Guid providerConfigId, CancellationToken ct);

    /// <summary>
    /// 이미지 생성 모델 목록 (사이클 #7 §4.2 #7).
    ///
    /// **텍스트 목록과 나눈 이유**: 한 목록에 섞으면 사용자가 텍스트 단계에 이미지 모델을
    /// 고를 수 있고, 그 오류는 접수가 아니라 실행 시점에야 드러난다. 메서드가 둘이면
    /// 화면이 어느 쪽을 물었는지가 호출에 드러난다.
    /// </summary>
    Task<IReadOnlyList<ProviderModel>> ListImageModelsAsync(
        Guid providerConfigId, CancellationToken ct);

    /// <summary>
    /// 3D 공급자의 남은 크레딧 (사이클 #12 후속).
    ///
    /// **이미 부르고 있던 값이다.** 키 확인용 잔액 조회의 응답에 들어 있는데 그동안
    /// 상태 코드만 보고 숫자를 버렸다. 3D 는 파츠 하나가 크레딧 30 이라 남은 양을
    /// 모르면 작업 도중에 멈춘다.
    ///
    /// 읽을 수 없으면 <c>null</c> 이다 — 0 과 구분해야 한다. 0 은 "다 썼다" 이고
    /// null 은 "모른다" 다.
    /// </summary>
    Task<int?> GetMeshCreditBalanceAsync(Guid providerConfigId, CancellationToken ct);

    /// <summary>
    /// 3D 생성 모델 목록 (사이클 #10 §10.1).
    ///
    /// 이미지 목록과 나눈 이유는 위와 같다 — 한 목록에 섞으면 사용자가 이미지 단계에
    /// 3D 모델을 고를 수 있고, 그 오류는 실행 시점에야 드러난다.
    /// </summary>
    Task<IReadOnlyList<ProviderModel>> ListMeshModelsAsync(
        Guid providerConfigId, CancellationToken ct);
}

/// <summary>
/// 고를 수 있는 모델 하나.
///
/// <paramref name="Id"/> 는 API 에 넘기는 값이고 <paramref name="DisplayName"/> 은 사람이 읽는 이름이다
/// (`claude-opus-5` 와 "Claude Opus 5"). 화면은 이름을 보여주고 id 를 보낸다.
/// </summary>
public sealed record ProviderModel(string Id, string DisplayName);
