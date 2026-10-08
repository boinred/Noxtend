using Noxtend.Domain.Ports;
using Noxtend.Domain.Job;
using SkiaSharp;

namespace Noxtend.Infrastructure.Image;

/// <summary>
/// 결정적 Fake 이미지 공급자.
///
/// Design Ref: NFR-07 · §8.1 — **인프라 없이 도는 L1-B 를 유지하는 장치다.**
///
/// 실제 이미지 생성은 장당 수십 초에 돈이 들고 결과가 비결정적이라, 팬아웃·부분 성공·
/// 재시도를 그것으로 검증할 수 없다. 여기서 성공·빈 응답·형식 위반·크기 초과·취소를
/// 재현하므로 <c>RunGenerationTaskHandler</c> 전체가 호출 없이 검증된다.
///
/// L2/L3 에서도 쓴다 (<c>Llm__UseFake=true</c>) — 텍스트 Fake 와 같은 스위치다.
/// </summary>
public sealed class FakeImageProvider : IImageProvider
{
    private readonly Exception? _failure;
    private readonly Func<Exception?>? _next;
    private readonly TimeSpan _delay;
    private readonly byte[] _bytes;
    private readonly string _contentType;
    private readonly int _imageCount;
    private readonly bool _spriteSize;

    private FakeImageProvider(
        Exception? failure,
        Func<Exception?>? next,
        TimeSpan delay,
        byte[] bytes,
        string contentType,
        int imageCount,
        bool spriteSize = true)
    {
        _failure = failure;
        _next = next;
        _delay = delay;
        _bytes = bytes;
        _contentType = contentType;
        _imageCount = imageCount;
        _spriteSize = spriteSize;
    }

    /// <summary>PNG 시그니처만 갖춘 최소 바이트. 크기·형식 검사가 통과할 만큼만 진짜다.</summary>
    private static readonly byte[] MinimalPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    public static FakeImageProvider Succeeding()
        => new(null, null, TimeSpan.Zero, MinimalPng, "image/png", 1);

    public static FakeImageProvider Failing(Exception failure)
        => new(failure, null, TimeSpan.Zero, MinimalPng, "image/png", 1);

    /// <summary>
    /// 호출마다 던질지 말지를 정한다 — 재시도 검증용.
    /// <c>null</c> 을 돌려주면 그 호출은 성공한다.
    /// </summary>
    public static FakeImageProvider Throwing(Func<Exception?> next)
        => new(null, next, TimeSpan.Zero, MinimalPng, "image/png", 1);

    /// <summary>응답을 직접 정한다 — 빈 응답·형식 위반·크기 초과를 재현한다.</summary>
    public static FakeImageProvider Returning(byte[] bytes, string contentType = "image/png", int imageCount = 1)
        => new(null, null, TimeSpan.Zero, bytes, contentType, imageCount, spriteSize: false);

    /// <summary>느린 호출 — 리스 갱신과 취소 감시를 재현한다.</summary>
    public static FakeImageProvider Slow(TimeSpan delay)
        => new(null, null, delay, MinimalPng, "image/png", 1);

    public async Task<ImageResult> GenerateAsync(ImageRequest request, CancellationToken ct)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, ct);
        }

        // 호출별 판정이 고정 실패보다 우선한다 — "처음엔 실패하고 다음엔 성공" 을 재현한다
        var failure = _next is not null ? _next() : _failure;
        if (failure is not null)
        {
            throw failure;
        }

        ct.ThrowIfCancellationRequested();

        // 실제 공급자가 usage 를 주므로 Fake 도 준다 — 비용 집계가 Fake 모드에서도 검증된다
        var bytes = _bytes;
        if ((request.Context.Kind == TaskKind.GenerateSprite || request.Context.SourceGenerationId is not null) && _spriteSize)
        {
            var size = request.Size.Split('x');
            if (size.Length != 2 || !int.TryParse(size[0], out var width) || !int.TryParse(size[1], out var height)
                || width <= 0 || height <= 0 || (long)width * height > 16_777_216)
                throw new ProviderBadResponseException("지원하지 않는 생성 크기입니다");
            using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.Erase(SKColors.Coral);
            bitmap.SetPixel(0, 0, SKColors.Transparent);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            bytes = data.ToArray();
        }
        return new ImageResult(bytes, _contentType, _imageCount, 1_120, 1_120);
    }
}
