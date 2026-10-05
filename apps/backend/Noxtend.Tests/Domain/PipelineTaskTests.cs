using Noxtend.Domain.Job;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Domain;

/// <summary>Design Ref: §8.2 #1~5 · #14d — 공정의 상태 전이와 리스.</summary>
public sealed class PipelineTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static PipelineTask Planned()
        => PipelineJob
            .Create(AssetCategory.Background, Guid.NewGuid(), Now)
            .PlanTask(TaskKind.Extract, ordinal: 0);

    // #1 — Claim: Pending → Running, 리스 설정
    [Fact]
    public void Claim_MovesPendingToRunningAndSetsLease()
    {
        var task = Planned();

        task.Claim(Now, Lease);

        Assert.Equal(TaskStatus.Running, task.Status);
        Assert.Equal(Now + Lease, task.LeaseExpiresAt);
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal(Now, task.StartedAt);
    }

    [Fact]
    public void Claim_RejectsTaskThatIsNotPending()
    {
        var task = Planned();
        task.Claim(Now, Lease);

        Assert.Throws<InvalidOperationException>(() => task.Claim(Now, Lease));
    }

    // #2 — RenewLease: 만료가 미래로 이동
    [Fact]
    public void RenewLease_MovesExpiryForward()
    {
        var task = Planned();
        task.Claim(Now, Lease);
        var later = Now + TimeSpan.FromMinutes(1);

        task.RenewLease(later, Lease);

        Assert.Equal(later + Lease, task.LeaseExpiresAt);
        // 갱신은 시도 횟수를 늘리지 않는다 — 같은 시도가 계속되고 있을 뿐이다
        Assert.Equal(1, task.AttemptCount);
    }

    [Fact]
    public void RenewLease_RejectsTaskThatIsNotRunning()
    {
        var task = Planned();

        Assert.Throws<InvalidOperationException>(() => task.RenewLease(Now, Lease));
    }

    // #3 — Succeed: Running 에서만 허용
    [Fact]
    public void Succeed_RequiresRunning()
    {
        var task = Planned();

        Assert.Throws<InvalidOperationException>(() => task.Succeed(Now));
    }

    [Fact]
    public void Succeed_ClearsLeaseAndStampsCompletion()
    {
        var task = Planned();
        task.Claim(Now, Lease);

        task.Succeed(Now);

        Assert.Equal(TaskStatus.Succeeded, task.Status);
        Assert.Null(task.LeaseExpiresAt);
        Assert.Equal(Now, task.CompletedAt);
    }

    // #4 — Fail: 사유·시각 기록
    [Fact]
    public void Fail_RecordsReasonAndTime()
    {
        var task = Planned();
        task.Claim(Now, Lease);

        task.Fail("PROVIDER_CALL_FAILED", Now);

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Equal("PROVIDER_CALL_FAILED", task.FailureReason);
        Assert.Equal(Now, task.CompletedAt);
        Assert.Null(task.LeaseExpiresAt);
    }

    // #5 — ReleaseForRetry: Running → Pending, AttemptCount 유지
    [Fact]
    public void ReleaseForRetry_ReturnsToPendingAndKeepsAttemptCount()
    {
        var task = Planned();
        task.Claim(Now, Lease);

        task.ReleaseForRetry();

        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.LeaseExpiresAt);
        // 시도 횟수가 유지되어야 스위퍼가 한도 초과를 판정할 수 있다
        Assert.Equal(1, task.AttemptCount);

        task.Claim(Now, Lease);
        Assert.Equal(2, task.AttemptCount);
    }

    // generation-rate-limiting §3② — 백오프: notBefore 를 지정하면 그 시각까지 재시도를 미룬다
    [Fact]
    public void ReleaseForRetry_WithNotBefore_SetsNotBeforeAndKeepsAttemptCount()
    {
        var task = Planned();
        task.Claim(Now, Lease);
        var notBefore = Now + TimeSpan.FromSeconds(5);

        task.ReleaseForRetry(notBefore);

        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Equal(notBefore, task.NotBefore);
        Assert.Equal(1, task.AttemptCount);
    }

    // notBefore 를 안 주면 기존 동작(즉시 재시도 가능) 그대로다 — 스위퍼 경로 회귀 방지
    [Fact]
    public void ReleaseForRetry_WithoutNotBefore_LeavesNotBeforeNull()
    {
        var task = Planned();
        task.Claim(Now, Lease);

        task.ReleaseForRetry();

        Assert.Null(task.NotBefore);
    }

    // 성공하면 이전 백오프 흔적(NotBefore)이 남아 있으면 안 된다
    [Fact]
    public void Succeed_ClearsNotBefore()
    {
        var task = Planned();
        task.Claim(Now, Lease);
        task.ReleaseForRetry(Now + TimeSpan.FromSeconds(5));
        task.Claim(Now, Lease);

        task.Succeed(Now);

        Assert.Null(task.NotBefore);
    }

    // #14d — Canceled 에서 Succeed 하면 예외. 결과 덮어쓰기 차단 (§2.2 안전장치)
    [Fact]
    public void Succeed_RejectedAfterCancel()
    {
        var task = Planned();
        task.Claim(Now, Lease);
        task.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => task.Succeed(Now));
        Assert.Equal(TaskStatus.Canceled, task.Status);
    }

    [Fact]
    public void Cancel_IsIdempotentOnTerminalTask()
    {
        var task = Planned();
        task.Claim(Now, Lease);
        task.Succeed(Now);

        // 취소가 성공을 되돌리면 안 된다 — 재취소는 무시한다
        task.Cancel(Now);

        Assert.Equal(TaskStatus.Succeeded, task.Status);
    }

    [Fact]
    public void IsLeaseExpired_TrueOnlyForRunningTaskPastExpiry()
    {
        var task = Planned();
        Assert.False(task.IsLeaseExpired(Now + TimeSpan.FromHours(1)));

        task.Claim(Now, Lease);
        Assert.False(task.IsLeaseExpired(Now + TimeSpan.FromMinutes(1)));
        Assert.True(task.IsLeaseExpired(Now + TimeSpan.FromMinutes(3)));
    }

    /// <summary>실패한 채로 끝난 공정은 사유를 지킨다 — 지우면 왜 실패했는지 못 본다.</summary>
    [Fact]
    public void Fail_KeepsTheReason()
    {
        var task = Planned();

        task.Claim(Now, Lease);
        task.Fail("STAGE_FAILED", Now);

        Assert.Equal("STAGE_FAILED", task.FailureReason);
    }
}
