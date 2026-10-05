namespace Noxtend.Application.Common;

/// <summary>
/// 생성 공정의 운영 노브.
///
/// Design Ref: §2.3 A-4·A-7·A-8
///
/// **텍스트 공정과 값을 나눈 이유는 자릿수다.** 이미지 생성은 장당 수십 초~분이라
/// 텍스트용 리스(2분)로는 매번 스위퍼에 뺏긴다 (Plan R-8). 반면 재시도 한도는
/// "몇 번 다시 물어볼 것인가" 라는 같은 판단이라 <see cref="JobOptions.MaxAttempts"/> 를 공유한다.
///
/// 형식·크기는 조립 기준이 정해지기 전에는 무엇이 맞는 값인지 모른다 — 설정으로 두면
/// 되돌리기 쉽다 (A-8).
/// </summary>
public sealed class GenerationOptions
{
    /// <summary>
    /// 동시에 도는 생성 공정 수 (NFR-09 · Plan R-3).
    ///
    /// <c>TaskWorker</c> 는 <c>await foreach</c> 로 순차 소비하므로 **워커 하나 = 동시 1건**이다.
    /// 새 장치 없이 설정 한 줄로 상한이 잡힌다 (A-4). 파츠가 20개여도 큐에 쌓일 뿐
    /// DB 연결과 공급자 동시 호출은 이 수를 넘지 않는다.
    /// </summary>
    public int Workers { get; init; } = 3;

    /// <summary>텍스트의 3배. 장당 지연이 자릿수가 다르다 (A-7).</summary>
    public double LeaseSeconds { get; init; } = 360;

    /// <summary>취소 지연의 상한이기도 하다 — 워커가 갱신 시점에 작업 상태를 확인한다.</summary>
    public double LeaseRenewSeconds { get; init; } = 30;

    /// <summary>서버 고정값 (A-8). 사용자 선택은 조립 기준이 생긴 뒤에 다시 본다.</summary>
    public string Size { get; init; } = "1024x1024";

    /// <summary>
    /// 응답 이미지 크기 상한.
    ///
    /// 공급자가 거대한 응답을 내도 메모리를 다 먹지 않게 한다 (§7). 초과는 재시도가 아니라
    /// 즉시 실패다 — 다시 걸어도 같은 모델이 같은 크기를 낸다.
    /// </summary>
    public long MaxImageBytes { get; init; } = 32 * 1024 * 1024;

    /// <summary>
    /// 받아들이는 형식.
    ///
    /// 배경 제거·알파는 이번 범위 밖이라(Plan D-12) PNG 로 충분하지만, 공급자가 WebP 를
    /// 낼 수 있어 열어 둔다. 조립이 알파를 요구하면 여기가 좁아진다.
    /// </summary>
    public IReadOnlySet<string> AllowedContentTypes { get; init; } =
        new HashSet<string> { "image/png", "image/webp", "image/jpeg" };

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);

    public TimeSpan LeaseRenew => TimeSpan.FromSeconds(LeaseRenewSeconds);
}
