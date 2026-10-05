using Noxtend.Application.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Mesh;

/// <summary>
/// 올리기 전에 입력 한 장을 다듬는다.
///
/// Design Ref: §6.5 · §7.2 · Plan FR-05 · NFR-03
///
/// **검증이 업로드 앞에 있어야 한다.** 뒤에 두면 거절될 파일 네 장을 다 올린 뒤에야
/// 알게 되고, 그 왕복이 재시도마다 반복된다.
/// </summary>
public sealed class MeshInputNormalizer(IImageTranscoder transcoder, MeshGenerationOptions options)
{
    /// <summary>
    /// Tripo 문서의 최소 입력 해상도.
    ///
    /// 이보다 작으면 받아 주더라도 형편없는 mesh 가 나오고 credit 은 그대로 나간다.
    /// </summary>
    private const int MinimumSide = 256;

    public async Task<PreparedMeshInput> PrepareAsync(
        Stream source,
        string contentType,
        ViewDirection direction,
        CancellationToken ct)
    {
        // 스트림을 두 번 읽어야 한다 — 크기 재기와 인코딩. 네트워크 스트림은 되감기지
        // 않으므로 메모리로 한 번 옮긴다. 상한이 20 MB 라 감당할 수 있는 크기다
        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);

        if (buffer.Length > options.MaxInputImageBytes)
        {
            throw new MeshInputRejectedException(
                $"입력 이미지가 상한을 넘습니다: {buffer.Length} 바이트");
        }

        if (!transcoder.IsUploadable(contentType) && !IsConvertible(contentType))
        {
            throw new MeshInputRejectedException($"올릴 수 없는 형식입니다: {contentType}");
        }

        buffer.Position = 0;
        var (width, height) = await transcoder.MeasureAsync(buffer, ct);

        if (width < MinimumSide || height < MinimumSide)
        {
            throw new MeshInputRejectedException(
                $"입력 이미지가 너무 작습니다: {width}×{height}");
        }

        buffer.Position = 0;

        // JPEG/PNG 는 손대지 않는다 — 다시 인코딩하면 세대 손실만 쌓인다
        if (transcoder.IsUploadable(contentType))
        {
            return new PreparedMeshInput(
                buffer, contentType, FileNameFor(direction, contentType), buffer.Length);
        }

        var png = await transcoder.ToPngAsync(buffer, options.MaxInputImageBytes, ct);
        await buffer.DisposeAsync();

        return new PreparedMeshInput(png, "image/png", FileNameFor(direction, "image/png"), png.Length);
    }

    /// <summary>WebP 만 바꾼다. 나머지 형식은 생성 계약에 없다.</summary>
    private static bool IsConvertible(string contentType)
        => contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// **방향 이름만 쓴다** (§7.2 · §13.1). 파츠 이름이나 사용자 파일명을 넣으면
    /// 우리 쪽 어휘가 공급자 로그에 남는다.
    /// </summary>
    private static string FileNameFor(ViewDirection direction, string contentType)
    {
        var extension = contentType.ToLowerInvariant() switch
        {
            "image/png" => "png",
            _ => "jpg",
        };

        return $"{direction.ToString().ToLowerInvariant()}.{extension}";
    }
}

/// <summary>올릴 준비가 끝난 입력. 스트림은 호출자가 닫는다.</summary>
public sealed record PreparedMeshInput(
    Stream Content, string ContentType, string SafeFileName, long Length);

/// <summary>
/// 입력이 공급자 요건에 못 미친다 — 확정 실패다.
///
/// 재시도해도 같은 파일이라 같은 결과다. 다시 그리는 것이 사용자가 할 일이다.
/// </summary>
public sealed class MeshInputRejectedException(string message) : Exception(message), IMeshFailure
{
    public string FailureCode => "MESH_INPUT_REJECTED";

    public bool CanRetry => false;
}
