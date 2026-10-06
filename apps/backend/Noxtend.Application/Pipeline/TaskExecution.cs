using Microsoft.Extensions.Logging;
using Noxtend.Application.Job;
using Noxtend.Domain.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 공정 하나의 **수명**을 관리한다 — 무엇을 하는 공정인지는 모른다.
///
/// Design Ref: §2.0 · §2.3 A-1
///
/// **사이클 #7 이 이것을 뽑은 이유.** Option B 는 텍스트와 이미지의 실행 경로를 나눈다.
/// 그런데 원안대로 핸들러를 둘로 만들면 툼스톤 확인 · <c>Claim</c> · 리스 갱신 루프 ·
/// 취소 감시 · 재시도 한도 · 오케스트레이터 통지가 **양쪽에 복제되고, 한쪽만 고치는
/// 순간 어긋난다.** 사이클 #5 에서 준비 판정이 두 곳에 흩어져 "스위퍼가 장면이 끝나기
/// 전에 추출·분해를 큐에 넣는" 결함이 실제로 났다.
///
/// **경계는 이렇다**: 나뉘는 것은 단계 지식과 공급자 호출(<paramref name="body"/>),
/// 공유되는 것은 공정의 수명이다. 리스·취소·재시도는 공정의 성질이지 단계의 성질이 아니다.
///
/// 세 가지가 동시에 돌아간다.
/// ① 본문 — 오래 걸리고 끊길 수 있다
/// ② 리스 갱신 — 살아 있음을 알려 스위퍼가 뺏어가지 않게 한다
/// ③ 취소 감시 — ②의 시점에 작업 상태를 함께 본다. **새 메커니즘을 만들지 않았다**
/// </summary>
public sealed class TaskExecution(
    IJobRepository jobs,
    JobOrchestrator orchestrator,
    IClock clock,
    IDelayedActionScheduler scheduler,
    ILogger<TaskExecution> logger)
{
    /// <summary>
    /// 공정을 집어 <paramref name="body"/> 를 돌리고 결과를 확정한다.
    /// </summary>
    /// <param name="body">
    /// 단계가 하는 일. **반영 함수를 돌려준다** — 해석·검증은 던질 수 있고 반영은 던지지
    /// 않는다는 `IStage` 2단계 규약(사이클 #5)을 이미지 경로에도 그대로 적용한 것이다.
    /// 반영이 성공 확정 뒤에 오므로, 반영이 던지면 이미 종료된 공정을 실패시킬 수 없다.
    /// </param>
    /// <param name="classify">
    /// 단계별 예외를 실패 코드와 재시도 여부로 옮긴다. <c>null</c> 을 돌려주면 분류하지
    /// 못한 것으로 보고 즉시 실패시킨다 — 모르는 예외를 재시도하면 원인 모를 비용이 는다.
    /// </param>
    public async Task<RunTaskOutcome> RunAsync(
        Guid taskId,
        TaskExecutionPolicy policy,
        Func<PipelineJob, PipelineTask, CancellationToken, Task<Action<PipelineJob>>> body,
        Func<Exception, TaskFailure?> classify,
        CancellationToken ct)
    {
        var job = await jobs.GetByTaskAsync(taskId, ct);
        var task = job?.Tasks.FirstOrDefault(t => t.Id == taskId);

        if (job is null || task is null)
        {
            // 정본에 없는 메시지다. 되살릴 근거가 없으므로 버린다 (Ack)
            return RunTaskOutcome.Skipped;
        }

        // 툼스톤 처리. 대기 중 취소된 작업의 메시지는 큐에 남아 있다 —
        // 꺼낸 시점에 상태를 보고 처리 없이 Ack 한다
        if (job.IsTerminal || task.IsTerminal)
        {
            return RunTaskOutcome.Skipped;
        }

        // 다른 워커가 이미 집었다. 중복 배달은 안전해야 하므로 예외 없이 넘긴다
        if (task.Status != Domain.Job.TaskStatus.Pending)
        {
            return RunTaskOutcome.Skipped;
        }

        if (!job.IsCurrentTask(task)) return RunTaskOutcome.Skipped;
        task.Claim(clock.Now, policy.Lease);
        var attempt = task.AttemptCount;
        job.MarkRunning();
        try { await jobs.SaveChangesAsync(ct); }
        catch (ConcurrencyConflictException) { return RunTaskOutcome.Skipped; }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ownershipVersion = task.RowVersion?.ToArray();
        var renewal = RenewLeaseLoopAsync(job.Id, taskId, attempt, ownershipVersion, policy, cts);
        Action<PipelineJob>? commit = null;
        TaskFailure? failure = null;
        var canceled = false;
        try
        {
            commit = await body(job, task, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { canceled = true; }
        catch (Exception ex)
        {
            LogFailure(taskId, task.Model, ex);
            failure = classify(ex) ?? TaskFailure.Fail(ex.GetType().Name);
        }
        finally
        {
            // 최종 조회와 리스 루프의 동일 DbContext 동시 접근 차단
            await cts.CancelAsync();
            ownershipVersion = await renewal;
        }

        // 공급자 재호출 없이 최신 aggregate에 결과만 다시 반영
        for (var conflict = 0; ; conflict++)
        {
            var current = await jobs.ReloadAsync(job.Id, ct);
            var owned = current?.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (current?.Status == JobStatus.Canceled || owned?.Status == Domain.Job.TaskStatus.Canceled)
                return RunTaskOutcome.Canceled;
            if (current is null || current.IsTerminal || owned is null
                || !owned.IsOwnedBy(attempt, clock.Now, ownershipVersion) || !current.IsCurrentTask(owned))
                return RunTaskOutcome.Skipped;
            TimeSpan? retryDelay = null;
            try
            {
                if (canceled) owned.Cancel(clock.Now);
                else if (failure is { } failed) retryDelay = Apply(owned, failed, policy);
                else
                {
                    owned.Succeed(clock.Now);
                    commit!(current);
                }
                await jobs.SaveChangesAsync(ct);
            }
            catch (ConcurrencyConflictException) when (conflict < 4) { continue; }

            if (retryDelay is { } delay) ScheduleRetry(current.Id, delay);
            var outcome = owned.Status switch
            {
                Domain.Job.TaskStatus.Succeeded => RunTaskOutcome.Succeeded,
                Domain.Job.TaskStatus.Canceled => RunTaskOutcome.Canceled,
                _ => RunTaskOutcome.Failed,
            };
            // 공개 이후 충돌은 결과 재반영 없이 상태 조정만 재시도
            for (var reconcile = 0; ; reconcile++)
            {
                try
                {
                    await orchestrator.OnTaskCompletedAsync(current, ct);
                    return outcome;
                }
                catch (ConcurrencyConflictException) when (reconcile < 4)
                {
                    current = await jobs.ReloadAsync(job.Id, ct);
                    if (current is null) return outcome;
                }
            }
        }
    }

    /// <summary>
    /// 실패 원인을 서버 로그에 남긴다.
    ///
    /// **§4.2 #13 은 클라이언트 응답에 대한 규칙인데, 로그까지 지워져 있었다.** 저장되는
    /// 것은 실패 코드뿐이라 운영자가 "왜" 를 알 방법이 없었다 — 실제 공급자로 첫 관통을
    /// 시도하다 이 공백이 드러났다. 로그는 서버 안에 있고, 진단 없는 실패보다는
    /// 진단 가능한 실패가 낫다.
    /// </summary>
    private void LogFailure(Guid taskId, string? model, Exception ex)
        => logger.LogError(
            ex,
            "공정 {TaskId} 실패 (모델 {Model})",
            taskId,
            model ?? "(없음)");

    /// <summary>
    /// 분류된 실패를 공정 상태에 반영한다.
    ///
    /// **재시도 판정이 여기 한 곳에 있는 이유**: 두 핸들러가 각자 판단하면 같은 성질의
    /// 실패가 경로에 따라 다르게 처리된다. 한도를 넘으면 확정 실패다 — 프롬프트가
    /// 잘못된 경우 무한히 돌면 비용만 태운다 (Plan R-7).
    /// </summary>
    private TimeSpan? Apply(PipelineTask task, TaskFailure failure, TaskExecutionPolicy policy)
    {
        // 취소가 먼저 도착했다 — 사용자의 결정이 나중에 도착한 오류보다 우선한다.
        // 취소 직후 공급자가 별개의 예외를 던지는 경합이 실제로 있다
        if (task.IsTerminal)
        {
            return null;
        }

        if (failure.Disposition == FailureDisposition.Fail
            || task.AttemptCount >= policy.MaxAttempts)
        {
            task.Fail(failure.Reason, clock.Now);
            return null;
        }

        // 대기로 되돌리면 오케스트레이터가 다시 적재한다 (§2.2).
        // AttemptCount 는 유지되므로 한도가 실제로 걸린다.
        //
        // 즉시 재적재하면 레이트리밋에 다시 걸린다 (generation-rate-limiting §1·§3②) —
        // 지수 백오프 + 지터로 재적재 시각을 미룬다. IsReadyToRun 이 이 시각을 본다
        var delay = RetryBackoff.NextDelay(task.AttemptCount);
        task.ReleaseForRetry(clock.Now + delay);

        return delay;
    }

    private void ScheduleRetry(Guid jobId, TimeSpan delay)
    {
        scheduler.Schedule(delay, async ct =>
        {
            var job = await jobs.ReloadAsync(jobId, ct);
            if (job is not null && !job.IsTerminal)
                await orchestrator.OnTaskCompletedAsync(job, ct);
        });
    }

    /// <summary>
    /// 리스를 주기적으로 갱신하며 **그 시점에 작업이 취소됐는지 함께 확인한다.**
    ///
    /// Design Ref: §2.2 — 취소를 위한 새 메커니즘을 만들지 않은 지점이다. 워커는 어차피
    /// 리스를 갱신해야 하므로 상태 확인을 얹으면 된다. 취소 지연은 갱신 주기와 같고,
    /// 10분짜리 작업에서 15초는 문제가 되지 않는다.
    /// </summary>
    private async Task<byte[]?> RenewLeaseLoopAsync(
        Guid jobId, Guid taskId, int attempt, byte[]? ownershipVersion, TaskExecutionPolicy policy, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(policy.LeaseRenew, cts.Token);
                // 루프 종료가 진행 중 SQL을 취소하지 않도록 완료 후 최종 조회와 합류
                var current = await jobs.ReloadAsync(jobId, CancellationToken.None);
                var task = current?.Tasks.FirstOrDefault(t => t.Id == taskId);
                if (current is null || current.IsTerminal || task is null
                    || !task.IsOwnedBy(attempt, clock.Now, ownershipVersion) || !current.IsCurrentTask(task))
                {
                    await cts.CancelAsync();
                    return ownershipVersion;
                }
                task.RenewLease(clock.Now, policy.Lease);
                try
                {
                    await jobs.SaveChangesAsync(CancellationToken.None);
                    ownershipVersion = task.RowVersion?.ToArray();
                }
                catch (ConcurrencyConflictException) { }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "공정 {TaskId} 리스 소유권 확인 실패", taskId);
            await cts.CancelAsync();
            throw;
        }
        return ownershipVersion;
    }
}

/// <summary>
/// 공정 수명의 시간·횟수 노브.
///
/// **매개변수인 이유** (§2.3 A-7): 이미지 생성은 장당 수십 초~분이라 텍스트와 지연
/// 자릿수가 다르다. 리스를 공유하면 생성 공정이 매번 스위퍼에 뺏긴다. 반면
/// <see cref="MaxAttempts"/> 는 "몇 번 다시 물어볼 것인가" 라는 같은 판단이라 공유한다.
/// </summary>
public sealed record TaskExecutionPolicy(TimeSpan Lease, TimeSpan LeaseRenew, int MaxAttempts);

/// <summary>실패를 다시 걸어볼 것인가, 여기서 끝낼 것인가.</summary>
public enum FailureDisposition
{
    /// <summary>
    /// 다시 걸어본다 — **공급자가 대답은 했는데 약속을 어긴 경우**.
    ///
    /// LLM 출력은 비결정적이라 같은 프롬프트로도 결과가 다르다. 실제로 파츠 하나를
    /// 두 번 서술해 작업 전체가 실패한 일이 있었는데, 다시 돌리면 대개 성공한다.
    /// 한도는 <see cref="TaskExecutionPolicy.MaxAttempts"/> 가 건다.
    /// </summary>
    Retry,

    /// <summary>다시 걸어도 같다 — 인증 실패 · 크레딧 부족 · 크기 상한 초과.</summary>
    Fail,
}

/// <summary>단계가 판단한 실패 — 저장될 코드와 재시도 여부.</summary>
public readonly record struct TaskFailure(string Reason, FailureDisposition Disposition)
{
    public static TaskFailure Retry(string reason) => new(reason, FailureDisposition.Retry);

    public static TaskFailure Fail(string reason) => new(reason, FailureDisposition.Fail);
}

/// <summary>
/// 공정 하나를 처리하는 유스케이스 — 워커가 보는 유일한 면.
///
/// Design Ref: §2.0 (사이클 #7)
///
/// **이것이 있어야 워커 클래스가 두 벌이 되지 않는다.** Option B 는 실행 경로를 나누지만
/// 큐에서 꺼내 넘기고 Ack 하는 일은 단계와 무관하다. 워커가 구현 타입을 직접 해석하면
/// 단계가 늘 때마다 워커도 늘어난다.
/// </summary>
public interface ITaskHandler
{
    Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct);
}

public enum RunTaskOutcome
{
    Succeeded,
    Failed,
    Canceled,

    /// <summary>처리할 것이 없었다. 메시지는 Ack 한다.</summary>
    Skipped,
}
