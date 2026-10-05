using Noxtend.Domain.Ports;

namespace Noxtend.Application.Generation;

/// <summary>
/// 어댑터가 한 번의 응답에서 실어 보내는 레이트리밋 헤더 값 전부.
///
/// 하나의 레코드로 묶는다(독립 리뷰 지적) — 예전엔 개별 원시값을 위치 인자로
/// 받아서, <c>CancellationToken</c> 뒤에 온 선택 인자(토큰 축)가 있는 축소 호출
/// (예전 4인자 호출)이 컴파일 에러 없이 통과하며 토큰 축을 조용히 빼먹기 쉬웠다.
/// </summary>
public sealed record RateLimitHeaders(
    int? RemainingRequests = null,
    TimeSpan? ResetAfter = null,
    int? RemainingTokens = null,
    TimeSpan? TokensResetAfter = null,
    int? CallTokens = null)
{
    /// <summary>
    /// 직전 호출이 실제로 쓴 토큰 수(입력+출력)를 더한다. 공급자가 usage 자체를
    /// 안 준 응답(스트리밍 등)이면 둘 다 null 이라 결과도 null — 0 으로 채우면
    /// "이번 호출은 공짜였다"로 잘못 읽힌다.
    /// </summary>
    public static int? SumTokens(int? inputTokens, int? outputTokens)
        => inputTokens is null && outputTokens is null ? null : (inputTokens ?? 0) + (outputTokens ?? 0);
}

/// <summary>
/// 이미지 생성 호출 전 확인·대기 (generation-rate-limiting §3①).
///
/// **어댑터는 저장소를 모른다(G-2)를 지키는 자리.** 어댑터(`OpenAiImageProvider` 등)는
/// 응답 헤더 값을 <see cref="ImageResult"/>에 실어 보내기만 한다 — 그 값을 Redis에
/// 저장하고, 다음 호출 전에 판단하는 건 여기(Application)가 한다.
///
/// `RedisTaskQueue`·`RedisConnectionProvider`가 이미 쓰는 "문제가 생기면 죽이지 말고
/// 취소 가능한 지연으로 기다린다" 패턴을 그대로 따른다.
/// </summary>
public sealed class RateLimitGate(IRateLimiter limiter, IClock clock)
{
    /// <summary>
    /// RPM(요청 수)·TPM(토큰 수) 두 축을 각각 본다 — OpenAI 는 둘을 독립으로 걸고
    /// 먼저 닿는 쪽에서 429 를 낸다(공식 문서: "Rate limits can be hit across any of
    /// the options depending on what occurs first"). 한쪽만 보면 다른 쪽 소진을
    /// 못 막는다. 둘 다 바닥났으면 **더 늦게 풀리는 쪽까지** 기다린다 — 먼저 풀리는
    /// 쪽만 보고 나가면 나머지 축에서 곧바로 다시 429 가 난다.
    ///
    /// **토큰 축의 "바닥"은 정확히 0 이 아니라 직전 호출이 쓴 토큰 수다**(독립 리뷰
    /// 지적). RPM 은 호출마다 정확히 1 씩 줄어 0 에 딱 닿지만, TPM 은 호출 하나가
    /// 수천씩 소모한다 — "1,500 남았는데 다음 비전 호출이 3,000 필요"한 상황에서
    /// 정확히 0 인지만 보면 통과시키고 그대로 429 가 난다. 직전 호출 크기
    /// (<see cref="RateLimitStatus.LastCallTokens"/>) 를 다음 비슷한 호출의 최소
    /// 여유분으로 삼는다.
    /// </summary>
    /// <returns>실제로 기다렸으면 <c>true</c>.</returns>
    public async Task<bool> WaitIfNeededAsync(Guid providerConfigId, CancellationToken ct)
    {
        var status = await limiter.GetStatusAsync(providerConfigId, ct);
        if (status is null)
        {
            return false;
        }

        var now = clock.Now;
        DateTimeOffset? waitUntil = null;

        if (status is { RemainingRequests: <= 0, ResetsAt: { } resetsAt } && resetsAt > now)
        {
            waitUntil = resetsAt;
        }

        var tokenFloor = status.LastCallTokens ?? 0;
        if (status is { RemainingTokens: { } remainingTokens, TokensResetAt: { } tokensResetAt }
            && remainingTokens <= tokenFloor && tokensResetAt > now)
        {
            waitUntil = waitUntil is { } current && current > tokensResetAt ? current : tokensResetAt;
        }

        if (waitUntil is not { } until)
        {
            return false;
        }

        await Task.Delay(until - now, ct);
        return true;
    }

    /// <summary>
    /// 호출이 끝난 뒤 어댑터가 실어 보낸 헤더 값을 기록한다. 아무 축도 없으면
    /// 아무것도 안 한다.
    ///
    /// **각 축을 독립으로 채운다** — 요청 축 파싱이 실패해도(예: reset 형식이
    /// 예상과 다름) 같이 온 토큰 축은 그대로 기록된다. 이번 호출에 없는 값은
    /// 직전 기록(<c>limiter.GetStatusAsync</c>)을 그대로 들고 간다 — "빈 값으로
    /// 덮어써서 있던 정보를 지운다"를 막는다.
    /// </summary>
    public async Task RecordAsync(Guid providerConfigId, RateLimitHeaders headers, CancellationToken ct)
    {
        if (headers is { RemainingRequests: null, RemainingTokens: null })
        {
            return;
        }

        var current = await limiter.GetStatusAsync(providerConfigId, ct);
        var now = clock.Now;

        var remainingRequests = headers.RemainingRequests ?? current?.RemainingRequests;
        var resetsAt = headers.RemainingRequests is not null && headers.ResetAfter is { } resetAfter
            ? now + resetAfter
            : current?.ResetsAt;

        var remainingTokens = headers.RemainingTokens ?? current?.RemainingTokens;
        var tokensResetAt = headers.RemainingTokens is not null && headers.TokensResetAfter is { } tokensReset
            ? now + tokensReset
            : current?.TokensResetAt;

        var lastCallTokens = headers.CallTokens ?? current?.LastCallTokens;

        var status = new RateLimitStatus(remainingRequests, resetsAt, remainingTokens, tokensResetAt, lastCallTokens);
        await limiter.UpdateAsync(providerConfigId, status, ct);
    }
}
