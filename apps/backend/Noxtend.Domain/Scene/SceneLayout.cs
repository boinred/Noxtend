using Noxtend.Domain.Scene.Projection;

namespace Noxtend.Domain.Scene;

/// <summary>장면 전체에 실제 미터 단위를 부여하는 기준 물체 높이와 2D 배치.</summary>
public sealed record SceneScaleCalibration(
    double HeightMeters,
    IReadOnlyList<Noxtend.Domain.Job.Bounds> AnchorPlacements,
    /// <summary>기준 물체가 표면으로 표시됐는가 — 보정 계수를 못 믿을 신호 (#20 §6).</summary>
    bool AnchorIsSurface = false);

/// <summary>revision 의 생애 — 활성은 job 당 하나뿐이다 (§4.1).</summary>
public enum SceneLayoutState
{
    Active = 0,
    Candidate = 1,
    Rejected = 2,
    Superseded = 3,
}

/// <summary>revision 이 어떻게 태어났나 — 이력 화면이 이것으로 줄을 가른다.</summary>
public enum SceneLayoutOrigin
{
    Composed = 0,
    SimilarityAdjustment = 1,
    Restore = 2,
}

/// <summary>mesh 가 바뀐 뒤의 복원·채택은 거짓말이다 — 그 배치는 지금 mesh 의 것이 아니다.</summary>
public sealed class SceneRevisionStaleException(string message) : Exception(message);

/// <summary>
/// 작업 하나의 3D 조립 명세 — **불변 revision** (D-02).
///
/// Design Ref: background-similarity-tuning §4 · scene-assembly §3.1
///
/// 값(인스턴스·카메라·조명)은 revision 생성 후 바뀌지 않는다. 바뀌는 것은 상태 전이뿐 —
/// 덮어쓰기가 없어야 후보 거부·복원·동시성 충돌을 이력 손실 없이 처리한다.
/// 복원은 과거 행을 되살리지 않고 값을 새 revision 으로 복사한다 (§4.3).
/// </summary>
public sealed class SceneLayout
{
    private readonly List<SceneInstance> _instances = [];

    private SceneLayout()
    {
        // EF Core 재구성용
        SourceMeshSignature = string.Empty;
        Camera = null!;
        Light = null!;
    }

    private SceneLayout(
        Guid id,
        Guid jobId,
        int revision,
        Guid? parentLayoutId,
        string signature,
        int sourceMeshCount,
        SceneLayoutState state,
        SceneLayoutOrigin origin,
        SceneCamera camera,
        SceneLightRig light,
        DateTimeOffset composedAt)
    {
        Id = id;
        JobId = jobId;
        Revision = revision;
        ParentLayoutId = parentLayoutId;
        SourceMeshSignature = signature;
        SourceMeshCount = sourceMeshCount;
        State = state;
        Origin = origin;
        Camera = camera;
        Light = light;
        ComposedAt = composedAt;
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    /// <summary>job 안에서 단조 증가 — 복원도 새 번호를 받는다 (§4.3).</summary>
    public int Revision { get; private set; }

    /// <summary>보정·복원이 시작된 revision.</summary>
    public Guid? ParentLayoutId { get; private set; }

    /// <summary>
    /// 정렬된 mesh asset id 의 SHA-256 (§4.1) — 개수만 세면 같은 개수의 교체를 못 본다.
    /// </summary>
    public string SourceMeshSignature { get; private set; }

    public int SourceMeshCount { get; private set; }

    public SceneLayoutState State { get; private set; }

    public SceneLayoutOrigin Origin { get; private set; }

    /// <summary>수치 카메라 — 서버가 정본이다 (D-03). 평가 렌더의 결정성이 여기 달렸다.</summary>
    public SceneCamera Camera { get; private set; }

    public SceneLightRig Light { get; private set; }

    public DateTimeOffset ComposedAt { get; private set; }

    /// <summary>
    /// 유도가 내린 판단 — 접지/고도 미상 개수, 대표 깊이, 기준 물체 편차 (§4.7 · FR-07).
    /// 보정 후보·복원본은 합성을 다시 하지 않으므로 부모의 요약을 그대로 물려받는다.
    /// </summary>
    public CompositionSummary? Composition { get; private set; }

    /// <summary>상태 전이 경쟁의 방어선 — 값은 불변이라 전이만 지키면 된다.</summary>
    public byte[]? RowVersion { get; private set; }

    /// <summary>배치 순서대로 — 저장이 순서를 보장하지 않으므로 여기서 정렬한다.</summary>
    public IReadOnlyList<SceneInstance> Instances =>
        [.. _instances.OrderBy(i => i.PartId).ThenBy(i => i.Ordinal)];

    public static SceneLayout ComposeActive(
        Guid jobId,
        int revision,
        IReadOnlyList<SceneInstance> instances,
        string signature,
        int sourceMeshCount,
        SceneCamera camera,
        SceneLightRig light,
        DateTimeOffset now,
        CompositionSummary? composition = null)
    {
        var layout = new SceneLayout(
            Guid.NewGuid(), jobId, revision, parentLayoutId: null, signature, sourceMeshCount,
            SceneLayoutState.Active, SceneLayoutOrigin.Composed, camera, light, now)
        {
            Composition = composition,
        };
        layout._instances.AddRange(instances);

        return layout;
    }

    /// <summary>보정 후보 — 비활성으로 태어난다 (D-07). 활성은 재평가 성공 transaction 에서만.</summary>
    public static SceneLayout CreateCandidate(
        SceneLayout parent,
        int revision,
        IReadOnlyList<SceneInstance> adjustedInstances,
        SceneCamera camera,
        SceneLightRig light,
        DateTimeOffset now)
    {
        var candidate = new SceneLayout(
            Guid.NewGuid(), parent.JobId, revision, parent.Id,
            parent.SourceMeshSignature, parent.SourceMeshCount,
            SceneLayoutState.Candidate, SceneLayoutOrigin.SimilarityAdjustment, camera, light, now)
        {
            Composition = parent.Composition,
        };
        candidate._instances.AddRange(adjustedInstances);

        return candidate;
    }

    /// <summary>복원 = 값 복사 (§4.3) — 과거 행을 되살리면 revision 단조가 깨진다.</summary>
    public static SceneLayout CreateRestored(
        SceneLayout source,
        string currentSignature,
        int revision,
        DateTimeOffset now)
    {
        if (source.SourceMeshSignature != currentSignature)
        {
            throw new SceneRevisionStaleException(
                "복원하려는 배치는 현재 3D 조합의 것이 아닙니다");
        }

        var restored = new SceneLayout(
            Guid.NewGuid(), source.JobId, revision, source.Id,
            source.SourceMeshSignature, source.SourceMeshCount,
            SceneLayoutState.Active, SceneLayoutOrigin.Restore, source.Camera, source.Light, now)
        {
            Composition = source.Composition,
        };
        restored._instances.AddRange(source.Instances);

        return restored;
    }

    /// <summary>후보 채택 — 기존 활성의 <see cref="MarkSuperseded"/> 와 같은 transaction 이어야 한다.</summary>
    public void Adopt()
    {
        if (State != SceneLayoutState.Candidate)
        {
            throw new InvalidOperationException($"후보만 활성화할 수 있습니다. 현재 상태: {State}");
        }

        State = SceneLayoutState.Active;
    }

    public void Reject()
    {
        if (State != SceneLayoutState.Candidate)
        {
            throw new InvalidOperationException($"후보만 거부할 수 있습니다. 현재 상태: {State}");
        }

        State = SceneLayoutState.Rejected;
    }

    public void MarkSuperseded()
    {
        if (State != SceneLayoutState.Active)
        {
            throw new InvalidOperationException($"활성만 대체될 수 있습니다. 현재 상태: {State}");
        }

        State = SceneLayoutState.Superseded;
    }
}

/// <summary>
/// 배치 하나가 놓일 자리 — "이 에셋을 여기에". 좌표는 월드 유닛 (§3.2 좌표계).
///
/// 배율은 **정규화된 GLB(높이 1 · 바닥 중심 원점)에 곱하는 값**이다. 정규화는 브라우저 몫이라는
/// 계약 위에 선다.
///
/// **축별로 나뉘어 있는 이유** (background-surface-parts #20 §4.2): 파츠에 두 종류가 있다.
/// 낱개 물건은 세 값이 같지만, 면을 덮는 표면(포장·도로·절벽 단면)은 배치 사각형이 곧
/// 크기여서 폭·깊이를 각각 맞춰야 한다. 균일 배율로는 정사각형이 아닌 면을 덮을 수 없다.
/// </summary>
public sealed class SceneInstance : IEquatable<SceneInstance>
{
    // EF 재구성용. 레코드로 두면 소유 컬렉션 키 바인딩이 번거롭다 — PartPlacement 와 같은 선택
    private SceneInstance() { }

    /// <summary>균일 배율 — 낱개 물건. 세 축이 같은 값을 갖는다.</summary>
    public SceneInstance(
        Guid partId, int ordinal, double x, double y, double z, double rotationY, double scale)
        : this(partId, ordinal, x, y, z, rotationY, scale, scale, scale)
    {
    }

    /// <summary>축별 배율 — 표면. 폭·높이·깊이를 따로 맞춘다.</summary>
    public SceneInstance(
        Guid partId, int ordinal, double x, double y, double z, double rotationY,
        double scaleX, double scaleY, double scaleZ)
    {
        PartId = partId;
        Ordinal = ordinal;
        X = x;
        Y = y;
        Z = z;
        RotationY = rotationY;
        ScaleX = scaleX;
        ScaleY = scaleY;
        ScaleZ = scaleZ;
    }

    public Guid PartId { get; private set; }

    /// <summary>파츠 안에서 몇 번째 배치인가 — PartPlacement.Ordinal 과 같은 번호.</summary>
    public int Ordinal { get; private set; }

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Z { get; private set; }

    /// <summary>이번 사이클은 항상 0 — 배치 편집 대비 자리만 잡아 둔다.</summary>
    public double RotationY { get; private set; }

    public double ScaleX { get; private set; }

    public double ScaleY { get; private set; }

    public double ScaleZ { get; private set; }

    /// <summary>
    /// 높이 — 정규화 기준 축이다.
    ///
    /// 균일 배율에서는 세 축이 같으므로 이 값이 곧 배율이다. 크기를 한 값으로 말해야 하는
    /// 자리(기준 물체 보정·유사도 보정)가 이 축을 본다.
    /// </summary>
    public double Scale => ScaleY;

    /// <summary>세 축이 같은가 — 낱개 물건이다.</summary>
    public bool IsUniform => ScaleX == ScaleY && ScaleY == ScaleZ;

    public bool Equals(SceneInstance? other)
        => other is not null
           && PartId == other.PartId && Ordinal == other.Ordinal
           && X == other.X && Y == other.Y && Z == other.Z
           && RotationY == other.RotationY
           && ScaleX == other.ScaleX && ScaleY == other.ScaleY && ScaleZ == other.ScaleZ;

    public override bool Equals(object? obj) => Equals(obj as SceneInstance);

    public override int GetHashCode() => HashCode.Combine(PartId, Ordinal, X, Z, ScaleY);
}
