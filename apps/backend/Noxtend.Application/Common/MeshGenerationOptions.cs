namespace Noxtend.Application.Common;

/// <summary>
/// 3D 제작 공정의 운영 값.
///
/// Design Ref: §12.1 · §8.3 · Plan D-04·D-07
///
/// **텍스트·이미지와 자릿수가 다르다.** 외부 작업이 10~120초 걸리므로 리스가 분 단위이고,
/// 조회 간격은 초 단위다. 같은 값을 나눠 쓰면 어느 한쪽이 늘 틀린다.
///
/// **품질 옵션은 여기 없다** (Plan D-04). `face_limit` 이나 texture 설정은 코드 기본값으로
/// 고정한다 — 운영자가 조합을 바꾸면 결과가 왜 달라졌는지 나중에 재현할 수 없다.
/// 작업에 저장되는 것은 모델 스냅숏 이름 하나뿐이다.
/// </summary>
public sealed class MeshGenerationOptions
{
    /// <summary>
    /// 동시에 도는 3D 공정 수.
    ///
    /// Tripo P Series 기본 동시성이 5건이라 여유를 두고 3으로 시작한다. 계정 tier 가 더
    /// 낮아 429 가 오면 header 기반 backoff 가 받아내고, 그때 이 값을 낮춘다.
    /// </summary>
    public int Workers { get; init; } = 3;

    /// <summary>외부 작업이 길어 리스도 길다. 조회 재개가 가능하므로 넉넉히 잡는다.</summary>
    public double LeaseSeconds { get; init; } = 900;

    public double LeaseRenewSeconds { get; init; } = 30;

    /// <summary>
    /// 한 실행의 최대 대기 시간. 넘으면 외부 작업 ID 를 보존한 채 끝낸다 —
    /// 새로 만들면 또 과금이다.
    /// </summary>
    public double RunTimeoutSeconds { get; init; } = 600;

    /// <summary>공급자 권장 조회 간격.</summary>
    public double PollIntervalSeconds { get; init; } = 2;

    /// <summary>같은 요청의 429/5xx 재시도 상한. 유료 제출에는 적용되지 않는다.</summary>
    public int MaxHttpRetries { get; init; } = 5;

    /// <summary>Tripo 한도는 20 MB 다. 그보다 낮게 잡을 수 있게 설정으로 둔다.</summary>
    public long MaxInputImageBytes { get; init; } = 20 * 1024 * 1024;

    /// <summary>
    /// 제출 본문의 상한 (meshy-provider §5.3 · D-13).
    ///
    /// **이 상한이 실제로 문다.** 입력이 장당 20MB 까지 허용되므로 네 장이면 80MB 이고,
    /// base64 로 실으면 약 107MB 다. Tripo 는 장당 올려서 없던 문제다.
    ///
    /// 넘으면 확정 실패다 — 같은 이미지를 다시 보내면 같은 크기다.
    /// </summary>
    public long MaxRequestBodyBytes { get; init; } = 24 * 1024 * 1024;

    public long MaxModelBytes { get; init; } = 128 * 1024 * 1024;

    public long MaxPreviewBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// 결과를 내려받아도 되는 host.
    ///
    /// **공급자 응답을 통한 SSRF 를 막는 자리다** (§13.2). 결과 URL 은 공급자가 주는
    /// 값이라 우리 네트워크 안쪽을 가리킬 수 있다.
    /// </summary>
    /// <remarks>
    /// **Meshy host 를 빠뜨리면 결과를 한 건도 못 받는다** (meshy-provider D-16).
    /// </remarks>
    public IReadOnlyList<string> AllowedResultHosts { get; init; } = ["tripo3d.ai", "meshy.ai"];

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);
    public TimeSpan LeaseRenew => TimeSpan.FromSeconds(LeaseRenewSeconds);
    public TimeSpan RunTimeout => TimeSpan.FromSeconds(RunTimeoutSeconds);
    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
}
