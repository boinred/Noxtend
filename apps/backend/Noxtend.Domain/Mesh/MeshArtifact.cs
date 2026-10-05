namespace Noxtend.Domain.Mesh;

/// <summary>
/// 3D 실행이 낳는 산출물의 종류.
///
/// Design Ref: §3.1 · D-08·D-09
///
/// **문자열로 저장한다.** 정수 순서에 기대면 열거형을 재배치하는 순간 지난 행들의 뜻이
/// 조용히 바뀐다 — <see cref="Provider.ProviderKind"/> 와 같은 이유다.
/// </summary>
public enum MeshArtifactKind
{
    /// <summary>필수 산출물. 이것이 없으면 실행이 완료될 수 없다.</summary>
    Glb,

    /// <summary>Meshy 만 낸다. Tripo 결과에는 없다 (Plan D-05).</summary>
    Fbx,

    /// <summary>공급자가 주는 렌더 이미지. 없을 수 있다.</summary>
    Preview,
}

/// <summary>
/// 자체 저장소에 들어간 산출물 하나.
///
/// Design Ref: §3.1~3.2 · D-08·D-09
///
/// **미리보기도 같은 목록에 있다.** 전에는 GLB 가 열 셋, 미리보기가 열 셋이었는데 FBX 가
/// 들어오면서 "있을 수도 없을 수도 있는 값" 이 둘이 됐다. 성질이 같은 것을 두 가지 모양으로
/// 두면 검증·저장·내려받기가 형식마다 나뉘고, 넷째 형식이 올 때 그 갈래가 또 는다.
///
/// **소유자가 둘이다** — <see cref="MeshRun"/> 은 내려받기 도중에 죽어도 이어서 하기 위한
/// checkpoint 로, <see cref="Job.GeneratedMesh"/> 는 사용자에게 보이는 최종 결과로 갖는다.
/// 키가 <c>(소유자, Kind)</c> 라 같은 종류가 두 번 들어갈 수 없다.
/// </summary>
public sealed class MeshArtifact
{
    private MeshArtifact()
    {
        // EF Core 재구성용
    }

    internal MeshArtifact(
        MeshArtifactKind kind, string blobKey, string contentType, long sizeBytes, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sizeBytes, 0);

        Kind = kind;
        BlobKey = blobKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = now;
    }

    public MeshArtifactKind Kind { get; private set; }

    /// <summary>우리 Blob 의 키. **공급자 URL 은 여기 오지 않는다** — 5분이면 만료된다.</summary>
    public string BlobKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// 아직 저장되지 않은 산출물의 서술 — 애그리게이트 경계를 넘길 때 쓴다.
///
/// 실행이 모아 둔 <see cref="MeshArtifact"/> 를 작업 쪽으로 옮길 때, 엔티티를 그대로
/// 넘기면 두 애그리게이트가 같은 인스턴스를 공유하게 된다.
/// </summary>
public sealed record MeshArtifactDescriptor(
    MeshArtifactKind Kind, string BlobKey, string ContentType, long SizeBytes);
