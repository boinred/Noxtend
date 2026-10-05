using Noxtend.Domain.Ports;
using Noxtend.Domain.Scene;

namespace Noxtend.Infrastructure.Mesh;

/// <summary>
/// 저장된 GLB 를 열어 월드 바운딩 박스를 잰다.
///
/// Design Ref: background-placement-projection(#19) 후속
///
/// **JSON 청크만 읽는다.** accessor 가 담은 축별 최소·최대를 노드 변환으로 옮겨 합칠 뿐
/// 삼각형 데이터는 건드리지 않는다. 열지 못하거나 형식이 어긋나면 <c>null</c> 이고,
/// 그러면 합성은 사이클 #19 까지의 동작(서 있는 물체) 그대로 간다.
/// </summary>
public sealed class MeshExtentsReader(IMeshArtifactStorage storage) : IMeshExtentsReader
{
    /// <summary>바운딩 박스를 읽는 데 필요한 앞부분만 — 큰 GLB 의 BIN 청크까지 받지 않는다.</summary>
    private const int MaxHeaderBytes = 4 * 1024 * 1024;

    public async Task<MeshExtents?> ReadAsync(string glbBlobKey, CancellationToken ct)
    {
        try
        {
            await using var content = await storage.OpenAsync(glbBlobKey, ct);
            using var buffer = new MemoryStream();

            var chunk = new byte[64 * 1024];
            int read;
            while (buffer.Length < MaxHeaderBytes
                && (read = await content.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
            }

            return GlbBoundsReader.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // 형태를 모르는 것은 실패가 아니다 — 장면은 그려져야 한다
            return null;
        }
    }
}
