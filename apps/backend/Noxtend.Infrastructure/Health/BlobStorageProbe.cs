using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Noxtend.Infrastructure.Health;

/// <summary>
/// Design Ref: §4.2 #1 — the "blob" entry of /health.
///
/// Lists a single container instead of merely constructing the client. The client
/// constructor only parses the connection string; it never touches the network,
/// so it would report "ok" against a stopped Azurite.
/// </summary>
public sealed class BlobStorageProbe(
    BlobServiceClient client,
    ILogger<BlobStorageProbe> logger) : IDependencyProbe
{
    public string Name => "blob";

    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in client.GetBlobContainersAsync(cancellationToken: ct))
            {
                break;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Health probe failed: blob");
            return false;
        }
    }
}
