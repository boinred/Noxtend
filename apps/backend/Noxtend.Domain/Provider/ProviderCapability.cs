namespace Noxtend.Domain.Provider;

/// <summary>
/// Application capabilities exposed by a provider adapter.
///
/// These values describe what Noxtend can use today, not every capability offered by
/// the vendor.
/// </summary>
public enum ProviderCapability
{
    TextAnalysis,
    ImageGeneration,

    /// <summary>4방향 이미지에서 3D mesh 를 만든다 (사이클 #10).</summary>
    MeshGeneration,

    /// <summary>
    /// 원본·렌더 두 이미지를 비교해 구조화 점수를 낸다 (background-similarity-tuning §7.2).
    /// 이미지 2장 입력과 structured output 을 모두 지원해야 한다 — Google 은 현재
    /// 이미지 생성 어댑터뿐이라 제외다.
    /// </summary>
    SimilarityEvaluation,
}

/// <summary>Single source of truth for provider-to-capability mapping.</summary>
public static class ProviderCapabilities
{
    private static readonly IReadOnlyList<ProviderCapability> OpenAi =
        [ProviderCapability.TextAnalysis, ProviderCapability.ImageGeneration,
         ProviderCapability.SimilarityEvaluation];

    private static readonly IReadOnlyList<ProviderCapability> Anthropic =
        [ProviderCapability.TextAnalysis, ProviderCapability.SimilarityEvaluation];

    private static readonly IReadOnlyList<ProviderCapability> Google =
        [ProviderCapability.TextAnalysis, ProviderCapability.ImageGeneration];

    // Tripo 는 3D 만 한다. 텍스트·이미지 목록에 섞이면 사용자가 그 단계에 고를 수 있고,
    // 그 오류는 접수가 아니라 실행 시점에야 드러난다
    private static readonly IReadOnlyList<ProviderCapability> Tripo =
        [ProviderCapability.MeshGeneration];

    // Meshy 도 3D 만 한다. 둘이 같은 목록에 나란히 서는 것이 사용자가 크레딧 있는 쪽을
    // 고를 수 있게 하는 전부다
    private static readonly IReadOnlyList<ProviderCapability> Meshy =
        [ProviderCapability.MeshGeneration];

    public static IReadOnlyList<ProviderCapability> For(ProviderKind kind) => kind switch
    {
        ProviderKind.OpenAI => OpenAi,
        ProviderKind.Anthropic => Anthropic,
        ProviderKind.Google => Google,
        ProviderKind.Tripo => Tripo,
        ProviderKind.Meshy => Meshy,
        _ => [],
    };

    public static bool Supports(ProviderKind kind, ProviderCapability capability)
        => For(kind).Contains(capability);
}
