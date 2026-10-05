using Microsoft.Extensions.Logging;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Health;

/// <summary>
/// `/health` 의 `imageDecoder` 항목.
///
/// Design Ref: §6.5 · §4.2 #1
///
/// **이 probe 는 실제로 난 사고에서 나왔다.** 관리형 어셈블리와 네이티브 라이브러리가
/// 둘 다 이미지 안에 있었는데도 3D 공정 10건이 전부 `TypeInitializationException` 으로
/// 죽었다 — 네이티브 쪽 의존인 `libfontconfig.so.1` 이 런타임 이미지에 없었다.
///
/// **증상이 나온 자리가 원인에서 멀었다.** 배포는 성공하고 `/health` 는 초록이며 앞
/// 네 단계도 정상이다. 파츠 이미지를 다 만들고 3D 를 올리려는 순간에야 터지므로, 그때는
/// 이미 가장 비싼 단계를 치른 뒤다.
///
/// 다른 probe 들과 달리 네트워크를 타지 않는다. 확인하는 것은 **네이티브가 로드되는가**
/// 하나이고, 그것이 기동 직후에 드러나야 할 값이다.
/// </summary>
public sealed class ImageDecoderProbe(
    IImageTranscoder transcoder,
    ILogger<ImageDecoderProbe> logger) : IDependencyProbe
{
    public string Name => "imageDecoder";

    /// <summary>
    /// 1×1 PNG 한 장의 크기를 잰다.
    ///
    /// 형식이나 크기를 보는 것이 아니라 **정적 초기화가 성공하는지**만 본다. 디코딩
    /// 자체는 마이크로초 단위라 health 응답을 늦추지 않는다.
    /// </summary>
    public async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            using var probe = new MemoryStream(Png);
            var (width, height) = await transcoder.MeasureAsync(probe, ct);

            return width == 1 && height == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Health probe failed: imageDecoder");
            return false;
        }
    }

    /// <summary>1×1 PNG. 헤더만 읽으므로 이보다 클 이유가 없다.</summary>
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
}
