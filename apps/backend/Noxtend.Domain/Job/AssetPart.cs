namespace Noxtend.Domain.Job;

/// <summary>
/// 파츠 — 소스 이미지에서 추출한 구성 요소 (등대 · 부두 · 어선 …).
///
/// Design Ref: §1.2 · §3.1
///
/// **파츠는 출력 데이터가 아니라 다음 단계 공정의 입력이다.** 분해·생성 단계가 붙으면
/// 파츠마다 이미지를 만들고 파츠마다 3D 모델을 만든다. JSON 필드였다면 그때 다시 뜯어야 한다.
///
/// 사이클 #5 에서 분해 단계가 서술·분류·공간 정보를 채운다.
/// 이후 생성 이미지·3D 모델 참조가 더 붙는다.
/// </summary>
public sealed class AssetPart
{
    private AssetPart()
    {
        // EF Core 재구성용
    }

    private AssetPart(Guid id, Guid jobId, string name, int ordinal)
    {
        Id = id;
        JobId = jobId;
        Name = name;
        Ordinal = ordinal;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>추출된 순서. LLM 이 낸 순서를 보존해야 사용자가 본 목록과 일치한다.</summary>
    public int Ordinal { get; private set; }

    // ─── 분해 단계가 채운다 (사이클 #5). 그 전에는 전부 비어 있다 ───

    /// <summary>이 파츠만 따로 그릴 수 있을 만큼의 서술.</summary>
    public string? Description { get; private set; }

    public string? Category { get; private set; }

    /// <summary>서술 출처 — 재승인 재작성 제외 판단과 검수 화면 칩에 쓰인다.</summary>
    public DescriptionSource DescriptionSource { get; private set; } = DescriptionSource.Model;

    /// <summary>
    /// 이 파츠를 놓을 자리들. 정규화 0~1 (사이클 #9).
    ///
    /// **배치는 합성 지시다** — "같은 그림을 이 자리들에 놓는다". 나무 네 그루면 나무를 한 번
    /// 그리고 네 자리에 놓는다. 전에는 상자가 하나뿐이라 여럿을 표현할 수 없었고, 모델은 낼 수
    /// 있는 유일한 답으로 **전부를 감싸는 합집합**을 냈다 — `가로등 w=0.94` 같은 값이 그래서
    /// 나왔다.
    ///
    /// 분해 전에는 비어 있다. `null` 이 아니라 빈 목록인 이유는 "아직 없다" 와 "0개다" 가
    /// 여기서 같은 뜻이기 때문이다.
    /// </summary>
    /// <remarks>
    /// **읽을 때 정렬한다.** DB 가 돌려주는 순서에 기대지 않는다 — 지금은 클러스터드 인덱스
    /// 덕에 맞아떨어지지만 그것은 계약이 아니다.
    /// </remarks>
    public IReadOnlyList<Bounds> Placements =>
        [.. _placements.OrderBy(p => p.Ordinal).Select(p => p.Bounds)];

    // EF 가 채워 넣어야 하므로 가변 목록이다 — 배열이면 "고정 크기" 로 막힌다.
    // `_parts` · `_tasks` 와 같은 방식이다
    private readonly List<PartPlacement> _placements = [];

    /// <summary>1 = 가장 앞. 작업 안에서 중복되지 않는다 (FR-05).</summary>
    public int? DepthOrder { get; private set; }

    /// <summary>
    /// 이 파츠를 가리는 파츠들의 이름.
    ///
    /// **이것이 분해를 한 번에 부르는 이유다** (Plan D-2). 가림은 파츠들 **사이**의
    /// 관계라, 파츠마다 따로 호출하면 각 호출이 다른 파츠를 몰라 낼 수가 없다.
    /// </summary>
    public IReadOnlyList<string> OccludedBy { get; private set; } = [];

    /// <summary>면을 덮는 파츠인가 (#20 §4.1). 분해가 답하지 않으면 낱개 물건이다.</summary>
    public PartSurface Surface { get; private set; } = PartSurface.None;

    /// <summary>
    /// 사람이 검수 화면에서 사각형으로 추가했는가 (review-gate §입력→출력 <c>source</c>).
    ///
    /// **VLM 이 찾은 파츠와 구분해야 한다.** 화면이 "감지됨/직접 추가"를 나눠 보여줘야
    /// 검수자가 어느 파츠를 자신이 그렸는지 알 수 있다 — 전부 같은 목록에 섞이면
    /// 방금 추가한 파츠를 다시 찾기 어렵다.
    /// </summary>
    public bool IsManuallyAdded { get; private set; }

    // ─── 생성 단계가 채운다 (사이클 #7) ───

    /// <summary>
    /// 생성된 이미지. 아직 안 만들었거나 실패했으면 <c>null</c>.
    ///
    /// Design Ref: §3.1 — 위 주석의 "이후 생성 이미지·3D 모델 참조가 더 붙는다" 중 앞의 것이다.
    /// 재생성하면 <see cref="GeneratedImage"/> 행이 쌓이고 이 참조가 최신 것을 가리킨다.
    /// </summary>
    public Guid? GeneratedImageId { get; private set; }

    internal static AssetPart Create(Guid jobId, string name, int ordinal)
        => new(Guid.NewGuid(), jobId, name, ordinal);

    /// <summary>생성 이미지 참조 갱신. 이전 이미지 행은 지우지 않는다 — 비교의 재료다.</summary>
    internal void AttachImage(Guid generatedImageId, ViewDirection viewDirection)
    {
        // 기존 API의 단일 이미지는 정면 대표 이미지로 유지
        if (viewDirection == ViewDirection.Front)
        {
            GeneratedImageId = generatedImageId;
        }
    }

    /// <summary>분해 결과 반영. 검증은 <see cref="PipelineJob.ApplyPartDetails"/> 가 이미 마쳤다.</summary>
    internal void ApplyDetail(PartDetail detail)
    {
        Description = detail.Description;
        DescriptionSource = DescriptionSource.Model;
        Category = detail.Category;
        // 모델이 낸 순서가 곧 번호다
        _placements.Clear();
        _placements.AddRange(
            detail.Placements.Select((bounds, ordinal) => new PartPlacement(ordinal, bounds)));
        DepthOrder = detail.DepthOrder;
        OccludedBy = [.. detail.OccludedBy];
        Surface = detail.Surface;
    }

    /// <summary><see cref="PipelineJob.AddReviewPart"/> 전용 — 다른 호출자는 이 표식을 못 바꾼다.</summary>
    internal void MarkManuallyAdded() => IsManuallyAdded = true;

    /// <summary>
    /// 가림 관계 하나를 더한다 (occludedby-recompute §입력→출력 1).
    ///
    /// <see cref="ApplyDetail"/> 은 좌표까지 통째로 갈아끼우므로 검수 편집 경로에서 쓸 수
    /// 없다 — 사람이 그린 좌표가 지워진다.
    /// </summary>
    internal void AddOccluder(string partName)
    {
        if (!OccludedBy.Contains(partName))
        {
            OccludedBy = [.. OccludedBy, partName];
        }
    }

    /// <summary>가림 관계 하나를 뺀다 — 가리던 파츠가 검수에서 삭제됐을 때.</summary>
    internal void RemoveOccluder(string partName)
        => OccludedBy = [.. OccludedBy.Where(name => name != partName)];

    /// <summary>
    /// 검수 화면에서 사람이 그린 파츠의 초기값 (occludedby-recompute §입력→출력 1).
    ///
    /// **<see cref="ApplyDetail"/> 을 쓰지 않는 이유**는 그쪽이 카테고리·서술을 필수로
    /// 받기 때문이다. 여기서는 둘 다 비어 있을 수 있다 — 비면 승인 시 재작성 공정이 원본
    /// 이미지를 보고 채운다.
    /// </summary>
    internal void InitializeManual(
        string? category, string? description, Bounds bounds, int depthOrder)
    {
        Category = category;
        Description = description;
        // 사람이 서술을 썼으면 사람 출처, 비우면 재작성이 채울 때까지 Model
        DescriptionSource = string.IsNullOrWhiteSpace(description)
            ? DescriptionSource.Model
            : DescriptionSource.Human;
        _placements.Clear();
        _placements.Add(new PartPlacement(0, bounds));
        DepthOrder = depthOrder;
    }

    /// <summary>
    /// 배치 하나의 좌표만 갈아끼운다 — 검수 화면에서 사람이 상자를 끌었을 때.
    ///
    /// **<see cref="ApplyDetail"/> 도 <see cref="InitializeManual"/> 도 쓸 수 없다.**
    /// 둘 다 <c>_placements</c> 를 통째로 비우므로 배치가 여럿인 파츠에서 나머지가 사라진다.
    /// </summary>
    /// <exception cref="KeyNotFoundException">그 번호의 배치가 없다</exception>
    internal void ReplacePlacement(int ordinal, Bounds bounds)
    {
        var index = _placements.FindIndex(p => p.Ordinal == ordinal);
        if (index < 0)
        {
            throw new KeyNotFoundException($"'{Name}' 에 {ordinal} 번 배치가 없습니다");
        }

        _placements[index] = new PartPlacement(ordinal, bounds);
    }

    /// <summary>
    /// 서술만 갈아끼운다 (occludedby-recompute §구현 범위 4).
    ///
    /// **<see cref="ApplyDetail"/> 을 재사용하면 안 된다** — 그쪽은 좌표까지 통째로
    /// 덮어쓰므로 사람이 그린 좌표가 지워진다. 팬아웃은 좌표를 안 보므로 정상 진행되고,
    /// 손실은 조립 단계에 가서야 드러난다.
    ///
    /// 카테고리는 **비어 있을 때만** 채운다 — 사람이 쓴 값을 모델이 덮으면 안 된다.
    /// </summary>
    internal void ReviseDescription(string description, string? category = null)
    {
        Description = description;
        DescriptionSource = DescriptionSource.Rewritten;

        if (string.IsNullOrWhiteSpace(Category) && !string.IsNullOrWhiteSpace(category))
        {
            Category = category;
        }
    }

    /// <summary>서술 확인 단계에서 사람이 서술을 고친다 (review-gate-staged 사이클 1).</summary>
    internal void EditDescription(string description)
    {
        Description = description;
        DescriptionSource = DescriptionSource.Human;
    }
}
