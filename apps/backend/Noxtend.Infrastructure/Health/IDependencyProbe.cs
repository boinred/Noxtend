namespace Noxtend.Infrastructure.Health;

/// <summary>
/// One reachability check for one external dependency.
///
/// Design Ref: §11.2 #3 — the thinnest vertical slice. It carries no domain logic,
/// which is the point: it proves the infrastructure sees itself before any business
/// rule exists to be blamed for a failure (R-1).
///
/// This abstraction lives in Infrastructure rather than Domain on purpose. Reachability
/// of mssql/Redis/Azurite is an operational concern, not a domain concept, and the
/// Port list in §3.2 is closed at seven.
/// </summary>
public interface IDependencyProbe
{
    /// <summary>Key in the /health response body — "db", "redis", "blob".</summary>
    string Name { get; }

    Task<bool> IsReachableAsync(CancellationToken ct);
}
