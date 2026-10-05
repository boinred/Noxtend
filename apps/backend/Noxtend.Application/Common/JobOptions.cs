namespace Noxtend.Application.Common;

/// <summary>
/// Design Ref: §10.3 — 리스·스위프·재시도 값. 운영 노브이므로 재빌드 없이 바뀌어야 한다.
/// </summary>
public sealed class JobOptions
{
    // double 인 이유는 설정 편의가 아니라 테스트다. 리스 갱신 주기가 초 단위 정수면
    // "갱신 시점에 취소를 알아채는가" (§8.2 #14b) 를 검증하는 데 실제로 몇 초를 기다려야 한다
    public double LeaseSeconds { get; init; } = 120;

    /// <summary>**취소 지연의 상한**이기도 하다. 워커는 갱신 시점에 작업 상태를 확인한다 (§2.2).</summary>
    public double LeaseRenewSeconds { get; init; } = 15;

    public double SweepIntervalSeconds { get; init; } = 60;

    public int MaxAttempts { get; init; } = 3;

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);

    public TimeSpan LeaseRenew => TimeSpan.FromSeconds(LeaseRenewSeconds);
}
