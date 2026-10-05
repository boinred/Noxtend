using System.Text.Json;
using Microsoft.Extensions.Logging;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Queue;

namespace Noxtend.Infrastructure.RateLimit;

/// <summary>
/// Redis 기반 <see cref="IRateLimiter"/> (generation-rate-limiting §3①).
///
/// **레이스 컨디션을 원자적으로 막지 않는다 — 의도적이다.** 두 호출이 동시에 "남았다"고
/// 읽고 같이 나가도, 최악의 경우 공급자가 실제로 429를 내려주고 그건 이미 만든 반응형
/// 백오프(`TaskExecution.Apply`)가 처리한다. 이 클래스는 그 이전에 대부분을 걸러내는
/// 소프트 휴리스틱이지, 정확한 카운터가 아니다 — Lua 스크립트 같은 원자성 장치를
/// 추가하는 비용이 지금 규모에서 이득보다 크다.
///
/// **Redis 장애는 지연이지 유실이 아니다** — `RedisTaskQueue.cs` 와 같은 이유로, 연결
/// 실패 시 예외를 던지는 대신 "정보 없음"(null)으로 저하시킨다. 대기를 못 걸어도
/// 반응형 백오프가 뒤를 받친다.
/// </summary>
public sealed class RedisRateLimiter(
    RedisConnectionProvider connections,
    ILogger<RedisRateLimiter> logger) : IRateLimiter
{
    // 창이 아무리 길어도 이 시간이 지나면 지워진다 — 공정이 취소돼 UpdateAsync 가 다시
    // 안 불려도 키가 영원히 안 남게 하는 안전판
    private static readonly TimeSpan MaxTtl = TimeSpan.FromMinutes(10);

    public async Task<RateLimitStatus?> GetStatusAsync(Guid providerConfigId, CancellationToken ct)
    {
        try
        {
            var db = (await connections.GetAsync()).GetDatabase();
            var raw = await db.StringGetAsync(KeyFor(providerConfigId));

            return raw.HasValue
                ? JsonSerializer.Deserialize<StoredStatus>((string)raw!)!.ToStatus()
                : null;
        }
        catch (Exception ex)
        {
            // 정보 없음으로 저하 — RateLimitGate 는 null 을 "대기 안 함"으로 다룬다
            logger.LogWarning(ex, "레이트리밋 상태 조회 실패 (공급자 {ProviderConfigId})", providerConfigId);
            return null;
        }
    }

    public async Task UpdateAsync(Guid providerConfigId, RateLimitStatus status, CancellationToken ct)
    {
        try
        {
            var db = (await connections.GetAsync()).GetDatabase();
            var json = JsonSerializer.Serialize(StoredStatus.From(status));
            await db.StringSetAsync(KeyFor(providerConfigId), json, MaxTtl);
        }
        catch (Exception ex)
        {
            // 기록 실패도 지연으로 저하 — 다음 호출이 "정보 없음"으로 통과한다
            logger.LogWarning(ex, "레이트리밋 상태 기록 실패 (공급자 {ProviderConfigId})", providerConfigId);
        }
    }

    private static string KeyFor(Guid providerConfigId) => $"noxtend:ratelimit:{providerConfigId}";

    // 두 축(RPM·TPM) 전부 nullable — 시각은 절대값(unix ms)으로 직렬화한다. 없으면
    // null 로 왕복해야 RateLimitGate 가 "정보 없음"으로 읽는다. `internal` — 이전에
    // `private` 였을 때 이 매핑을 통과하는 회귀 테스트가 하나도 없어(게이트 테스트는
    // 전부 FakeRateLimiter 라 이 DTO 를 안 지난다) TPM 필드가 조용히 직렬화에서
    // 빠지는 버그가 한 번 났다(커밋 f0f123c). `InternalsVisibleTo("Noxtend.Tests")`로
    // 왕복 테스트를 붙인다(`RedisRateLimiterStoredStatusTests`)
    internal sealed record StoredStatus(
        int? RemainingRequests,
        long? ResetsAtUnixMs,
        int? RemainingTokens,
        long? TokensResetAtUnixMs,
        int? LastCallTokens)
    {
        public static StoredStatus From(RateLimitStatus status)
            => new(
                status.RemainingRequests,
                status.ResetsAt?.ToUnixTimeMilliseconds(),
                status.RemainingTokens,
                status.TokensResetAt?.ToUnixTimeMilliseconds(),
                status.LastCallTokens);

        public RateLimitStatus ToStatus()
            => new(
                RemainingRequests,
                ResetsAtUnixMs is { } r ? DateTimeOffset.FromUnixTimeMilliseconds(r) : null,
                RemainingTokens,
                TokensResetAtUnixMs is { } t ? DateTimeOffset.FromUnixTimeMilliseconds(t) : null,
                LastCallTokens);
    }
}
