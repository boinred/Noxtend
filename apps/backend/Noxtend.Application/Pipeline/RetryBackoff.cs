namespace Noxtend.Application.Pipeline;

/// <summary>
/// 재시도 지연 계산 — 순수 함수 (generation-rate-limiting §3②).
///
/// 즉시 재적재가 레이트리밋을 다시 때리는 것을 막는다(§1). 여러 공정이 같은 순간
/// 레이트리밋에 걸렸을 때 다 같이 같은 시각에 재시도하지 않도록 지터를 섞는다.
/// </summary>
public static class RetryBackoff
{
    // 1차 5초, 2차 30초, 3차부터는 2분에서 고정 — 그 이상 늘려도 사용자 체감상 이득이 적다
    private static readonly TimeSpan[] BaseDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    ];

    public static TimeSpan BaseDelayFor(int attemptCount)
    {
        var index = Math.Clamp(attemptCount - 1, 0, BaseDelays.Length - 1);
        return BaseDelays[index];
    }

    /// <param name="jitterFraction">-0.25~0.25 사이. 그 밖의 값은 호출자 책임이다.</param>
    public static TimeSpan ApplyJitter(TimeSpan baseDelay, double jitterFraction) =>
        baseDelay + TimeSpan.FromTicks((long)(baseDelay.Ticks * jitterFraction));

    /// <summary>실제 호출 지점(<c>TaskExecution.Apply</c>)이 쓰는 진입점.</summary>
    public static TimeSpan NextDelay(int attemptCount)
    {
        var baseDelay = BaseDelayFor(attemptCount);

        // ±25% — 동시에 실패한 여러 공정이 똑같은 시각에 다시 몰리지 않게 흩뿌린다
        var jitterFraction = (Random.Shared.NextDouble() * 0.5) - 0.25;

        return ApplyJitter(baseDelay, jitterFraction);
    }
}
