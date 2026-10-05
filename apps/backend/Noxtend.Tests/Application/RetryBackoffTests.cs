using Noxtend.Application.Pipeline;

namespace Noxtend.Tests.Application;

/// <summary>generation-rate-limiting §3② — 지수 백오프 계산(순수 함수)</summary>
public sealed class RetryBackoffTests
{
    // 1차 실패 후 5초, 2차 30초, 3차부터는 2분으로 고정(더 늘지 않음)
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 120)]
    public void BaseDelayFor_FollowsSpecSchedule(int attemptCount, int expectedSeconds)
    {
        var delay = RetryBackoff.BaseDelayFor(attemptCount);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    // 지터는 기준 지연의 ±25% 안에서만 움직여야 한다 — 너무 크면 오히려 예측 불가능해진다
    [Fact]
    public void ApplyJitter_StaysWithinQuarterOfBaseDelay()
    {
        var baseDelay = TimeSpan.FromSeconds(30);

        var withMaxPositiveJitter = RetryBackoff.ApplyJitter(baseDelay, jitterFraction: 0.25);
        var withMaxNegativeJitter = RetryBackoff.ApplyJitter(baseDelay, jitterFraction: -0.25);

        Assert.Equal(TimeSpan.FromSeconds(37.5), withMaxPositiveJitter);
        Assert.Equal(TimeSpan.FromSeconds(22.5), withMaxNegativeJitter);
    }

    // 지터가 0이면 기준 지연 그대로다 — 경계값
    [Fact]
    public void ApplyJitter_ZeroFraction_ReturnsBaseDelayUnchanged()
    {
        var baseDelay = TimeSpan.FromSeconds(5);

        var result = RetryBackoff.ApplyJitter(baseDelay, jitterFraction: 0);

        Assert.Equal(baseDelay, result);
    }

    // NextDelay는 실제 사용 지점(TaskExecution.Apply)이 호출할 진입점이다.
    // 난수를 쓰므로 정확한 값 대신 "기준 지연의 ±25% 범위 안에 떨어지는가"만 반복 확인한다
    [Fact]
    public void NextDelay_AlwaysFallsWithinJitterRange()
    {
        var baseDelay = RetryBackoff.BaseDelayFor(attemptCount: 1);
        var lowerBound = TimeSpan.FromSeconds(baseDelay.TotalSeconds * 0.75);
        var upperBound = TimeSpan.FromSeconds(baseDelay.TotalSeconds * 1.25);

        for (var i = 0; i < 50; i++)
        {
            var delay = RetryBackoff.NextDelay(attemptCount: 1);

            Assert.InRange(delay, lowerBound, upperBound);
        }
    }
}
