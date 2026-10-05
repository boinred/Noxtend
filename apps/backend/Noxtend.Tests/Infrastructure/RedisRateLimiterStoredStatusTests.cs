using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.RateLimit;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// `RedisRateLimiter.StoredStatus` 직렬화 왕복 회귀 테스트.
///
/// **재발 방지 테스트다.** 커밋 f0f123c 에서 TPM 필드가 이 DTO 의 직렬화에서
/// 조용히 빠지는 버그를 고쳤는데, 그 버그를 잡아 줄 테스트가 하나도 없었다 —
/// `RateLimitGateTests`는 전부 `FakeRateLimiter`(순수 인메모리 딕셔너리)를 써서
/// 이 DTO 를 한 번도 지나지 않는다. Redis 자체는 안 띄우고 `StoredStatus`의
/// `From`/`ToStatus` 매핑만 순수 함수로 검증한다(`internal` + `InternalsVisibleTo`).
/// </summary>
public sealed class RedisRateLimiterStoredStatusTests
{
    [Fact]
    public void FullStatus_RoundTrips_WithoutLosingAnyField()
    {
        var status = new RateLimitStatus(
            RemainingRequests: 5,
            ResetsAt: new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero),
            RemainingTokens: 1500,
            TokensResetAt: new DateTimeOffset(2026, 8, 21, 12, 0, 30, TimeSpan.Zero),
            LastCallTokens: 3000);

        var roundTripped = RedisRateLimiter.StoredStatus.From(status).ToStatus();

        Assert.Equal(status, roundTripped);
    }

    // 토큰 축이 아예 안 온 상태(요청 축만 있음) — TPM 필드가 조용히 사라지던 버그의 재현
    [Fact]
    public void RequestsOnlyStatus_RoundTrips_WithTokenFieldsStayingNull()
    {
        var status = new RateLimitStatus(
            RemainingRequests: 5,
            ResetsAt: new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));

        var roundTripped = RedisRateLimiter.StoredStatus.From(status).ToStatus();

        Assert.Equal(status, roundTripped);
        Assert.Null(roundTripped.RemainingTokens);
        Assert.Null(roundTripped.TokensResetAt);
        Assert.Null(roundTripped.LastCallTokens);
    }

    // 완전히 빈 상태(모든 축 null)도 왕복이 깨지지 않는다 — 배포 롤오버 중 구버전
    // 키가 남아 있는 상황과 같은 모양이다
    [Fact]
    public void EmptyStatus_RoundTrips()
    {
        var status = new RateLimitStatus();

        var roundTripped = RedisRateLimiter.StoredStatus.From(status).ToStatus();

        Assert.Equal(status, roundTripped);
    }
}
