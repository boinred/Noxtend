using Microsoft.AspNetCore.Mvc;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Controllers;

/// <summary>
/// 생성된 파츠 이미지.
///
/// Design Ref: §4.2 #4 · §7
///
/// **GUID 로만 조회한다.** 추측도 순회도 되지 않고, 사용자 입력이 경로가 되는 길이
/// 없다 (NFR-08) — 키는 서버가 만들고 여기서는 id 만 받는다.
/// </summary>
[ApiController]
[Route("api/generated-images")]
public sealed class GeneratedImagesController(
    IJobRepository jobs,
    IBlobStorage blobs) : ControllerBase
{
    /// <summary>
    /// 봉투를 쓰지 않는다 — 바이너리를 JSON 에 담을 수 없고, 화면이 `&lt;img src&gt;` 로
    /// 직접 건다. 업로드 이미지와 같은 규약이다.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var image = await jobs.GetGeneratedImageAsync(id, ct);
        if (image is null)
        {
            return NotFound();
        }

        var stream = await blobs.OpenReadAsync(image.BlobKey, ct);

        // 생성물은 바뀌지 않는다 — 재생성하면 새 id 가 나오므로 이 id 의 내용은 불변이다.
        // private 인 이유는 인증이 붙었을 때 공유 캐시에 남아 있으면 안 되기 때문이다
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";

        return File(stream, image.ContentType);
    }
}
