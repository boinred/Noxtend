using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure;

/// <summary>Design Ref: §9.3 — the only place the process reads the wall clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
