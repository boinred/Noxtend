namespace Noxtend.Infrastructure.Llm;

/// <summary>
/// 마이그레이션이 심는 초기 단가.
///
/// **여기가 정본이 아니다.** 프롬프트와 같은 구조로, 코드에 두는 이유는 마이그레이션이
/// 초기값을 심어야 하기 때문이다 — 한 번 심으면 이후 정본은 DB 이고 수정은 관리자
/// 화면에서 한다. 여기 값을 고쳐도 이미 배포된 환경은 바뀌지 않는다.
///
/// 값은 2026-08-01 공식 단가표 기준이다. `gpt-5.4-cyber` 는 표에 단가가 비어 있어
/// 넣지 않았다 — 빈칸이 곧 "모른다" 이고, 모르는 것은 미등록으로 남는 편이 낫다.
/// Anthropic 장문 구간은 확인하지 못해 비워 두었다.
/// </summary>
public static class SeedModelPrices
{
    /// <summary>
    /// 초기 단가의 시행일.
    ///
    /// 이보다 오래된 호출은 <c>ModelPriceBook</c> 이 가장 오래된 행으로 소급한다 —
    /// 이 표가 생기기 전에 쌓인 호출을 통째로 미등록 처리하지 않으려는 것이다.
    /// </summary>
    public static readonly DateTimeOffset EffectiveFrom = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    public sealed record Row(
        Guid Id,
        string Model,
        decimal Input,
        decimal Output,
        int? LongFrom,
        decimal? LongInput,
        decimal? LongOutput,
        string Note,
        /// <summary>장당 USD. 토큰 과금 모델이면 null (사이클 #7 · Plan D-8).</summary>
        decimal? PerImage = null);

    /// <summary>OpenAI 장문 단가가 시작되는 입력 토큰 수.</summary>
    private const int OpenAiLongFrom = 272_000;

    private const string Anthropic = "Anthropic 공식 단가 (2026-08-01)";
    private const string OpenAi = "OpenAI 공식 단가 (2026-08-01)";

    public static IReadOnlyList<Row> All =>
    [
        // ── Anthropic ── 장문 구간은 확인하지 못해 비워 둔다
        Make(01, "claude-fable-5", 10m, 50m, null, null, null, Anthropic),
        Make(02, "claude-opus-5", 5m, 25m, null, null, null, Anthropic),
        Make(03, "claude-opus-4-8", 5m, 25m, null, null, null, Anthropic),
        Make(04, "claude-opus-4-7", 5m, 25m, null, null, null, Anthropic),
        Make(05, "claude-opus-4-6", 5m, 25m, null, null, null, Anthropic),
        Make(06, "claude-opus-4-5", 5m, 25m, null, null, null, Anthropic),
        Make(07, "claude-opus-4-1", 15m, 75m, null, null, null, Anthropic),
        Make(08, "claude-sonnet-5", 3m, 15m, null, null, null, Anthropic),
        Make(09, "claude-sonnet-4-6", 3m, 15m, null, null, null, Anthropic),
        Make(10, "claude-sonnet-4-5", 3m, 15m, null, null, null, Anthropic),
        Make(11, "claude-haiku-4-5", 1m, 5m, null, null, null, Anthropic),

        // ── OpenAI ── 5.4·5.5·5.6 계열은 272K 초과 입력에 단가가 뛴다
        Make(20, "gpt-5", 1.25m, 10m, null, null, null, OpenAi),
        Make(21, "gpt-5-mini", 0.25m, 2m, null, null, null, OpenAi),
        Make(22, "gpt-5-nano", 0.05m, 0.40m, null, null, null, OpenAi),
        Make(23, "gpt-5.1", 1.25m, 10m, null, null, null, OpenAi),
        Make(24, "gpt-5.2", 1.75m, 14m, null, null, null, OpenAi),
        Make(25, "gpt-5.3-codex", 1.75m, 14m, null, null, null, OpenAi),
        Make(26, "gpt-5.3-chat-latest", 1.75m, 14m, null, null, null, OpenAi),
        Make(27, "gpt-5.4", 2.50m, 15m, OpenAiLongFrom, 5m, 22.50m, OpenAi),
        Make(28, "gpt-5.4-mini", 0.75m, 4.50m, null, null, null, OpenAi),
        Make(29, "gpt-5.4-nano", 0.20m, 1.25m, null, null, null, OpenAi),
        Make(30, "gpt-5.4-pro", 30m, 180m, OpenAiLongFrom, 60m, 270m, OpenAi),
        Make(31, "gpt-5.5", 5m, 30m, OpenAiLongFrom, 10m, 45m, OpenAi),
        Make(32, "gpt-5.6-sol", 5m, 30m, OpenAiLongFrom, 10m, 45m, OpenAi),
        Make(33, "gpt-5.6-terra", 2m, 12m, OpenAiLongFrom, 4m, 18m, OpenAi),
        Make(34, "gpt-5.6-luna", 0.20m, 1.20m, OpenAiLongFrom, 0.40m, 1.80m, OpenAi),
    ];

    /// <summary>
    /// 이미지 생성 단가 (사이클 #7). 장당 과금이라 토큰 칸이 0 이다.
    ///
    /// **<see cref="All"/> 과 나눠 둔 것이 의도적이다.** 저 목록은 사이클 #6 의
    /// `ModelPrices` 마이그레이션이 순회하는데, 그 마이그레이션은 `PerImage` 열이 생기기
    /// **전**에 돈다. 한 목록에 섞으면 신규 DB 에서 이미지 행이 장당 단가 없이 들어가
    /// 조용히 미등록이 된다.
    ///
    /// 해상도·품질에 따라 실제 단가가 달라지지만 서버가 크기를 고정하므로(§2.3 A-8)
    /// 그 하나만 심는다. 크기를 사용자 선택으로 열면 여기가 행 여럿이 된다.
    /// </summary>
    public static IReadOnlyList<Row> Images =>
    [
        Image(40, "gpt-image-1", 0.040m, OpenAiImage),
        Image(41, "gpt-image-1-mini", 0.011m, OpenAiImage),
        Image(42, "gemini-3-pro-image-preview", 0.134m, GoogleImage),
        Image(43, "gemini-2.5-flash-image", 0.039m, GoogleImage),
    ];

    /// <summary>
    /// 카탈로그에는 있는데 <see cref="Images"/> 에는 없던 모델들 (사이클 #9).
    ///
    /// **<see cref="Images"/> 에 더하지 않고 목록을 새로 판 이유**는 저 목록을 사이클 #7 의
    /// `PartGeneration` 마이그레이션이 순회하기 때문이다. 거기에 행을 보태면 신규 DB 에서
    /// 그 마이그레이션과 이 행들을 심는 마이그레이션이 같은 행을 두 번 넣어
    /// `IX_ModelPrices_ModelEffectiveFrom` 유니크 인덱스에 걸린다.
    ///
    /// `gemini-3-pro-image-preview` 는 <see cref="Images"/> 에 그대로 둔다 — 정식 출시명으로
    /// 바뀌기 전 그 이름으로 쌓인 호출에게는 그 행이 유일한 근거다. 이름이 다르면 다른 행이다.
    /// </summary>
    public static IReadOnlyList<Row> MissingImagePrices =>
    [
        // OpenAI 는 장당 값을 공표하지 않고 토큰으로만 매긴다. 이 값은 공식 가이드의
        // 1024x1024 · medium 추정치이고, medium 은 `OpenAiImageProvider` 가 실어 보내는
        // 고정 품질이다 — 저기를 바꾸면 이 값도 같이 바뀌어야 한다 (low $0.006 · high $0.211)
        Image(44, "gpt-image-2", 0.053m, OpenAiImage),

        Image(45, "gemini-3.1-flash-image", 0.067m, GoogleImage),

        // 출력 이미지 $30/1M × 1,120토큰. Google 이 환산값을 직접 공표한다
        Image(46, "gemini-3.1-flash-lite-image", 0.0336m, GoogleImage),

        // 정식 출시로 `-preview` 가 떨어졌다. 단가는 그대로라 값이 42번 행과 같다
        Image(47, "gemini-3-pro-image", 0.134m, GoogleImage),
    ];

    /// <summary>새 단가가 적용되는 시각 — 장당 정액 행(44번, 2026-08-01)보다 나중이라야 우선한다.</summary>
    public static readonly DateTimeOffset GptImage2TokenPricingEffectiveFrom =
        new(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// `gpt-image-2` 를 장당 정액이 아니라 실제 토큰 단가로 다시 심는다 (README "미구현·가라
    /// 목록" — 참조 장수별 토큰 사용량이 비용에 반영 안 되던 문제, B안: 블렌디드 근사).
    ///
    /// OpenAI 공식 단가는 텍스트 입력 $5/100만·이미지 입력 $8/100만·출력 $30/100만으로
    /// 셋으로 나뉜다. 지금 `ModelPrice` 는 입력 단가를 하나만 가지므로(텍스트/이미지
    /// 세부 분해를 저장하지 않음, 이번 범위 밖 — A안), **$8(이미지 단가)을 입력 전체에
    /// 적용한다.** 참조 이미지가 텍스트 프롬프트보다 훨씬 많은 토큰을 차지하므로 실제
    /// 값보다 약간 높게 잡히는 쪽(과소청구보다 안전) — 정확한 근거이지 임의 추정이 아니다.
    ///
    /// `PerImage` 도 유지한다 — 토큰이 안 잡히는 응답이 오면(§3.3 fallback) 장당 정액으로
    /// 계속 계산된다. 새 행(시행일 2026-08-16)이라 그 이전 호출의 비용은 안 바뀐다.
    /// </summary>
    public static Row GptImage2TokenPricing => new(
        Guid.Parse("b1000000-0000-4000-8000-000000000050"),
        "gpt-image-2",
        Input: 8m, Output: 30m,
        LongFrom: null, LongInput: null, LongOutput: null,
        Note: "OpenAI 공식 단가(텍스트입력 $5/이미지입력 $8/출력 $30, 100만 토큰당, " +
              "2026-08-16) — 입력은 이미지 단가로 근사(블렌디드, README 미구현 목록 대응)",
        PerImage: 0.053m);

    /// <summary>
    /// 3D 생성 단가 (사이클 #11).
    ///
    /// **단위가 다르다.** 공급자는 크레딧으로 매기고 우리는 USD 로 저장한다 —
    /// Tripo 가 `1 credit = $0.01` 을 공표하므로 환산이 가능하다.
    ///
    /// **파츠당 한 건이다.** 이미지 네 장을 올려도 유료 호출은 제출 하나뿐이고, 업로드와
    /// 조회는 크레딧을 쓰지 않는다. 그래서 `PerImage` 열을 쓴다 — 이름은 "장당" 이지만
    /// 실제 뜻은 "출력물 하나당" 이다.
    ///
    /// 값은 우리 고정 설정 기준이다 (`texture: true` · `texture_quality: standard` ·
    /// `pbr: true`). 부가 옵션(HD 텍스처 +10 · 8K +20 · HD 지오메트리 +20 · quad +5)은
    /// 쓰지 않으므로 더해지지 않는다 — **그 설정을 바꾸면 이 값도 같이 바뀌어야 한다.**
    /// </summary>
    public static IReadOnlyList<Row> MeshPrices =>
    [
        // Multiview to 3D · P Series · standard texture = 30 credits × $0.01
        Image(48, "P1-20260311", 0.30m, TripoMesh),
    ];

    /// <summary>
    /// Meshy 단가 (사이클 #12 D-06 재검토).
    ///
    /// **처음에는 등록하지 않기로 했다.** 근거는 "크레딧당 USD 가 공개돼 있지 않다" 였는데
    /// 그것이 부정확했다 — API 문서에 없을 뿐, **요금제에서 유도된다.**
    ///
    /// | 요금제 | 월 | 크레딧 | 크레딧당 |
    /// |---|---:|---:|---:|
    /// | Pro | $20 | 1,000 | $0.0200 |
    /// | **Premium** | **$40** | **3,000** | **$0.0133** |
    /// | Ultra | $100 | 10,000 | $0.0100 |
    ///
    /// **이 값은 공급자의 정가가 아니라 우리 구독에서 유도한 것이다.** 요금제를 바꾸면
    /// 그날짜로 새 행을 넣는다 — 지난 비용은 그대로 남는다. 그것이 `EffectiveFrom` 이
    /// 있는 이유다.
    ///
    /// **크레딧이 월마다 소멸하므로 다 못 쓰면 실효 단가는 이보다 높다.** 여기 담긴 것은
    /// 한도를 다 쓸 때의 하한이다.
    ///
    /// API 는 웹앱과 같은 크레딧 풀을 쓴다 — 별도 개발자 요금제가 없다.
    /// </summary>
    public static IReadOnlyList<Row> MeshyPrices =>
    [
        // multi-image-to-3d · meshy-7 · texture = 30 credits × $0.0133 (Premium)
        Image(51, "meshy-7", 0.40m, MeshyMesh),
    ];

    private const string TripoMesh =
        "Tripo P Series multiview 30크레딧 × $0.01 (파츠당 1건, 2026-08-12)";

    private const string MeshyMesh =
        "Meshy multi-image 30크레딧 × $0.0133 (Premium $40/3,000, 파츠당 1건, 2026-08-14)";

    private const string OpenAiImage = "OpenAI 이미지 단가 (1024x1024 기준, 2026-08-01)";
    private const string GoogleImage = "Google 이미지 단가 (1024x1024 기준, 2026-08-01)";

    /// <summary>장당 과금 행. 토큰 단가는 0 이고 계산에 쓰이지 않는다 (§3.3).</summary>
    private static Row Image(int n, string model, decimal perImage, string note)
        => new(
            Guid.Parse($"b1000000-0000-4000-8000-{n:D12}"),
            model, 0m, 0m, null, null, null, note, perImage);

    // 고정 id 로 심는다 — 환경마다 다르면 "어느 행이 문제인가" 를 대조할 수 없다
    private static Row Make(
        int n, string model, decimal input, decimal output,
        int? longFrom, decimal? longInput, decimal? longOutput, string note)
        => new(
            Guid.Parse($"b1000000-0000-4000-8000-{n:D12}"),
            model, input, output, longFrom, longInput, longOutput, note);
}
