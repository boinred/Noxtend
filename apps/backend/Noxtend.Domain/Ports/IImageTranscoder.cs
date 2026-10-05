namespace Noxtend.Domain.Ports;

/// <summary>
/// 업로드 전에 이미지를 공급자가 받는 형식으로 맞춘다.
///
/// Design Ref: §6.5 · §7.2 · Plan FR-05 · NFR-03
///
/// **이것이 Port 인 이유는 디코딩 라이브러리 때문이다.** 픽셀을 읽고 다시 인코딩하려면
/// 네이티브 의존이 필요한데, 그것이 Application 에 들어오면 유스케이스 테스트가 그
/// 라이브러리를 요구하게 된다.
///
/// **실제 경로에서는 거의 아무 일도 하지 않는다** (§1.4). 지금 생성되는 이미지는 전부
/// JPEG 이고 Tripo 는 JPEG 을 그대로 받는다. 그래도 두는 것은 생성 계약이 WebP 를
/// 허용하기 때문이다 — 언젠가 WebP 가 들어오면 이 자리 없이는 업로드가 거절된다.
/// </summary>
public interface IImageTranscoder
{
    /// <summary>이 형식을 그대로 올려도 되는가. 아니면 <see cref="ToPngAsync"/> 를 거친다.</summary>
    bool IsUploadable(string contentType);

    /// <summary>
    /// 픽셀 크기를 읽는다. 최소 해상도 검증이 업로드 **전에** 일어나야 공급자 왕복을 아낀다.
    /// </summary>
    Task<(int Width, int Height)> MeasureAsync(Stream image, CancellationToken ct);

    /// <summary>
    /// PNG 로 바꾼다. 결과가 <paramref name="maxBytes"/> 를 넘으면 확정 실패다 —
    /// 줄여서 올리면 우리가 모르는 품질 손실이 결과 mesh 에 남는다.
    /// </summary>
    Task<Stream> ToPngAsync(Stream image, long maxBytes, CancellationToken ct);

    /// <summary>
    /// 가로로 좌우 반전한다 (spec 20260917 대칭 선택).
    ///
    /// 원본 형식 그대로 다시 인코딩해 돌려준다 — 호출자가 이미 아는 <paramref
    /// name="contentType"/> 을 그대로 쓸 수 있어야, 이 결과를 기존 검증·업로드
    /// 파이프라인에 그대로 흘려보낼 수 있다.
    /// </summary>
    Task<Stream> FlipHorizontallyAsync(Stream image, string contentType, CancellationToken ct);

    /// <summary>
    /// 평가 프레임 정규화 (background-similarity-tuning §8.1) — EXIF 방향을 적용하고
    /// 지정한 프레임에 contain 으로 배치한다. 프레임은 렌더의 비율을 따른다(원본 비율
    /// 캡처 — 사용자 결정으로 §8.1 의 정사각 고정을 대체). 남는 영역은
    /// <paramref name="backgroundHex"/>. **원본 blob 은 바뀌지 않는다** — 평가 요청에
    /// 실을 byte 만 만든다: reference 와 render 가 같은 프레임이어야 비교가 공정하다.
    /// </summary>
    Task<byte[]> NormalizeToFrameAsync(
        Stream image, int frameWidth, int frameHeight, string backgroundHex, CancellationToken ct);
}
