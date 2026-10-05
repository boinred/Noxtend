using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.1 — 가장 위험한 로직을 인프라 없이 검증한다.
/// 실제 LLM·Redis·DB 어느 것도 뜨지 않는다.
///
/// 사이클 #5 부터 공정이 셋이다. `job.Tasks[0]` 은 첫 공정(Analyze)이고,
/// 작업이 성공하려면 셋을 다 돌려야 한다.
/// </summary>
public sealed class RunTaskHandlerTests
{
    // #11 — Fake 성공 → 공정·작업 Succeeded
    [Fact]
    public async Task Succeeds_AndCompletesJob_WhenProviderReturnsResult()
    {
        var fixture = new PipelineFixture(FakeLlmProvider.Succeeding("등대", "목조 부두"));
        var job = await fixture.StartJobAsync();

        await fixture.RunAllStagesAsync(job);

        Assert.All(job.Tasks, t => Assert.Equal(TaskStatus.Succeeded, t.Status));
        Assert.Equal(JobStatus.Succeeded, job.Status);

        // 장면 명세가 채워졌다 — 조립을 좌우하는 셋이 핵심이다
        Assert.NotNull(job.Scene);
        Assert.Equal(0.55, job.Scene!.Camera.HorizonY);

        Assert.Equal(["등대", "목조 부두"], job.Parts.Select(p => p.Name));

        // 분해가 파츠를 채웠다
        Assert.All(job.Parts, p => Assert.NotNull(p.Description));
        Assert.All(job.Parts, p => Assert.NotEmpty(p.Placements));
        Assert.Equal([1, 2], job.Parts.Select(p => p.DepthOrder));
    }

    // 공정이 셋이라는 것 자체가 계약이다 (FR-00)
    [Fact]
    public async Task PlansThreeStagesInOrder()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        Assert.Equal(
            [TaskKind.Analyze, TaskKind.Extract, TaskKind.Decompose],
            job.Tasks.OrderBy(t => t.Ordinal).Select(t => t.Kind));

        // 선형 의존 — 각 공정이 직전을 가리킨다
        var ordered = job.Tasks.OrderBy(t => t.Ordinal).ToList();
        Assert.Null(ordered[0].DependsOnTaskId);
        Assert.Equal(ordered[0].Id, ordered[1].DependsOnTaskId);
        Assert.Equal(ordered[1].Id, ordered[2].DependsOnTaskId);

        // 의존이 없는 첫 공정만 큐에 들어간다
        Assert.Single(fixture.Queue.Enqueued);
    }

    // 중간 공정이 실패하면 뒤가 돌지 않는다 (§8.1 #4)
    [Fact]
    public async Task LaterStagesDoNotRun_WhenAnEarlierStageFails()
    {
        var fixture = new PipelineFixture(
            FakeLlmProvider.Failing(new ProviderCallFailedException("401")));
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        Assert.Equal(JobStatus.Failed, job.Status);

        var ordered = job.Tasks.OrderBy(t => t.Ordinal).ToList();
        Assert.Equal(TaskStatus.Failed, ordered[0].Status);
        // 뒤 두 공정은 대기 상태 그대로다 — 적재되지 않았다
        Assert.Equal(TaskStatus.Pending, ordered[1].Status);
        Assert.Equal(TaskStatus.Pending, ordered[2].Status);
        Assert.Single(fixture.Queue.Enqueued);
    }

    // #12 — Fake 예외 → Failed + 사유
    [Fact]
    public async Task Fails_WithReason_WhenProviderThrows()
    {
        var fixture = new PipelineFixture(
            FakeLlmProvider.Failing(new ProviderCallFailedException("401")));
        var job = await fixture.StartJobAsync();

        var outcome = await fixture.RunFirstStageAsync(job);

        Assert.Equal(RunTaskOutcome.Failed, outcome);
        Assert.Equal(TaskStatus.Failed, job.Tasks[0].Status);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(ErrorCode.ProviderCallFailed, job.Tasks[0].FailureReason);

        // 공급자 원문("401")이 사유에 섞이지 않는다 — 키가 메시지에 들어올 수 있다 (§4.2 #13)
        Assert.DoesNotContain("401", job.Tasks[0].FailureReason);
    }

    /// <summary>
    /// **일시적 공급자 실패는 다시 걸어본다.**
    ///
    /// 실제로 OpenAI 가 500 을 한 번 냈을 때 작업 전체가 실패했다. 그때는 호출 실패를
    /// 전부 영구로 다뤘고, 영구와 일시를 가르지 않으면 남의 사정 때문에 10분짜리
    /// 작업을 버리게 된다.
    /// </summary>
    [Fact]
    public async Task RetriesTransientProviderFailure()
    {
        var attempt = 0;
        var fixture = new PipelineFixture(FakeLlmProvider.Throwing(() =>
            ++attempt == 1
                ? new ProviderCallFailedException("500", isTransient: true)
                : null));

        var job = await fixture.StartJobAsync();
        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);
        Assert.Equal(TaskStatus.Succeeded, analyze.Status);
        Assert.Equal(2, analyze.AttemptCount);
    }

    // generation-rate-limiting §3② — 일시적 실패는 즉시가 아니라 백오프 뒤에 재시도돼야 한다
    [Fact]
    public async Task RetryableFailure_SetsNotBeforeWithinBackoffWindow()
    {
        var fixture = new PipelineFixture(FakeLlmProvider.Failing(
            new ProviderCallFailedException("500", isTransient: true)));
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);
        Assert.Equal(TaskStatus.Pending, analyze.Status);
        Assert.NotNull(analyze.NotBefore);

        // 1차 실패 → 기준 지연 5초, 지터 ±25% = [3.75초, 6.25초]
        var delay = analyze.NotBefore!.Value - fixture.Clock.Now;
        Assert.InRange(delay, TimeSpan.FromSeconds(3.75), TimeSpan.FromSeconds(6.25));
    }

    // 백오프가 실제로 큐 재적재를 막는지 — NotBefore 만 세팅되고 안 쓰이면 의미가 없다
    [Fact]
    public async Task RetryableFailure_DoesNotReenqueueImmediately()
    {
        var fixture = new PipelineFixture(FakeLlmProvider.Failing(
            new ProviderCallFailedException("500", isTransient: true)));
        var job = await fixture.StartJobAsync();
        var enqueuedBeforeFailure = fixture.Queue.Enqueued.Count(e => e.Kind == TaskKind.Analyze);

        await fixture.RunFirstStageAsync(job);

        var enqueuedAfterFailure = fixture.Queue.Enqueued.Count(e => e.Kind == TaskKind.Analyze);
        Assert.Equal(enqueuedBeforeFailure, enqueuedAfterFailure);
    }

    [Fact]
    public async Task DoesNotRetryPermanentProviderFailure()
    {
        // 401·402 는 몇 번을 걸어도 같다. 재시도가 비용과 지연만 늘린다
        var fixture = new PipelineFixture(
            FakeLlmProvider.Failing(new ProviderCallFailedException("401")));

        var job = await fixture.StartJobAsync();
        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);
        Assert.Equal(TaskStatus.Failed, analyze.Status);
        Assert.Equal(1, analyze.AttemptCount);
    }

    // #13 — Fake 형식 위반 → PROVIDER_BAD_RESPONSE
    [Fact]
    public async Task Fails_WithBadResponseCode_WhenProviderBreaksContract()
    {
        var fixture = new PipelineFixture(
            FakeLlmProvider.Failing(new ProviderBadResponseException("파싱 실패")));
        var job = await fixture.StartJobAsync();

        // 계약 위반은 한도까지 재시도된다 — 확정 실패를 보려면 소진시킨다
        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        Assert.Equal(ErrorCode.ProviderBadResponse, job.Tasks[0].FailureReason);
    }

    [Fact]
    public async Task Fails_WithSceneIncomplete_WhenRequiredFieldsAreMissing()
    {
        // 형식은 JSON 이지만 조립에 필요한 셋이 없다. 성공으로 저장하면
        // 뒤 단계들이 기준선 없이 돌게 된다 (FR-05).
        //
        // Check 단계 G-1: 예전에는 이것이 PROVIDER_BAD_RESPONSE 로 나갔다.
        // "재시도하면 될 문제" 와 "프롬프트를 고쳐야 할 문제" 가 구분되지 않았다
        var fixture = new PipelineFixture(
            FakeLlmProvider.Returning(_ => """{"timeOfDay": "해질녘"}"""));
        var job = await fixture.StartJobAsync();

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        Assert.Equal(ErrorCode.SceneIncomplete, job.Tasks[0].FailureReason);
        Assert.Null(job.Scene);
    }

    // #14 — 취소 토큰 → Canceled, 결과 미저장
    [Fact]
    public async Task Cancels_WithoutStoringResult_WhenTokenIsCanceled()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var outcome = await fixture.Run.HandleAsync(job.Tasks[0].Id, cts.Token);

        Assert.Equal(RunTaskOutcome.Canceled, outcome);
        Assert.Equal(TaskStatus.Canceled, job.Tasks[0].Status);
        // 결과가 저장되지 않았다는 것이 검증 대상이다
        Assert.Null(job.Scene);
        Assert.Empty(job.Parts);
    }

    // #14b — 리스 갱신 시점에 작업이 Canceled → CancellationToken 취소
    [Fact]
    public async Task Cancels_WhenJobIsCanceledDuringProviderCall()
    {
        // 갱신 주기를 짧게 잡아 "갱신 시점에 상태를 본다" 를 실제로 재현한다 (§2.2)
        var fixture = new PipelineFixture(
            FakeLlmProvider.Succeeding().WithDelay(TimeSpan.FromSeconds(10)),
            new JobOptions { LeaseSeconds = 120, LeaseRenewSeconds = 0.05, MaxAttempts = 3 });

        var job = await fixture.StartJobAsync();

        var running = fixture.Run.HandleAsync(job.Tasks[0].Id, CancellationToken.None);
        await Task.Delay(100);

        var canceled = await fixture.Cancel.HandleAsync(job.Id, CancellationToken.None);
        Assert.True(canceled.IsSuccess);

        // 워커가 스스로 끊는다. 10초짜리 호출을 기다리지 않는다
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunTaskOutcome.Canceled, outcome);
        Assert.Equal(JobStatus.Canceled, job.Status);
        Assert.Null(job.Scene);
    }

    // #14c — 꺼낸 공정의 작업이 이미 Canceled → 처리 없이 Ack (툼스톤)
    [Fact]
    public async Task Skips_WhenJobWasAlreadyCanceled()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.Cancel.HandleAsync(job.Id, CancellationToken.None);

        var outcome = await fixture.Run.HandleAsync(job.Tasks[0].Id, CancellationToken.None);

        Assert.Equal(RunTaskOutcome.Skipped, outcome);
        Assert.Equal(TaskStatus.Canceled, job.Tasks[0].Status);
        Assert.Null(job.Scene);
    }

    [Fact]
    public async Task Skips_WhenTaskIdIsUnknown()
    {
        var fixture = new PipelineFixture();

        var outcome = await fixture.Run.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(RunTaskOutcome.Skipped, outcome);
    }

    [Fact]
    public async Task Skips_WhenTaskWasAlreadyClaimedByAnotherWorker()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        job.Tasks[0].Claim(fixture.Clock.Now, fixture.Options.Lease);

        // 중복 배달은 안전해야 한다 — 예외가 아니라 건너뛴다
        var outcome = await fixture.Run.HandleAsync(job.Tasks[0].Id, CancellationToken.None);

        Assert.Equal(RunTaskOutcome.Skipped, outcome);
        Assert.Equal(1, job.Tasks[0].AttemptCount);
    }

    /// <summary>공급자가 매번 같은 헤더 값을 실어 보낸다고 가정한 고정 응답.</summary>
    private sealed class RateLimitHeaderLlmProvider(
        int remaining, TimeSpan resetAfter,
        int? remainingTokens = null, TimeSpan? tokensResetAfter = null) : ILlmProvider
    {
        public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            var baseResult = await FakeLlmProvider.Succeeding().CompleteAsync(request, ct);
            return baseResult with
            {
                RateLimitRemainingRequests = remaining,
                RateLimitResetAfter = resetAfter,
                RateLimitRemainingTokens = remainingTokens,
                RateLimitResetTokensAfter = tokensResetAfter,
            };
        }
    }

    // text-generation-rate-limiting §구현변경-5 — 호출 성공 후 헤더 값이 RateLimiter에 기록되는지
    [Fact]
    public async Task RecordsRateLimitHeaders_AfterASuccessfulCall()
    {
        var provider = new RateLimitHeaderLlmProvider(
            remaining: 2, resetAfter: TimeSpan.FromSeconds(45));
        var fixture = new PipelineFixture(provider);
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        var status = await fixture.RateLimiter.GetStatusAsync(
            job.Tasks[0].ProviderConfigId!.Value, CancellationToken.None);
        Assert.Equal(2, status!.RemainingRequests);
        Assert.Equal(fixture.Clock.Now + TimeSpan.FromSeconds(45), status.ResetsAt);
    }

    // TPM(토큰) 헤더도 같이 기록되는지 — RPM 만 기록하면 토큰 축 게이트가 항상 통과한다
    [Fact]
    public async Task RecordsTokenRateLimitHeaders_AfterASuccessfulCall()
    {
        var provider = new RateLimitHeaderLlmProvider(
            remaining: 5, resetAfter: TimeSpan.FromMinutes(1),
            remainingTokens: 1500, tokensResetAfter: TimeSpan.FromSeconds(8));
        var fixture = new PipelineFixture(provider);
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        var status = await fixture.RateLimiter.GetStatusAsync(
            job.Tasks[0].ProviderConfigId!.Value, CancellationToken.None);
        Assert.Equal(1500, status!.RemainingTokens);
        Assert.Equal(fixture.Clock.Now + TimeSpan.FromSeconds(8), status.TokensResetAt);
    }

    // 남은 횟수가 0으로 기록돼 있으면, 다음 호출 전에 초기화 시각까지 실제로 기다린다
    [Fact]
    public async Task WaitsForResetBeforeCalling_WhenNoRequestsRemain()
    {
        var provider = new RateLimitHeaderLlmProvider(
            remaining: 5, resetAfter: TimeSpan.FromMinutes(1));
        var fixture = new PipelineFixture(provider);
        var job = await fixture.StartJobAsync();

        // 이전 호출이 "이제 없다"고 알려준 상태를 미리 만들어 둔다
        await fixture.RateLimiter.UpdateAsync(
            job.Tasks[0].ProviderConfigId!.Value,
            new RateLimitStatus(RemainingRequests: 0, ResetsAt: fixture.Clock.Now + TimeSpan.FromMilliseconds(30)),
            CancellationToken.None);

        var before = DateTimeOffset.UtcNow;
        await fixture.RunFirstStageAsync(job);
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.True(elapsed >= TimeSpan.FromMilliseconds(25));
    }

    // #25 — IClock 고정 → 타임스탬프 결정적
    [Fact]
    public async Task Timestamps_ComeFromTheInjectedClock()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        await fixture.RunAllStagesAsync(job);

        Assert.Equal(fixture.Clock.Now, job.CreatedAt);
        Assert.Equal(fixture.Clock.Now, job.CompletedAt);
        Assert.Equal(fixture.Clock.Now, job.Tasks[0].CompletedAt);
    }
}
