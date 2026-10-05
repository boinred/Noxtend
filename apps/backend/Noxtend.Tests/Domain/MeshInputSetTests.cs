using Noxtend.Domain.Job;

namespace Noxtend.Tests.Domain;

/// <summary>
/// `MeshInputSet` 생성자 불변식 — merge-gate 리뷰 F7.
///
/// 비정면끼리 같은 이미지 ID를 두 슬롯에 겹쳐 쓸 수 없다. 지금 있는 호출 경로로는
/// 도달하지 못하지만(`Both`는 서로 다른 방향을 조회하고, `Mirror*`는 항상 새 이미지를
/// 만든다), 이 타입 자체가 지켜야 할 불변식이다 — 한 장을 두 방향에 겹쳐 쓰면 공급자에게
/// "이 방향은 다르게 생겼다"는 거짓 신호를 준다.
/// </summary>
public sealed class MeshInputSetTests
{
    [Fact]
    public void RightAndLeftSameId_Throws()
    {
        var front = Guid.NewGuid();
        var shared = Guid.NewGuid();

        Assert.Throws<ArgumentException>(
            () => new MeshInputSet(front, rightImageId: shared, leftImageId: shared));
    }

    [Fact]
    public void BackAndLeftSameId_Throws()
    {
        var front = Guid.NewGuid();
        var shared = Guid.NewGuid();

        Assert.Throws<ArgumentException>(
            () => new MeshInputSet(front, backImageId: shared, leftImageId: shared));
    }

    [Fact]
    public void AllDifferentIds_Succeeds()
    {
        var inputs = new MeshInputSet(
            Guid.NewGuid(), rightImageId: Guid.NewGuid(), backImageId: Guid.NewGuid(), leftImageId: Guid.NewGuid());

        Assert.NotEqual(inputs.RightImageId, inputs.LeftImageId);
    }
}
