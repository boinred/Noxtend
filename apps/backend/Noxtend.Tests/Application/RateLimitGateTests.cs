using Noxtend.Application.Generation;
using Noxtend.Domain.Ports;

namespace Noxtend.Tests.Application;

/// <summary>
/// generation-rate-limiting §3① — 호출 전 확인·대기.
///
/// Redis 없이 순수 로직만 검증한다 — <see cref="FakeRateLimiter"/> 가 §8.1 "인프라
/// 없는 파이프라인"과 같은 이유로 Redis 를 대신한다.
/// </summary>
public sealed class RateLimitGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProviderConfigId = Guid.NewGuid();

    // 기록된 상태가 없으면(첫 호출) 대기하지 않는다 — "정보 없음"을 "0"으로 오판하면 안 된다
    [Fact]
    public async Task WaitIfNeeded_NoRecordedStatus_DoesNotWait()
    {
        var limiter = new FakeRateLimiter();
        var gate = new RateLimitGate(limiter, FixedClock.At(2026, 7, 28, 12, 0));

        var waited = await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);

        Assert.False(waited);
    }

    // 남은 횟수가 있으면 대기하지 않는다
    [Fact]
    public async Task WaitIfNeeded_RemainingRequestsPositive_DoesNotWait()
    {
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            new RateLimitStatus(RemainingRequests: 3, ResetsAt: Now + TimeSpan.FromMinutes(1)),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, FixedClock.At(2026, 7, 28, 12, 0));

        var waited = await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);

        Assert.False(waited);
    }

    // 남은 횟수가 0이고 초기화 시각이 아직 안 왔으면 그 시각까지 기다린다
    [Fact]
    public async Task WaitIfNeeded_NoRemainingRequests_WaitsUntilReset()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var resetsAt = clock.Now + TimeSpan.FromMilliseconds(30);
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            new RateLimitStatus(RemainingRequests: 0, ResetsAt: resetsAt),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, clock);

        var before = DateTimeOffset.UtcNow;
        var waited = await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.True(waited);
        // 실제 시계로 30ms 가까이 흘렀는지만 확인한다 — 논리 시계(FixedClock)는
        // 안 흐르므로 "얼마나 기다렸는가"는 실제 경과 시간으로만 검증할 수 있다
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(25));
    }

    // TPM(토큰) 한도도 RPM(요청 수)과 별개로 OpenAI 가 거는 축이다 — 요청 수는 남아도
    // 토큰이 바닥나면 429가 난다(OpenAI 공식 문서: "Rate limits can be hit across any
    // of the options depending on what occurs first"). text-generation-rate-limiting
    // 후속 — 요청 카운터만 보던 게이트를 토큰 카운터도 보게 확장한다
    [Fact]
    public async Task WaitIfNeeded_NoRemainingTokens_WaitsUntilTokensReset()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var tokensResetAt = clock.Now + TimeSpan.FromMilliseconds(30);
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            // 요청 수는 넉넉하다 — 토큰만 바닥났다
            new RateLimitStatus(
                RemainingRequests: 100, ResetsAt: clock.Now + TimeSpan.FromMinutes(1),
                RemainingTokens: 0, TokensResetAt: tokensResetAt),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, clock);

        var before = DateTimeOffset.UtcNow;
        var waited = await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.True(waited);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(25));
    }

    // 독립 리뷰 지적 — TPM 의 "바닥"은 정확히 0 이 아니라 직전 호출이 쓴 토큰 수다.
    // "1,500 남았는데 직전 호출이 3,000 을 썼다" 면 다음 비슷한 호출도 못 버틸
    // 가능성이 높으므로, 정확히 0 이 아니어도 미리 기다려야 한다
    [Fact]
    public async Task WaitIfNeeded_RemainingTokensBelowLastCallSize_WaitsEvenIfNotExactlyZero()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var tokensResetAt = clock.Now + TimeSpan.FromMilliseconds(30);
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            new RateLimitStatus(
                RemainingRequests: 100, ResetsAt: clock.Now + TimeSpan.FromMinutes(1),
                RemainingTokens: 1500, TokensResetAt: tokensResetAt,
                LastCallTokens: 3000),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, clock);

        var before = DateTimeOffset.UtcNow;
        var waited = await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.True(waited);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(25));
    }

    // 남은 토큰이 직전 호출 크기보다 넉넉하면 대기하지 않는다
    [Fact]
    public async Task WaitIfNeeded_RemainingTokensAboveLastCallSize_DoesNotWait()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            new RateLimitStatus(
                RemainingRequests: 100, ResetsAt: clock.Now + TimeSpan.FromMinutes(1),
                RemainingTokens: 5000, TokensResetAt: clock.Now + TimeSpan.FromMinutes(1),
                LastCallTokens: 3000),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, clock);

        var waited = await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);

        Assert.False(waited);
    }

    // 둘 다 바닥났으면 더 늦게 풀리는 쪽까지 기다려야 한다 — 먼저 풀리는 쪽만 보고
    // 나가면 나머지 축에서 곧바로 다시 429가 난다
    [Fact]
    public async Task WaitIfNeeded_BothExhausted_WaitsUntilTheLaterReset()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            new RateLimitStatus(
                RemainingRequests: 0, ResetsAt: clock.Now + TimeSpan.FromMilliseconds(10),
                RemainingTokens: 0, TokensResetAt: clock.Now + TimeSpan.FromMilliseconds(60)),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, clock);

        var before = DateTimeOffset.UtcNow;
        await gate.WaitIfNeededAsync(ProviderConfigId, CancellationToken.None);
        var elapsed = DateTimeOffset.UtcNow - before;

        // 요청 쪽(10ms)만 봤다면 여기서 이미 빠져나왔을 것 — 토큰 쪽(60ms)까지 기다렸는지 확인
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(55));
    }

    // 헤더에 토큰 값이 있으면 기록되고, 다음 조회에서 그대로 나온다
    [Fact]
    public async Task RecordAsync_WithTokenHeaderValues_StoresTokenResetTime()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        var gate = new RateLimitGate(limiter, clock);

        await gate.RecordAsync(
            ProviderConfigId,
            new RateLimitHeaders(
                RemainingRequests: 10, ResetAfter: TimeSpan.FromSeconds(30),
                RemainingTokens: 500, TokensResetAfter: TimeSpan.FromSeconds(12)),
            CancellationToken.None);

        var status = await limiter.GetStatusAsync(ProviderConfigId, CancellationToken.None);
        Assert.Equal(500, status!.RemainingTokens);
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(12), status.TokensResetAt);
    }

    // 독립 리뷰 지적 — 요청 축이 파싱 실패해서(예: reset 형식이 예상과 다름) null 로
    // 와도, 같이 온 토큰 축은 통째로 버려지지 않고 그대로 기록돼야 한다
    [Fact]
    public async Task RecordAsync_TokensOnlyWithoutRequestReset_StillRecordsTokens()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        var gate = new RateLimitGate(limiter, clock);

        // remainingRequests 는 왔지만(헤더 자체는 있었다) resetAfter 파싱은 실패했다고 가정
        await gate.RecordAsync(
            ProviderConfigId,
            new RateLimitHeaders(RemainingTokens: 700, TokensResetAfter: TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        var status = await limiter.GetStatusAsync(ProviderConfigId, CancellationToken.None);
        Assert.Equal(700, status!.RemainingTokens);
        Assert.Null(status.RemainingRequests);
    }

    // 이번 호출에 없는 축은 직전 기록을 유지한다 — 빈 값으로 덮어써서 있던 정보를 지우지 않는다
    [Fact]
    public async Task RecordAsync_MissingAxis_KeepsPreviouslyRecordedValue()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        var gate = new RateLimitGate(limiter, clock);

        await gate.RecordAsync(
            ProviderConfigId,
            new RateLimitHeaders(
                RemainingRequests: 10, ResetAfter: TimeSpan.FromSeconds(30),
                RemainingTokens: 700, TokensResetAfter: TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        // 두 번째 호출은 요청 축만 새로 알려준다 — 토큰 축은 이번 응답에 없었다
        await gate.RecordAsync(
            ProviderConfigId,
            new RateLimitHeaders(RemainingRequests: 9, ResetAfter: TimeSpan.FromSeconds(29)),
            CancellationToken.None);

        var status = await limiter.GetStatusAsync(ProviderConfigId, CancellationToken.None);
        Assert.Equal(9, status!.RemainingRequests);
        // 토큰 축은 직전 값(700)이 그대로 남아 있다 — 사라지지 않았다
        Assert.Equal(700, status.RemainingTokens);
    }

    // 취소하면 대기 중이라도 즉시 빠져나온다 — 기존 지연 패턴(RedisTaskQueue 등)과 같은 규약
    [Fact]
    public async Task WaitIfNeeded_Cancellation_ThrowsInsteadOfWaitingFully()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        await limiter.UpdateAsync(
            ProviderConfigId,
            new RateLimitStatus(RemainingRequests: 0, ResetsAt: clock.Now + TimeSpan.FromMinutes(5)),
            CancellationToken.None);
        var gate = new RateLimitGate(limiter, clock);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => gate.WaitIfNeededAsync(ProviderConfigId, cts.Token));
    }

    // 어댑터가 헤더 값을 안 실어 보내면(정보 없음) 기록하지 않는다
    [Fact]
    public async Task RecordAsync_WithoutHeaderValues_DoesNotUpdate()
    {
        var limiter = new FakeRateLimiter();
        var gate = new RateLimitGate(limiter, FixedClock.At(2026, 7, 28, 12, 0));

        await gate.RecordAsync(ProviderConfigId, new RateLimitHeaders(), CancellationToken.None);

        Assert.Null(await limiter.GetStatusAsync(ProviderConfigId, CancellationToken.None));
    }

    // 헤더 값이 있으면 상대 시간(RateLimitResetAfter)을 절대 시각으로 바꿔 기록한다
    [Fact]
    public async Task RecordAsync_WithHeaderValues_StoresAbsoluteResetTime()
    {
        var clock = FixedClock.At(2026, 7, 28, 12, 0);
        var limiter = new FakeRateLimiter();
        var gate = new RateLimitGate(limiter, clock);

        await gate.RecordAsync(
            ProviderConfigId,
            new RateLimitHeaders(RemainingRequests: 2, ResetAfter: TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        var status = await limiter.GetStatusAsync(ProviderConfigId, CancellationToken.None);
        Assert.Equal(2, status!.RemainingRequests);
        Assert.Equal(clock.Now + TimeSpan.FromSeconds(30), status.ResetsAt);
    }

    private sealed class FakeRateLimiter : IRateLimiter
    {
        private readonly Dictionary<Guid, RateLimitStatus> _byProvider = new();

        public Task<RateLimitStatus?> GetStatusAsync(Guid providerConfigId, CancellationToken ct)
            => Task.FromResult(_byProvider.GetValueOrDefault(providerConfigId));

        public Task UpdateAsync(Guid providerConfigId, RateLimitStatus status, CancellationToken ct)
        {
            _byProvider[providerConfigId] = status;
            return Task.CompletedTask;
        }
    }
}
