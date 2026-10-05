using Noxtend.Domain.Mesh;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 3D 산출물 저장소.
///
/// Design Ref: §6.4 · Plan FR-09 · NFR-04·NFR-07
///
/// **일반 Blob 저장소와 나눈 이유는 키를 정하는 방식이다.** 그쪽은 GUID 로 새 키를
/// 만들지만, 여기서는 실행 ID 로 **결정된 키**를 쓴다. 내려받다 죽어서 다시 시도해도
/// 같은 자리에 덮어쓰므로 고아 Blob 이 쌓이지 않는다.
///
/// **검증도 여기 있다.** 저장과 검증이 따로 돌면 검증을 건너뛴 경로가 생기고,
/// 그때 들어간 깨진 GLB 는 사용자가 내려받아 열어 봐야 드러난다.
/// </summary>
public interface IMeshArtifactStorage
{
    /// <summary>
    /// 산출물 하나를 검증하고 저장한다. 형식이 아니면 던진다.
    ///
    /// **검증 규칙을 <paramref name="kind"/> 가 정한다** (§3.1) — GLB 는 <c>glTF</c> magic,
    /// FBX 는 <c>Kaydara FBX Binary</c> magic, 미리보기는 PNG·JPEG magic 이다.
    /// 종류마다 메서드를 두면 새 형식이 올 때마다 포트가 넓어진다 (D-08).
    /// </summary>
    Task<StoredMeshArtifact> SaveAsync(
        Guid meshRunId,
        MeshArtifactKind kind,
        Stream content,
        string? declaredContentType,
        long maxBytes,
        CancellationToken ct);

    Task<Stream> OpenAsync(string blobKey, CancellationToken ct);

    /// <summary>지운다. 없으면 조용히 넘어간다 — 이유는 <see cref="IBlobStorage"/> 와 같다.</summary>
    Task DeleteAsync(string blobKey, CancellationToken ct);
}

/// <summary>저장된 산출물 하나.</summary>
public sealed record StoredMeshArtifact(string BlobKey, string ContentType, long SizeBytes);

/// <summary>
/// 산출물이 형식에 맞지 않는다 — 확정 실패다.
///
/// 다시 내려받아도 같은 바이트라 같은 결과다. 자동으로 새 유료 작업을 만들지 않는다.
/// </summary>
public sealed class MeshArtifactRejectedException(string message) : Exception(message), IMeshFailure
{
    public string FailureCode => "MESH_RESULT_INVALID";

    public bool CanRetry => false;
}

/// <summary>
/// 3D 경로의 실패가 나르는 것.
///
/// Design Ref: §7.6
///
/// **이것이 Domain 에 있는 이유는 계층 때문이다.** 실패 코드를 만드는 것은 어댑터인데
/// 그것을 읽어 공정 결과를 정하는 것은 Application 이다. 예외 타입을 직접 참조하면
/// 의존이 뒤집히고, 리플렉션으로 꺼내면 이름이 바뀔 때 조용히 깨진다.
/// </summary>
public interface IMeshFailure
{
    /// <summary>사용자에게 보여도 안전한 코드 (§7.6).</summary>
    string FailureCode { get; }

    /// <summary>같은 요청을 다시 보내도 되는가.</summary>
    bool CanRetry { get; }
}
