namespace Noxtend.Domain.Provider;

/// <summary>
/// 공급자 종류. Design Ref: §3.1
///
/// 값이 늘면 Infrastructure 의 어댑터가 하나 늘 뿐이다 — Application 은 Factory 에
/// id 만 넘기므로 공급자가 무엇인지 모른다 (§3.2).
/// </summary>
public enum ProviderKind
{
    OpenAI,
    Anthropic,

    /// <summary>
    /// 사이클 #7 신설 — 이미지 생성 비교 대상 (Plan D-4).
    ///
    /// 이미지 생성은 공급자 간 화풍 차가 텍스트보다 훨씬 커서 "하나 붙이고 나중에 비교" 가
    /// 잘못된 기본값을 고착시킨다. 골든 세트로 고르려면 둘이 동시에 있어야 한다.
    /// </summary>
    Google,

    /// <summary>
    /// 사이클 #10 신설 — 4방향 이미지에서 3D 를 만드는 첫 공급자 (Plan D-03).
    ///
    /// 텍스트·이미지 공급자와 계열이 다르다. 같은 열거형에 두는 이유는 키 저장·암호화·
    /// 마스킹이 완전히 같기 때문이고, 나뉘는 것은 capability 뿐이다.
    /// </summary>
    Tripo,

    /// <summary>
    /// 사이클 #12 신설 — 두 번째 3D 공급자 (meshy-provider Plan D-01).
    ///
    /// **포트가 둘째 공급자를 전제로 만들어졌다는 주장이 여기서 검증된다.** Tripo 와
    /// 다른 것은 어댑터 안의 두 가지뿐이다 — 방향을 이름이 아니라 **위치**로 받고,
    /// 입력을 업로드가 아니라 **요청 본문**에 싣는다.
    /// </summary>
    Meshy,
}
