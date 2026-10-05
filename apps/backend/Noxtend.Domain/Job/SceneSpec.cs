namespace Noxtend.Domain.Job;

/// <summary>
/// 팔레트 한 칸 — 장면 속 색의 역할과 그 색 자체.
///
/// Design Ref: §4.1 · D-01·D-02
///
/// **둘을 한 문자열에 담아 두면 색이 사라진다.** 전에는 `"백색 건물 외벽"` 같은 자연어가
/// 그대로 들어왔고, 화면이 거기서 HEX 를 정규식으로 긁어 색칩을 칠했다. 자연어만 오는
/// 순간 칩이 투명해져 이름만 남았다.
///
/// <paramref name="Hex"/> 가 nullable 인 이유는 **레거시 복원 하나뿐이다.** 신규 분석
/// 경로는 색상어를 식별할 수 없는 값을 만들 수 없다.
/// </summary>
/// <param name="Name">사람이 읽는 역할 설명. 예: "백색 건물 외벽"</param>
/// <param name="Hex">대문자 `#RRGGBB`. 색상어를 식별할 수 없는 과거 값만 null</param>
public sealed record PaletteEntry(string Name, string? Hex);

/// <summary>
/// 장면 명세 — 이후 모든 단계가 참조하는 기준선.
///
/// Design Ref: §3.1 · Plan D-13
///
/// **값 객체다.** 정체성이 없고 작업에 속한 한 덩어리의 값이며, 바뀌면 통째로 교체된다.
/// 엔티티로 두면 없는 생명주기를 관리하게 된다.
///
/// 사이클 #4 는 이것이 `ConsistencyPrompt` 라는 2~4문장 산문이었다. 파츠를 따로 생성해
/// 한 장면으로 합치려 할 때, 산문은 매번 다른 부분에 무게가 실려 재현되지 않았고
/// **조립에 결정적인 시점·광원 방향·수평선·스케일이 아예 빠져 있었다.**
/// </summary>
public sealed record SceneSpec(
    IReadOnlyList<PaletteEntry> Palette,
    string TimeOfDay,
    string Mood,
    string RenderingStyle,
    string MaterialFeel,
    CameraSpec Camera,
    LightSpec Light,
    ScaleReference Scale);

/// <summary>
/// 시점.
///
/// **없으면 파츠마다 다른 각도로 그려진다.** 부두는 눈높이에서, 등대는 위에서 그려지면
/// 두 이미지는 절대 합쳐지지 않는다.
/// </summary>
/// <param name="Type">one-point · two-point · isometric · orthographic</param>
/// <param name="EyeLevel">"지면에서 1.6m" 처럼 시점 높이</param>
/// <param name="HorizonY">화면 상단 기준 0~1. 지면에 물체를 놓는 기준선이다</param>
public sealed record CameraSpec(string Type, string EyeLevel, double HorizonY);

/// <summary>
/// 광원.
///
/// **없으면 그림자가 서로 다른 방향으로 진다.** `timeOfDay` 만으로는 부족하다 —
/// "해질녘" 은 방위를 정하지 않는다.
/// </summary>
public sealed record LightSpec(string Direction, string Temperature, string ShadowHardness);

/// <summary>
/// 스케일 기준.
///
/// **없으면 파츠 간 상대 크기가 어긋난다.** 등대가 부두보다 작게 그려지는 일이 생긴다.
/// </summary>
/// <param name="HeightMeters">기준 물체의 미터 단위 높이. 구조화 높이가 없는 과거 장면은 null</param>
public sealed record ScaleReference(
    string Object,
    string RealWorldSize,
    double? HeightMeters = null);

/// <summary>파츠의 화면상 위치. 정규화 0~1, 원점 좌상단 (§10).</summary>
public sealed record Bounds(double X, double Y, double W, double H)
{
    /// <summary>
    /// 유효성 (FR-05). 경계값 0 과 1 은 통과한다 — 화면 끝에 닿는 파츠가 정상이다.
    ///
    /// 오른쪽·아래 끝이 1 을 넘는 것도 막는다. 넘으면 화면 밖을 가리키는 셈이라
    /// 다음 단계가 그 영역을 생성할 수 없다.
    /// </summary>
    public bool IsWithinFrame()
        => X is >= 0 and <= 1
           && Y is >= 0 and <= 1
           && W is > 0 and <= 1
           && H is > 0 and <= 1
           && X + W <= 1.0001   // 부동소수 오차 여유
           && Y + H <= 1.0001;

    /// <summary>
    /// 겹친 넓이 ÷ 두 박스 중 더 작은 쪽 넓이 (review-gate 결정로그 T-03).
    ///
    /// **IoU(교집합÷합집합)를 안 쓰는 이유**: 벨트처럼 작은 파츠가 바지처럼 큰 파츠 안에
    /// 거의 다 걸쳐 있어도, 둘을 합친 전체 넓이가 워낙 커서 IoU 는 낮게 나와 놓친다.
    /// 작은 쪽 넓이로 나누면 "작은 파츠가 거의 통째로 파묻히는가"를 직접 답한다 — 목걸이와
    /// 옷깃처럼 경계만 살짝 스치는 정상적인 인접 관계는 여전히 낮게 나온다.
    /// </summary>
    public double OverlapCoefficient(Bounds other)
    {
        var intersectionW = Math.Max(0, Math.Min(X + W, other.X + other.W) - Math.Max(X, other.X));
        var intersectionH = Math.Max(0, Math.Min(Y + H, other.Y + other.H) - Math.Max(Y, other.Y));
        var intersectionArea = intersectionW * intersectionH;
        if (intersectionArea == 0)
        {
            return 0;
        }

        var smallerArea = Math.Min(W * H, other.W * other.H);
        return intersectionArea / smallerArea;
    }
}

/// <summary>
/// 배치 하나 — 좌표와 그 순서 (사이클 #9 · D-03a).
///
/// **`Ordinal` 이 도메인에 있는 이유**는 순서를 코드가 지키기 위해서다. 그림자 속성으로
/// 두면 DB 가 번호를 매기고, 읽는 순서는 클러스터드 인덱스가 그렇게 스캔해 준 결과일 뿐이다 —
/// 쿼리 계획이 바뀌면 조용히 뒤섞이고 아무도 눈치채지 못한다. 여기 두면 `OrderBy` 를
/// 명시할 수 있다.
///
/// `AssetPart.Placements` 는 이것을 드러내지 않는다. 순서는 목록의 순서로 이미 표현되므로
/// 바깥이 번호를 볼 이유가 없다.
/// </summary>
public sealed class PartPlacement
{
    // EF 재구성용. 레코드로 두면 소유 타입(`Bounds`)을 생성자에 바인딩하지 못한다
    private PartPlacement() { }

    public PartPlacement(int ordinal, Bounds bounds)
    {
        Ordinal = ordinal;
        Bounds = bounds;
    }

    public int Ordinal { get; private set; }

    public Bounds Bounds { get; private set; } = null!;
}

/// <summary>
/// 분해가 낸 파츠 하나의 명세.
///
/// 도메인이 이 모양을 아는 이유는 <see cref="PipelineJob.ApplyPartDetails"/> 가
/// 유효성을 판단해야 하기 때문이다 — 검사 규칙이 엔티티 밖에 있으면 흩어진다.
/// </summary>
public sealed record PartDetail(
    string Name,
    string Category,
    string Description,
    IReadOnlyList<Bounds> Placements,
    int DepthOrder,
    IReadOnlyList<string> OccludedBy,
    /// <summary>면을 덮는 파츠인가. 기본은 낱개 물건이다 (#20 §4.1).</summary>
    PartSurface Surface = PartSurface.None);

/// <summary>
/// 파츠가 낱개 물건인가, 면을 덮는 표면인가.
///
/// Design Ref: background-surface-parts(#20) §4.1 — D-01
///
/// **기하로는 가려낼 수 없다.** 같은 성격의 파츠 셋을 GLB 종횡비로 재 보면 0.43 · 0.14 ·
/// 0.57 로 흩어져 하나만 걸린다 — 3D 생성기가 두께를 제각각 내기 때문이다. 이름 키워드도
/// 큰 배치의 38%에 그친다. 반면 모델은 "포장 석재" 를 만들 때 그것이 면임을 알고 있다.
///
/// 방향까지 받는 이유는 실측이다 — 화면을 가장 크게 덮는 배치가 "수직 절벽 단면"(면적
/// 1.00)이라, 지면만 다루면 1위를 놓친다.
/// </summary>
public enum PartSurface
{
    /// <summary>낱개 물건 — 지금까지의 전부. 배치가 "이 물건을 여기에" 를 뜻한다.</summary>
    None = 0,

    /// <summary>지면을 덮는다 — 포장·도로·보도·수면. 배치 사각형이 곧 바닥 면이다.</summary>
    Ground = 1,

    /// <summary>수직면을 덮는다 — 절벽 단면·벽면. 배치 사각형이 곧 그 벽이다.</summary>
    Vertical = 2,
}
