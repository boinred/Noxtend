using Noxtend.Domain.Job;
using Noxtend.Domain.Prompt;
using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Application.Prompts;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Api.Contracts;

/// <summary>
/// Design Ref: §4.2 #16~19 — 프롬프트 버전.
///
/// 본문(`system`·`user`)을 목록에도 싣는다. 편집 화면이 별도 조회 없이 바로 열리고,
/// 비교 화면이 "무엇이 달라졌나" 를 보여줄 수 있어야 한다.
/// </summary>
public sealed record PromptVersionResponse(
    Guid Id,
    string Kind,
    // null 이면 기본(카테고리 없음). asset-category-contract 의 character|object|background 표기
    string? Category,
    int Version,
    string System,
    string User,
    string JsonSchema,
    string? Note,
    bool IsActive,
    IReadOnlyList<string> AllowedVariables,
    DateTimeOffset CreatedAt)
{
    public static PromptVersionResponse From(PromptVersion version)
        => new(
            version.Id,
            JobResponse.Wire(version.Kind),
            version.Category is { } c ? JobResponse.Wire(c) : null,
            version.Version,
            version.System,
            version.User,
            version.JsonSchema,
            version.Note,
            version.IsActive,
            // 편집 화면이 "쓸 수 있는 변수" 를 보여줘야 오타로 저장이 거부되는 일이 준다
            [.. PromptTemplate.AllowedVariables(version.Kind).Order()],
            version.CreatedAt);
}

// Category 는 선택 — 생략/null 이면 기본 슬롯
public sealed record PromptWriteRequest(
    string System, string User, string JsonSchema, string? Note, string? Category = null);

/// <summary>
/// Design Ref: prompt-category-axis §12.1 — (단계 × 카테고리) 유효 활성 격자.
///
/// 화면은 이 값을 표로 펼치기만 한다. 폴백은 서버가 이미 반영했으므로 프론트는 알지 못한다.
/// </summary>
public sealed record PromptGridResponse(IReadOnlyList<PromptGridRowResponse> Rows)
{
    public static PromptGridResponse From(IReadOnlyList<PromptGridRow> rows)
        => new([.. rows.Select(PromptGridRowResponse.From)]);
}

public sealed record PromptGridRowResponse(string Kind, IReadOnlyList<PromptGridCellResponse> Cells)
{
    public static PromptGridRowResponse From(PromptGridRow row)
        => new(JobResponse.Wire(row.Kind), [.. row.Columns.Select(PromptGridCellResponse.From)]);
}

/// <summary>
/// <paramref name="Category"/> 가 null 이면 기본 열. <paramref name="Status"/> 는
/// <c>dedicated</c>·<c>fallback</c>·<c>unavailable</c>. <paramref name="Version"/>·
/// <paramref name="VersionId"/> 는 실행 불가 칸에서 null.
/// </summary>
public sealed record PromptGridCellResponse(
    string? Category, string Status, int? Version, Guid? VersionId)
{
    public static PromptGridCellResponse From(PromptGridColumn column)
        => new(
            column.Category is { } c ? JobResponse.Wire(c) : null,
            JobResponse.Wire(column.Cell.Status),
            column.Cell.Version,
            column.Cell.VersionId);
}

/// <summary>
/// Design Ref: §4.2 #24 — LLM 호출 내역.
///
/// **요청·응답 전문을 그대로 싣는다.** 이 화면의 목적이 "무엇을 보내 무엇을 받았나" 를
/// 보는 것이므로 요약하면 쓸모가 없다. 키가 섞일 경로는 구조로 막혀 있다 (§7 S-1).
///
/// <paramref name="Succeeded"/> 는 **공급자 호출**의 성패다. 그 단계가 성공했는지는
/// 작업의 `tasks[].status` 를 봐야 한다 — 유효한 JSON 이 왔는데 내용이 기대와 달라
/// 파싱에서 실패하면 호출은 성공이고 공정은 실패다 (G-5).
/// </summary>
public sealed record LlmCallResponse(
    Guid Id,
    Guid? TaskId,
    /// <summary>유사도 평가 호출이면 평가 id — 공정 호출이면 null (§7.3).</summary>
    Guid? SimilarityEvaluationId,
    string Kind,
    Guid PromptVersionId,
    string Model,
    string RequestPayload,
    string? ResponsePayload,
    int? InputTokens,
    int? OutputTokens,
    int LatencyMs,
    bool Succeeded,
    string? FailureReason,
    DateTimeOffset At,
    /// <summary>USD. 단가를 모르는 모델이면 `null` — 0 이 아니다 (§ModelPriceBook).</summary>
    decimal? EstimatedCostUsd,
    /// <summary>이미지 호출이면 장 수, 텍스트면 `null` (사이클 #7 · D-16).</summary>
    int? OutputImages)
{
    public static LlmCallResponse From(LlmCall call, ModelPriceBook prices)
        => new(
            call.Id, call.TaskId, call.SimilarityEvaluationId, JobResponse.Wire(call.Kind), call.PromptVersionId,
            call.Model, call.RequestPayload, call.ResponsePayload,
            call.InputTokens, call.OutputTokens, call.LatencyMs,
            call.Succeeded, call.FailureReason, call.At,
            prices.Estimate(
                call.Model, call.At, call.InputTokens, call.OutputTokens, call.OutputImages),
            call.OutputImages);
}

/// <summary>
/// Design Ref: §4.2 #26 — 내역 규모와 누적 비용.
///
/// <paramref name="UnpricedCalls"/> 가 0 이 아니면 합계가 실제보다 낮다 — 단가를
/// 모르는 모델이 섞였다는 뜻이다. 이 수를 함께 내보내지 않으면 사용자가 합계를
/// 실제 지출로 믿게 된다.
/// </summary>
public sealed record CallStatsResponse(
    long Count,
    long ApproximateBytes,
    decimal TotalCostUsd,
    long UnpricedCalls);

/// <summary>Design Ref: §4.2 #20~22 — 골든 샘플.</summary>
public sealed record GoldenSampleResponse(
    Guid Id,
    Guid StoredImageId,
    string Name,
    string ExpectedNote,
    DateTimeOffset CreatedAt)
{
    public static GoldenSampleResponse From(GoldenSample sample)
        => new(sample.Id, sample.StoredImageId, sample.Name, sample.ExpectedNote, sample.CreatedAt);
}

public sealed record GoldenWriteRequest(Guid StoredImageId, string Name, string ExpectedNote);
public sealed record GoldenUpdateRequest(string Name, string ExpectedNote);

/// <summary>
/// Design Ref: §4.2 #23 — 골든 샘플의 실행 이력.
///
/// **단계별 프롬프트 버전이 함께 온다.** 이것이 없으면 목록에서 무엇을 비교하는지
/// 알 수 없다 — "이 실행은 어느 프롬프트로 돌렸나" 가 비교의 전제다.
/// </summary>
public sealed record GoldenRunResponse(
    Guid JobId,
    string Status,
    string? Model,
    IReadOnlyDictionary<string, int> PromptVersions,
    int PartCount,
    VerdictResponse? Verdict,
    DateTimeOffset CreatedAt);

public sealed record VerdictResponse(bool IsPass, string Memo, DateTimeOffset At)
{
    public static VerdictResponse From(Verdict verdict)
        => new(verdict.IsPass, verdict.Memo, verdict.At);
}

public sealed record VerdictWriteRequest(bool IsPass, string Memo);

/// <summary>
/// 단가 행 하나.
///
/// <paramref name="EffectiveFrom"/> 이 이 표의 핵심이다 — 같은 모델에 시행일별로
/// 여러 행이 쌓이고, 호출은 자기 시각에 걸리는 행으로 계산된다.
/// </summary>
public sealed record ModelPriceResponse(
    Guid Id,
    string Model,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    int? LongContextFrom,
    decimal? LongInputPerMillion,
    decimal? LongOutputPerMillion,
    DateTimeOffset EffectiveFrom,
    string Note,
    /// <summary>이미지 1장당 USD. 토큰 과금 모델이면 `null` (사이클 #7 · Plan D-8).</summary>
    decimal? PerImage,
    string? Provider = null,
    bool AllowHistoricalFallback = true,
    string? SourceEvidenceJson = null)
{
    public static ModelPriceResponse From(ModelPrice price)
        => new(
            price.Id, price.Model, price.InputPerMillion, price.OutputPerMillion,
            price.LongContextFrom, price.LongInputPerMillion, price.LongOutputPerMillion,
            price.EffectiveFrom, price.Note, price.PerImage, price.Provider,
            price.AllowHistoricalFallback, price.SourceEvidenceJson);
}

/// <summary>단가 작성·수정 요청. 수정 시 <see cref="Model"/> 은 무시된다.</summary>
public sealed record ModelPriceRequest(
    string Model,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    int? LongContextFrom,
    decimal? LongInputPerMillion,
    decimal? LongOutputPerMillion,
    DateTimeOffset EffectiveFrom,
    string? Note,
    decimal? PerImage,
    string? Provider = null)
{
    public ModelPriceInput ToInput()
        => new(
            Model ?? string.Empty, InputPerMillion, OutputPerMillion,
            LongContextFrom, LongInputPerMillion, LongOutputPerMillion, EffectiveFrom, Note,
            PerImage, Provider);
}
