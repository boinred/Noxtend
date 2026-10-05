namespace Noxtend.Domain.Ports;

/// <summary>
/// 공급자 설정 단위 속도 제한 상태 저장소 (generation-rate-limiting §3①).
///
/// Design Ref: <c>docs/02-design/features/generation-rate-limiting.spec.md</c>
///
/// **이 Port 가 있는 이유는 <see cref="ITaskQueue"/> 와 같다 — 어휘 격리다.** Redis 로
/// 확정됐지만, Application 이 Redis 키·직렬화를 알면 저장소를 바꿀 때 유스케이스가 바뀐다.
///
/// 워커가 몇 대든 공급자 설정(<c>ProviderConfig</c>) 단위로 값이 공유돼야 하므로
/// 프로세스 메모리가 아니라 이 Port 뒤(Redis)에 둔다.
/// </summary>
public interface IRateLimiter
{
    /// <summary>가장 최근에 기록된 상태. 아무 호출도 기록된 적 없으면 <c>null</c>.</summary>
    Task<RateLimitStatus?> GetStatusAsync(Guid providerConfigId, CancellationToken ct);

    /// <summary>공급자 응답이 알려준 최신 값으로 덮어쓴다.</summary>
    Task UpdateAsync(
        Guid providerConfigId, RateLimitStatus status, CancellationToken ct);
}

/// <summary>
/// 공급자가 알려준 속도 제한 상태.
///
/// **두 축(RPM·TPM) 이 전부 nullable 이다 — 독립 리뷰 지적으로 대칭을 맞췄다.**
/// 처음엔 <paramref name="RemainingRequests"/>·<paramref name="ResetsAt"/> 만 필수였는데,
/// 그러면 헤더 응답이 요청 축만 파싱 실패해도(예: reset 형식이 예상과 다름) 같이
/// 온 토큰 축 값까지 통째로 버려야 했다(둘 다 있어야 기록하는 계약이었으므로).
/// 이제 <c>RateLimitGate.RecordAsync</c> 가 각 축을 독립으로 채우고, 없는
/// 값은 직전 기록을 유지한다.
///
/// <paramref name="ResetsAt"/>·<paramref name="TokensResetAt"/> 는 **절대 시각**이다 —
/// 어댑터가 돌려주는 상대 시간을 기록 시점의 <c>IClock.Now</c> 와 더해 여기서 절대
/// 시각으로 바꾼다. 절대 시각으로 저장해야 나중에 조회하는 시점의 경과 시간과
/// 무관하게 정확하다.
///
/// <paramref name="RemainingTokens"/>·<paramref name="TokensResetAt"/> 는 별개 축(TPM)이다
/// — OpenAI 는 RPM 과 TPM 을 독립으로 걸고 둘 중 먼저 닿는 쪽에서 429 를 낸다(공식 문서).
/// 요청 수는 남아도 토큰이 바닥나면 막아야 하므로 게이트가 둘 다 본다. 정보가 없으면
/// (구버전 어댑터 등) null 로 두고 그 축은 검사하지 않는다.
///
/// <paramref name="LastCallTokens"/> — 직전 호출이 실제로 쓴 토큰 수(입력+출력).
/// TPM 은 요청 하나에 수천씩 줄어드는데 "남은 토큰 ≤ 0" 만 보면 "1,500 남았는데
/// 다음 호출이 3,000 필요"한 상황을 못 잡는다(독립 리뷰 지적). 직전 호출 크기를
/// 다음 비슷한 호출의 최소 여유분으로 써서 "남은 토큰이 직전 호출보다 적으면
/// 미리 기다린다"는 휴리스틱에 쓴다.
/// </summary>
public sealed record RateLimitStatus(
    int? RemainingRequests = null,
    DateTimeOffset? ResetsAt = null,
    int? RemainingTokens = null,
    DateTimeOffset? TokensResetAt = null,
    int? LastCallTokens = null);
