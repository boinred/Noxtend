namespace Noxtend.Domain.Job;

/// <summary>
/// 3D 재구성 공정이 실제로 쓴 이미지들.
///
/// Design Ref: §4.3 · Plan D-05 · FR-02·FR-04
///
/// **방향이 이름이지 위치가 아니다.** Tripo 의 위치 기반 순서는 `front, left, back, right`
/// 로 우리 내부 순서와 좌우가 다르다. 네 ID 를 배열로 두면 언젠가 그 순서로 넘겨 좌우가
/// 뒤집힌 mesh 가 나오는데, 그것은 실행 중에 예외로 드러나지 않고 결과물의 품질로만
/// 나타나므로 알아채기가 어렵다. 이름 붙은 필드에는 그 실수를 할 자리가 없다.
///
/// **계획 시점에 얼린다** (FR-04). 재생성이 이미지 행을 쌓으므로 "그때 최신" 이 나중의
/// 최신과 다르다. 얼려 두지 않으면 같은 공정을 재시도할 때 다른 입력이 들어가 재현이 안 된다.
/// </summary>
public sealed record MeshInputSet
{
    public Guid FrontImageId { get; init; }
    public Guid? RightImageId { get; init; }
    public Guid? BackImageId { get; init; }
    public Guid? LeftImageId { get; init; }

    public MeshInputSet(
        Guid frontImageId,
        Guid? rightImageId = null,
        Guid? backImageId = null,
        Guid? leftImageId = null)
    {
        if (frontImageId == Guid.Empty)
        {
            throw new ArgumentException("정면 이미지 ID는 필수입니다.", nameof(frontImageId));
        }

        // 비정면 0장도 허용한다 — 공급자별 최소 장수(Tripo 2장 등)는 도메인이 아니라
        // 각 어댑터가 제출 직전에 검증한다 (spec 20260917 §설계 결정, 포트가 공급자 차이를
        // 몰라야 한다는 원칙에 따름). 여기서는 "지정됐다면 유효한 값인가"만 본다.
        var nonFront = new[] { rightImageId, backImageId, leftImageId };
        foreach (var id in nonFront)
        {
            if (id is { } value && (value == Guid.Empty || value == frontImageId))
            {
                throw new ArgumentException("비정면 이미지 ID는 비어 있거나 정면과 같을 수 없습니다.", nameof(frontImageId));
            }
        }

        // 비정면끼리도 같은 이미지를 두 방향에 겹쳐 쓸 수 없다 — 한 장을 두 슬롯에 넣으면
        // 공급자에게 "이 방향은 다르게 생겼다"는 신호가 거짓이 된다(merge-gate 리뷰 F7).
        // 지금 있는 호출 경로(Both는 서로 다른 방향을 조회, Mirror*는 새 이미지를 만듦)로는
        // 도달 못 하지만, 이 타입 자체의 불변식이어야 한다
        var presentNonFront = nonFront.Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        if (presentNonFront.Distinct().Count() != presentNonFront.Length)
        {
            throw new ArgumentException("비정면 이미지끼리 같은 ID를 쓸 수 없습니다.", nameof(rightImageId));
        }

        FrontImageId = frontImageId;
        RightImageId = rightImageId;
        BackImageId = backImageId;
        LeftImageId = leftImageId;
    }

    /// <summary>방향과 이미지의 짝 — 업로드가 이 순서로 돈다.</summary>
    public IEnumerable<(ViewDirection Direction, Guid ImageId)> Pairs()
    {
        yield return (ViewDirection.Front, FrontImageId);
        if (RightImageId.HasValue) yield return (ViewDirection.Right, RightImageId.Value);
        if (BackImageId.HasValue) yield return (ViewDirection.Back, BackImageId.Value);
        if (LeftImageId.HasValue) yield return (ViewDirection.Left, LeftImageId.Value);
    }
}
