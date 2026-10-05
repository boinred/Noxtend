using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Noxtend.Infrastructure.Health;

/// <summary>
/// Design Ref: §4.2 #1 — the "db" entry of /health.
///
/// Opens a connection and runs SELECT 1. Opening alone would be satisfied by the
/// pooler handing back a dead connection, so the round trip is the actual evidence.
/// </summary>
public sealed class SqlServerProbe : IDependencyProbe
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerProbe> _logger;

    public SqlServerProbe(string connectionString, ILogger<SqlServerProbe> logger)
    {
        // Probe the server, not the application database.
        //
        // The Noxtend database is created by migrations (module-3); until then a probe
        // against it reports "login failed", which reads as a credential problem when the
        // server is in fact reachable. /health answers "can the infrastructure see itself"
        // (§11.2 #3) — schema readiness is a different question with a different owner.
        _connectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;

        _logger = logger;
    }

    public string Name => "db";

    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(ct);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unreachable is a reportable state, not a fault — /health answers 503, not 500
            _logger.LogWarning(ex, "Health probe failed: db");
            return false;
        }
    }
}
