using Noxtend.Domain.Scene;

namespace Noxtend.Domain.Ports;

/// <summary>
/// GLB 하나의 축별 크기를 읽는다.
///
/// Design Ref: background-placement-projection(#19) 후속 — 납작한 파츠 대응
///
/// **포트로 두는 이유는 계층이다.** 합성은 순수 계산이어야 하고(NFR-01), GLB 를 여는 것은
/// 어댑터의 일이다. 읽지 못하면 <c>null</c> — 형태를 모르면 서 있는 물체로 다룬다.
/// </summary>
public interface IMeshExtentsReader
{
    Task<MeshExtents?> ReadAsync(string glbBlobKey, CancellationToken ct);
}
