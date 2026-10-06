using Noxtend.Domain.Provider;

namespace Noxtend.Api.Contracts;

/// <summary>
/// Design Ref: §4.2 #9 · §7 — **이 응답에 키가 없다는 것이 계약이다.**
///
/// `ApiKeyCipher` 도 없다. 암호문이라도 내보내면 오프라인 공격 대상이 되고,
/// 화면이 그것을 쓸 일도 없다. 나가는 것은 마스킹된 끝 4자리뿐이다.
///
/// L1-B #24 가 이 타입의 어떤 필드에도 평문이 없음을 증명한다.
/// </summary>
public sealed record ProviderResponse(
    Guid Id,
    string DisplayName,
    string Kind,
    IReadOnlyList<string> Capabilities,
    string ApiKeyMasked,
    bool IsEnabled)
{
    public static ProviderResponse From(ProviderConfig config)
        => new(
            config.Id,
            config.DisplayName,
            Wire(config.Kind),
            ProviderCapabilities.For(config.Kind).Select(JobResponse.Wire).ToList(),
            config.MaskedApiKey,
            config.IsEnabled);

    /// <summary>
    /// 종류의 전송 표기.
    ///
    /// `JobResponse.Wire` 는 첫 글자만 내리는 camelCase 라 `OpenAI` 가 `openAI` 가 된다 —
    /// 프론트 유니온은 `'openai'` 이므로 머리글자 약어는 여기서 따로 다룬다.
    /// 능력표 응답도 같은 규칙을 써야 해서 `internal` 이다.
    /// </summary>
    internal static string Wire(ProviderKind kind) => kind switch
    {
        ProviderKind.OpenAI => "openai",
        _ => JobResponse.Wire(kind),
    };
}

/// <summary>
/// Design Ref: §4.2 #10·#11 — `apiKey` 가 null 이면 기존 키 유지 (수정 시).
///
/// **`model` 이 없다.** 공급자는 접속 수단이고 모델은 실행할 때 고른다 — 등록은
/// 이름·종류·키 세 가지다.
/// </summary>
public sealed record ProviderWriteRequest(
    string DisplayName,
    string Kind,
    string? ApiKey,
    bool IsEnabled = true);

/// <summary>
/// Design Ref: §4.2 #13
///
/// 기능별 모델 수는 연결 확인이 실제로 무엇을 검증했는지 보여준다.
/// </summary>
/// <summary>
/// 공급자 종류가 앱에서 무엇에 쓰이는가 — **등록 전에도 알아야 하는 값**.
///
/// 관리자 폼은 공급자를 만들기 전에 "이 종류는 무엇에 쓰이나" 를 보여줘야 하는데,
/// 그때는 <see cref="ProviderResponse.Capabilities"/> 가 없다. 그렇다고 화면이
/// 같은 표를 따로 들면 백엔드가 바뀔 때 조용히 어긋난다 —
/// `ProviderCapabilities.For` 가 유일한 출처로 남아야 한다.
/// </summary>
public sealed record ProviderKindCapabilitiesResponse(
    string Kind,
    IReadOnlyList<string> Capabilities)
{
    public static ProviderKindCapabilitiesResponse From(ProviderKind kind)
        => new(
            ProviderResponse.Wire(kind),
            ProviderCapabilities.For(kind).Select(JobResponse.Wire).ToList());
}

public sealed record ProviderTestResponse(
    bool Ok,
    int LatencyMs,
    int? TextModelCount,
    int? ImageModelCount,

    /// <summary>3D 공급자만 값을 갖는다 (사이클 #10). 나머지는 null 이다.</summary>
    int? MeshModelCount,

    /// <summary>
    /// 3D 공급자의 남은 크레딧. **못 읽었으면 null** — 0 과 구분해야 한다.
    /// </summary>
    int? MeshCreditBalance);

/// <summary>
/// Design Ref: §4.2 #14 (신설) — 고를 수 있는 모델 하나.
///
/// 이미 걸러진 목록이다. 추출이 요구하는 이미지 입력·구조화 출력을 갖춘 모델만 실린다.
/// </summary>
public sealed record ProviderModelResponse(
    string Id, string DisplayName, Domain.Ports.SpriteImageCapabilities? Sprite = null)
{
    public static ProviderModelResponse From(Domain.Ports.ProviderModel model)
        => new(model.Id, model.DisplayName, model.Sprite);
}
