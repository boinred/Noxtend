using Microsoft.AspNetCore.Mvc;
using Noxtend.Api.Contracts;
using Noxtend.Infrastructure.Health;

namespace Noxtend.Api.Controllers;

/// <summary>
/// Design Ref: §11.2 #3 — the thinnest vertical slice, and module-1's only completion
/// condition. It carries no domain logic by design: it separates infrastructure risk
/// from feature risk (R-1).
/// </summary>
[ApiController]
[Route("health")]
public sealed class HealthController(IEnumerable<IDependencyProbe> probes) : ControllerBase
{
    /// <summary>
    /// Liveness — the process answers. Deliberately checks nothing external.
    ///
    /// Kubernetes probes target this instead of <see cref="GetAsync"/>: probing the
    /// dependency report would evict the pod from the Service when Redis is down and
    /// turn a readable 503 into a connection refused (§6 — the banner needs a response).
    /// </summary>
    [HttpGet("live")]
    public IActionResult GetLive() => Ok(ApiResponse<object>.Ok(new { status = "ok" }));

    /// <summary>
    /// Design Ref: §4.2 #1 — 200 with every dependency "ok", otherwise 503 naming the
    /// first one that is down.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        // Probes run concurrently: three sequential timeouts would make /health itself
        // look hung when the whole stack is down
        var results = await Task.WhenAll(probes.Select(async probe =>
            (probe.Name, Reachable: await probe.IsReachableAsync(ct))));

        var down = results.Where(r => !r.Reachable).Select(r => r.Name).ToArray();

        if (down.Length > 0)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<IReadOnlyDictionary<string, string>>.Fail(
                    "DEPENDENCY_UNAVAILABLE", string.Join(", ", down)));
        }

        // Flat map, not a wrapper object — the contract is data: { db, redis, blob } (§4.2 #1)
        IReadOnlyDictionary<string, string> dependencies =
            results.ToDictionary(r => r.Name, _ => "ok");

        return Ok(ApiResponse<IReadOnlyDictionary<string, string>>.Ok(dependencies));
    }
}
