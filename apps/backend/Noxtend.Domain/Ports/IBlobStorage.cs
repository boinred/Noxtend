namespace Noxtend.Domain.Ports;

/// <summary>
/// 이미지 원본 저장소.
///
/// Design Ref: §3.2 — <see cref="SaveAsync"/> 가 키를 **반환**한다. 호출자가 키를
/// 정하는 형태였다면 사용자 입력이 경로가 되는 길이 열린다 (§7 경로 조작 차단).
/// </summary>
public interface IBlobStorage
{
    /// <returns>서버가 생성한 저장소 키.</returns>
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct);

    Task<Stream> OpenReadAsync(string blobKey, CancellationToken ct);

    /// <summary>
    /// 지운다. **없으면 조용히 넘어간다.**
    ///
    /// 삭제는 DB 트랜잭션 밖에서 최선으로 도는 일이라, 같은 키를 두 번 지우려는
    /// 상황이 정상이다 — 없는 것을 오류로 다루면 재시도가 첫 실패에서 멈춘다.
    /// </summary>
    Task DeleteAsync(string blobKey, CancellationToken ct);
}
