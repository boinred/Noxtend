namespace Noxtend.Domain.Job;

/// <summary>
/// 생성된 파츠 이미지.
///
/// Design Ref: §3.1 (사이클 #7)
///
/// **<see cref="Noxtend.Domain.Upload.StoredImage"/> 와 나눈 이유**: 그쪽은 사용자가 올린 것이라
/// <c>OriginalName</c> 을 갖는데 생성물에는 그런 것이 없고, 대신 어느 작업의 어느 파츠에서
/// 나왔는지가 필요하다. 한 표에 합치면 절반이 항상 비는 열이 생긴다.
///
/// 재생성하면 **새 행이 쌓인다** — 파츠가 최신 것을 가리킨다. 이전 것을 지우지 않는 것은
/// 골든 판정의 비교 재료이기 때문이다.
///
/// **바이트는 여기 없다** (NFR-10). <see cref="BlobKey"/> 만 있다.
/// </summary>
public sealed class GeneratedImage
{
    private GeneratedImage()
    {
        // EF Core 재구성용
    }

    private GeneratedImage(
        Guid id, Guid jobId, Guid partId, Guid taskId, ViewDirection viewDirection,
        string blobKey, string contentType, long sizeBytes, DateTimeOffset now, bool isSynthetic)
    {
        Id = id;
        JobId = jobId;
        PartId = partId;
        TaskId = taskId;
        ViewDirection = viewDirection;
        BlobKey = blobKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = now;
        IsSynthetic = isSynthetic;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public Guid PartId { get; private set; }

    /// <summary>이 이미지를 만든 공정. 내역·재시도 추적에 쓴다.</summary>
    public Guid TaskId { get; private set; }

    /// <summary>3D 재구성 입력에서 이 이미지가 나타내는 수평 방향.</summary>
    public ViewDirection ViewDirection { get; private set; }

    /// <summary>서버가 만든다. 원본 업로드와 같은 규칙 (§7 · C-1).</summary>
    public string BlobKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>서술 수정 복귀 무효화 여부.</summary>
    public bool IsObsoleted { get; private set; }

    /// <summary>
    /// 좌우/전후 대칭으로 다른 이미지를 반전해서 만든 파생 이미지인가 (spec 20260917).
    ///
    /// 실제로 그려진 게 아니라 반전으로 만들어진 것이라, "이 방향의 현재 이미지"로
    /// 자동 승격되면 안 된다 — <see cref="IsCurrentCandidate"/> 참고.
    /// </summary>
    public bool IsSynthetic { get; private set; }

    /// <summary>
    /// "이 방향의 현재 이미지" 후보인가 — 일반 갤러리와 자동 3D 팬인이 여기에 맞춰
    /// "최신"을 계산해야 한다(spec 20260917). 두 곳이 각자 조건을 손으로 적으면
    /// 어긋난다 — 실제로 한쪽만 <see cref="IsObsoleted"/> 를 걸러 어긋나 있었다.
    /// </summary>
    public bool IsCurrentCandidate => !IsObsoleted && !IsSynthetic;

    /// <summary>서술 수정 복귀 시 이전 생성 무효화.</summary>
    public void MarkObsoleted() => IsObsoleted = true;

    internal static GeneratedImage Create(
        Guid jobId, Guid partId, Guid taskId, ViewDirection viewDirection,
        string blobKey, string contentType, long sizeBytes, DateTimeOffset now,
        bool isSynthetic = false)
        => new(
            Guid.NewGuid(), jobId, partId, taskId, viewDirection,
            blobKey, contentType, sizeBytes, now, isSynthetic);
}
