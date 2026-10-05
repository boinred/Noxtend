using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Mesh;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 네이티브 이미지 디코더가 실제로 올라오는가.
///
/// Design Ref: §6.5
///
/// **이 검사가 있는 이유는 배포에서만 터지는 결함이 있었기 때문이다.** 관리형 어셈블리
/// (`SkiaSharp.dll`)와 네이티브 라이브러리(`libSkiaSharp.so`)가 둘 다 이미지 안에
/// 있었는데도 3D 공정 10건이 전부 `TypeInitializationException` 으로 죽었다 — 네이티브
/// 쪽 의존인 `libfontconfig.so.1` 이 런타임 이미지에 없었다.
///
/// 개발 기기(macOS)에서는 fontconfig 이 늘 있어 드러나지 않는다. 그래서 이 테스트는
/// **CI 와 컨테이너에서 돌 때 값어치가 있다** — 거기서 깨지면 배포 전에 안다.
///
/// 형식 변환이나 크기 판정을 보는 것이 아니다. **정적 초기화가 성공하는가** 하나다.
/// </summary>
public sealed class SkiaLoadsTests
{
    [Fact]
    public async Task NativeDecoderInitializes()
    {
        IImageTranscoder transcoder = new SkiaImageTranscoder();

        using var image = TestImages.Jpeg(512, 512);

        // 여기서 네이티브가 처음 로드된다. 의존이 빠져 있으면 TypeInitializationException 이다
        var (width, height) = await transcoder.MeasureAsync(image, default);

        Assert.Equal(512, width);
        Assert.Equal(512, height);
    }

    /// <summary>인코딩 경로도 네이티브를 쓴다 — 읽기만 되는 반쪽 설치를 잡는다.</summary>
    [Fact]
    public async Task NativeEncoderInitializes()
    {
        IImageTranscoder transcoder = new SkiaImageTranscoder();

        using var source = TestImages.Png(300, 300);
        await using var png = await transcoder.ToPngAsync(source, 20 * 1024 * 1024, default);

        Assert.True(png.Length > 0);
    }
}
