using Noxtend.Domain.Job;

namespace Noxtend.Api.Contracts;

/// <summary>
/// 검수 대기 상태 응답 (review-gate §입력→출력 — <c>GET .../review</c>).
///
/// 화면이 이 하나로 검수 목록을 그린다 — 상태와 파츠(감지됨/직접 추가 구분 포함)가
/// 함께 나가야 "전체 승인" 버튼을 활성화할지 판단할 수 있다.
/// </summary>
public sealed record ReviewStateResponse(
    string Status,
    IReadOnlyList<ReviewPartResponse> Parts,
    /// <summary>
    /// 승인 시 서술을 다시 쓸 파츠 (occludedby-recompute §입력→출력 1).
    ///
    /// **작업 수준 값이라 여기 있다.** 파츠마다 실으면 GET 경로에서 매번 계산해야 하고,
    /// 안 하면 "비어 있다" 고 거짓 단언하게 된다.
    /// </summary>
    IReadOnlyList<string> DescriptionsStale,
    /// <summary><c>"boxes"</c> · <c>"descriptions"</c>. 게이트 없는 작업·승인 완료는 <c>null</c>.</summary>
    string? ReviewPhase,
    /// <summary>장면 팔레트 — 서술 단계에서 편집. 장면이 없으면 빈 목록.</summary>
    IReadOnlyList<PaletteEntryResponse> Palette)
{
    public static ReviewStateResponse From(PipelineJob job)
        => new(
            JobResponse.Wire(job.Status),
            [.. job.Parts.OrderBy(p => p.Ordinal).Select(part => ReviewPartResponse.From(part, job))],
            job.DescriptionsStale,
            // 화면 분기는 검수 중인 두 단계만 필요
            job.RequiresReview && job.ReviewPhase != Domain.Job.ReviewPhase.Approved
                ? JobResponse.Wire(job.ReviewPhase)
                : null,
            [.. (job.Scene?.Palette ?? []).Select(entry => new PaletteEntryResponse(entry.Name, entry.Hex))]);
}

/// <summary>파츠 한 줄 — 검수 화면이 사각형을 겹쳐 그릴 좌표와 출처.</summary>
public sealed record ReviewPartResponse(
    Guid Id,
    string PartRef,
    string Name,
    string? Category,
    string? Description,
    IReadOnlyList<BoundsResponse> Placements,
    /// <summary><c>"detected"</c>(VLM) 또는 <c>"manual"</c>(사람이 추가) — §입력→출력 예시와 동일 어휘.</summary>
    string Source,
    /// <summary>이 파츠를 가리는 파츠 — 검수자가 결과를 눈으로 확인하는 값이다.</summary>
    IReadOnlyList<string> OccludedBy,
    /// <summary>이 파츠가 가리는 기존 파츠 (occludedby-recompute §입력→출력 1).</summary>
    IReadOnlyList<string> Occludes,
    /// <summary><c>"model"</c> · <c>"rewritten"</c> · <c>"human"</c> — 서술 확인 화면 칩.</summary>
    string DescriptionSource)
{
    /// <summary>
    /// 가림 관계는 작업 전체를 봐야 안다 — "이 파츠를 가리는 것" 은 파츠가 알지만
    /// "이 파츠가 가리는 것" 은 남들의 <c>OccludedBy</c> 를 훑어야 나온다.
    ///
    /// **빈 배열을 기본값으로 두는 오버로드를 만들지 않는다** — 필드가 없는 것과
    /// "없다고 단언" 하는 것은 다르고, 후자는 화면이 조용히 잘못 읽는다.
    /// </summary>
    public static ReviewPartResponse From(AssetPart part, PipelineJob job)
        => From(
            part,
            [.. job.Parts.Where(o => o.OccludedBy.Contains(part.Name)).Select(o => o.Name)]);

    public static ReviewPartResponse From(AssetPart part, IReadOnlyList<string> occludes)
        => new(
            part.Id,
            $"P{part.Ordinal + 1:D2}",
            part.Name,
            part.Category,
            part.Description,
            [.. part.Placements.Select(b => new BoundsResponse(b.X, b.Y, b.W, b.H))],
            part.IsManuallyAdded ? "manual" : "detected",
            part.OccludedBy,
            occludes,
            JobResponse.Wire(part.DescriptionSource));
}

/// <summary>추가할 파츠의 사각형 — 프론트가 드래그 픽셀 좌표를 0~1 로 정규화해 보낸다.</summary>
public sealed record BoundsDto(double X, double Y, double W, double H)
{
    public Bounds ToDomain() => new(X, Y, W, H);
}

/// <summary>검수 화면에서 파츠 하나를 추가하는 요청 (§입력→출력 — <c>POST .../review/parts</c>).</summary>
/// <summary>
/// 검수 화면의 파츠 추가 요청.
///
/// **<see cref="Occludes"/> 는 생략과 빈 배열의 뜻이 다르다** (occludedby-recompute
/// §입력→출력 1). <c>null</c> 이면 기본 추정 — 겹치는 파츠 전부를 이 파츠가 가린다.
/// 빈 배열이면 검수자가 체크를 전부 해제했다는 뜻이라 아무 관계도 기록하지 않는다.
/// 그래서 <c>string[]</c> 이 아니라 nullable 이다 — 합치면 정반대 결과가 된다.
/// </summary>
public sealed record AddReviewPartRequest(
    string Name,
    /// <summary>비우면 승인 시 재작성 공정이 원본 이미지를 보고 채운다.</summary>
    string? Category,
    BoundsDto Bounds,
    string? Description = null,
    IReadOnlyList<string>? Occludes = null);

/// <summary>겹침 조회 요청 — 사각형 하나만 보낸다.</summary>
public sealed record FindOverlapsRequest(BoundsDto Bounds);

/// <summary>서술 단계 서술 편집 요청.</summary>
public sealed record EditReviewDescriptionRequest(string Description);

/// <summary>서술 단계 팔레트 교체 요청 — 통째 교체.</summary>
public sealed record EditReviewPaletteRequest(IReadOnlyList<PaletteEntryResponse> Palette);
