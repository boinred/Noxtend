using Noxtend.Domain.Job;

namespace Noxtend.Domain.Mesh;

/// <summary>
/// 실행 하나가 쓰는 방향 하나의 입력.
///
/// Design Ref: §5.2
///
/// **방향마다 따로 두는 이유는 재기동이다.** 넷 중 둘째까지 올리고 죽으면, 재개한 워커가
/// 앞의 둘을 다시 올리지 않아야 한다. 행마다 token 을 남겨 두면 빈 방향만 채우면 된다.
///
/// <see cref="ProviderFileToken"/> 은 API 응답과 로그에 내보내지 않는다. 공급자 키 없이는
/// 조회에 쓸 수 없는 불투명한 참조이지만, 새어 나가서 좋을 것도 없다.
/// </summary>
public sealed class MeshRunInput
{
    private MeshRunInput()
    {
        // EF Core 재구성용
    }

    internal MeshRunInput(Guid meshRunId, ViewDirection viewDirection, Guid generatedImageId)
    {
        MeshRunId = meshRunId;
        ViewDirection = viewDirection;
        GeneratedImageId = generatedImageId;
    }

    public Guid MeshRunId { get; private set; }

    /// <summary>복합 키의 나머지 절반. 실행 하나에 방향은 넷뿐이다.</summary>
    public ViewDirection ViewDirection { get; private set; }

    /// <summary>이 방향에 실제로 쓴 이미지. 계획 시점에 얼린 값이다 (FR-04).</summary>
    public Guid GeneratedImageId { get; private set; }

    /// <summary>Blob 에 저장된 원본 MIME.</summary>
    public string? SourceContentType { get; private set; }

    /// <summary>공급자에게 실제로 보낸 MIME. WebP 는 PNG 로 바뀌어 나간다 (§6.5).</summary>
    public string? UploadContentType { get; private set; }

    /// <summary>
    /// 다시 쓸 수 있는 공급자 참조. **내구적인 핸들일 때만 값이 있다** (D-10).
    ///
    /// Tripo 는 업로드한 <c>file_token</c> 이라 채워지고, Meshy 는 요청 본문에 실리는
    /// base64 data URI 라 채워지지 않는다 — 열에 들어가지도 않고, 넣으면 DB 가 이미지
    /// 저장소가 되며, 본문을 남기지 않는다는 규칙(NFR-02)과도 어긋난다.
    /// </summary>
    public string? ProviderFileToken { get; private set; }

    /// <summary>
    /// 준비를 마친 시각.
    ///
    /// <c>UploadedAt</c> 에서 이름이 바뀌었다 — Meshy 는 아무것도 올리지 않는다.
    /// </summary>
    public DateTimeOffset? PreparedAt { get; private set; }

    /// <summary>
    /// 이 방향의 준비가 끝났는가 — **제출 관문이 보는 값이다.**
    ///
    /// 토큰 유무로 판정하면 Meshy 는 관문을 영원히 못 넘는다. 준비했다는 사실과
    /// 다시 쓸 수 있다는 사실은 다른 것이라 값을 나눈다 (D-11).
    /// </summary>
    public bool IsPrepared => PreparedAt is not null;

    /// <summary>
    /// 다시 준비해야 하는가 — **준비 루프가 보는 값이다.**
    ///
    /// 내구적 핸들이 남아 있으면 건너뛴다. 없으면 매번 다시 만든다 — Meshy 는 로컬
    /// 인코딩이라 네트워크도 크레딧도 들지 않는다.
    /// </summary>
    public bool NeedsPreparation => string.IsNullOrEmpty(ProviderFileToken);

    internal void RecordPrepared(string? providerFileToken, string uploadContentType, DateTimeOffset now)
    {
        // **첫 token 을 지킨다.** 덮어쓰면 재개 때 앞의 업로드가 헛일이 되고, 같은 파일이
        // 공급자 쪽에 여러 벌 남는다. 비내구적 핸들은 애초에 저장하지 않으므로 해당 없다
        if (!NeedsPreparation)
        {
            return;
        }

        ProviderFileToken = providerFileToken;
        UploadContentType = uploadContentType;
        PreparedAt = now;
    }
}
