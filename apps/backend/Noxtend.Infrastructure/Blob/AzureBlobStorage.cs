using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Blob;

/// <summary>
/// Design Ref: §3.2 · §7
///
/// **키를 서버가 만든다.** 원본 파일명을 경로로 쓰면 사용자가 보낸 문자열이 저장소
/// 경로가 되어 경로 조작이 열린다. GUID 는 추측도 충돌도 되지 않는다.
///
/// 로컬은 Azurite, 배포는 Azure Blob 이고 어댑터는 같다 — 연결 문자열만 바뀐다.
/// </summary>
public sealed class AzureBlobStorage(BlobServiceClient client, string containerName) : IBlobStorage
{
    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
    {
        var container = client.GetBlobContainerClient(containerName);

        // 컨테이너를 여기서 만든다. 별도 프로비저닝 단계를 두면 매니페스트 적용 한 번으로
        // 로컬 전체가 뜬다는 조건이 깨진다 (Plan §4.1)
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobKey = $"{DateTimeOffset.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():n}";
        var blob = container.GetBlobClient(blobKey);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

        return blobKey;
    }

    public async Task<Stream> OpenReadAsync(string blobKey, CancellationToken ct)
    {
        var blob = client.GetBlobContainerClient(containerName).GetBlobClient(blobKey);
        return await blob.OpenReadAsync(cancellationToken: ct);
    }

    /// <summary>없으면 조용히 넘어간다 — 최선 삭제라 두 번 부르는 것이 정상이다.</summary>
    public async Task DeleteAsync(string blobKey, CancellationToken ct)
        => await client.GetBlobContainerClient(containerName)
            .GetBlobClient(blobKey)
            .DeleteIfExistsAsync(cancellationToken: ct);
}
