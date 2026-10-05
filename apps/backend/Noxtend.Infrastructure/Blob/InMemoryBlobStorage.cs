using System.Collections.Concurrent;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Blob;

/// <summary>
/// 인프라 없는 <see cref="IBlobStorage"/> 구현.
///
/// Design Ref: §7 경로 조작 차단 — 실제 어댑터와 마찬가지로 **키를 서버가 만든다.**
/// Fake 가 호출자에게 키를 받는 형태였다면 실제 어댑터의 계약과 어긋나고,
/// 테스트가 통과해도 배포에서 다르게 동작한다.
/// </summary>
public sealed class InMemoryBlobStorage : IBlobStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new();

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        var blobKey = $"images/{Guid.NewGuid():n}";
        _blobs[blobKey] = buffer.ToArray();

        return blobKey;
    }

    public Task<Stream> OpenReadAsync(string blobKey, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(blobKey, out var bytes))
        {
            throw new FileNotFoundException($"Blob not found: {blobKey}");
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    /// <summary>남아 있는 파일 수 — 삭제가 실제로 치웠는지 보는 창구다.</summary>
    public int Count => _blobs.Count;

    /// <summary>저장소 장애를 흉내낸다. 삭제 실패가 작업 삭제를 되돌리면 안 된다.</summary>
    public bool FailDeletes { get; set; }

    public Task DeleteAsync(string blobKey, CancellationToken ct)
    {
        if (FailDeletes)
        {
            throw new InvalidOperationException($"저장소 삭제 실패: {blobKey}");
        }

        _blobs.TryRemove(blobKey, out _);
        return Task.CompletedTask;
    }
}
