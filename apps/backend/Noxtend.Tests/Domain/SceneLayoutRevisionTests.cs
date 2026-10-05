using Noxtend.Domain.Scene;

namespace Noxtend.Tests.Domain;

/// <summary>
/// SceneLayout revision 전이. Design Ref: background-similarity-tuning §4.1·§4.3 · D-02
///
/// **덮어쓰기가 없어야 거부·복원·동시성이 성립한다.** 후보가 실패하면 이전 장면이
/// 그대로 있어야 하고, 복원은 과거 행을 되살리는 게 아니라 값을 새 revision 으로
/// 복사한다 — 이력이 단조 증가해야 활성 전이 시점이 보존된다.
/// </summary>
public sealed class SceneLayoutRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    // ─── 생성 ───

    [Fact]
    public void ComposeActive_StartsActiveComposed()
    {
        var layout = Compose(revision: 1);

        Assert.Equal(SceneLayoutState.Active, layout.State);
        Assert.Equal(SceneLayoutOrigin.Composed, layout.Origin);
        Assert.Null(layout.ParentLayoutId);
    }

    /// <summary>후보는 비활성으로 태어난다 (D-07) — 활성은 재평가 성공 후에만.</summary>
    [Fact]
    public void CreateCandidate_IsInactiveAndPointsAtItsParent()
    {
        var parent = Compose(revision: 1);
        var adjusted = new[] { Instance(x: 2) };

        var candidate = SceneLayout.CreateCandidate(
            parent, revision: 2, adjusted, parent.Camera, parent.Light, Now);

        Assert.Equal(SceneLayoutState.Candidate, candidate.State);
        Assert.Equal(SceneLayoutOrigin.SimilarityAdjustment, candidate.Origin);
        Assert.Equal(parent.Id, candidate.ParentLayoutId);
        Assert.Equal(parent.SourceMeshSignature, candidate.SourceMeshSignature);
    }

    /// <summary>SC-03 — 후보를 만들어도 원본 revision 의 값은 그대로다.</summary>
    [Fact]
    public void CreateCandidate_LeavesTheParentUntouched()
    {
        var parent = Compose(revision: 1);
        var before = parent.Instances.Single();

        SceneLayout.CreateCandidate(
            parent, 2, [Instance(x: 99)], parent.Camera, parent.Light, Now);

        Assert.Equal(SceneLayoutState.Active, parent.State);
        Assert.Equal(before, parent.Instances.Single());
    }

    // ─── 채택·거부 ───

    [Fact]
    public void Adopt_SwapsActiveAndCandidate()
    {
        var parent = Compose(revision: 1);
        var candidate = Candidate(parent, revision: 2);

        parent.MarkSuperseded();
        candidate.Adopt();

        Assert.Equal(SceneLayoutState.Superseded, parent.State);
        Assert.Equal(SceneLayoutState.Active, candidate.State);
    }

    [Fact]
    public void Adopt_RefusesANonCandidate()
    {
        var active = Compose(revision: 1);

        Assert.Throws<InvalidOperationException>(active.Adopt);
    }

    [Fact]
    public void Reject_ClosesTheCandidateOnly()
    {
        var parent = Compose(revision: 1);
        var candidate = Candidate(parent, revision: 2);

        candidate.Reject();

        Assert.Equal(SceneLayoutState.Rejected, candidate.State);
        Assert.Equal(SceneLayoutState.Active, parent.State);
    }

    // ─── 복원 ───

    /// <summary>복원은 복사다 — 과거 행을 되살리면 revision 이 단조가 아니게 된다 (§4.3).</summary>
    [Fact]
    public void Restore_CopiesTheSourceIntoANewActiveRevision()
    {
        var old = Compose(revision: 1);
        old.MarkSuperseded();

        var restored = SceneLayout.CreateRestored(
            old, currentSignature: old.SourceMeshSignature, revision: 3, Now);

        Assert.Equal(SceneLayoutState.Active, restored.State);
        Assert.Equal(SceneLayoutOrigin.Restore, restored.Origin);
        Assert.Equal(3, restored.Revision);
        Assert.Equal(old.Id, restored.ParentLayoutId);
        Assert.Equal(old.Instances, restored.Instances);
        // 원본 행은 그대로 — 이력이다
        Assert.Equal(SceneLayoutState.Superseded, old.State);
    }

    /// <summary>mesh 가 바뀐 뒤의 복원은 거짓말이 된다 — 그 배치는 지금 mesh 의 것이 아니다.</summary>
    [Fact]
    public void Restore_RefusesAStaleMeshSignature()
    {
        var old = Compose(revision: 1);

        Assert.Throws<SceneRevisionStaleException>(() =>
            SceneLayout.CreateRestored(old, currentSignature: "다른서명", revision: 2, Now));
    }

    // ─── 설정 ───

    private static SceneLayout Compose(int revision)
        => SceneLayout.ComposeActive(
            Guid.NewGuid(),
            revision,
            [Instance(x: 1)],
            signature: "SIG-1",
            sourceMeshCount: 1,
            SceneStaging.ComposeCamera(null, []),
            SceneStaging.ComposeLight(null),
            Now);

    private static SceneLayout Candidate(SceneLayout parent, int revision)
        => SceneLayout.CreateCandidate(
            parent, revision, parent.Instances, parent.Camera, parent.Light, Now);

    private static SceneInstance Instance(double x)
        => new(Guid.NewGuid(), 0, x, 0, -3, 0, 1.5);
}
