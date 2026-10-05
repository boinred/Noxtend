using Noxtend.Domain.Ports;

namespace Noxtend.Tests;

/// <summary>
/// 고정 시계.
///
/// Design Ref: §8.2 #25 — 리스 만료·타임스탬프가 전부 시각에 걸려 있다.
/// 실제 시계로는 "리스가 만료된 공정" 을 만들려고 2분을 기다려야 한다.
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; private set; } = now;

    public static FixedClock At(int year, int month, int day, int hour = 0, int minute = 0)
        => new(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero));

    public static FixedClock Default => At(2026, 7, 28, 12, 0);

    /// <summary>시간을 앞으로 민다. 리스 만료를 만드는 유일한 수단이다.</summary>
    public FixedClock Advance(TimeSpan by)
    {
        Now += by;
        return this;
    }
}
