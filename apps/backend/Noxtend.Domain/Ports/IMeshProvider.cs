using Noxtend.Domain.Job;
using Noxtend.Domain.Mesh;

namespace Noxtend.Domain.Ports;

/// <summary>
/// 3D 생성 공급자 — 단계를 모르는 실행기.
///
/// Design Ref: §6.1 · Plan D-03
///
/// **호출이 넷인 이유는 외부 작업이 비동기이기 때문이다.** 이미지 공급자는 한 번 부르고
/// 바이트를 받지만, 여기서는 올리고·보내고·기다리고·내려받는다. 그 네 지점이 각각
/// 재기동 경계라 한 메서드로 합칠 수 없다.
///
/// **공급자 어휘가 이 경계를 넘지 않는다.** `file_token`, `task_id`, `queued/running` 같은
/// 문자열은 어댑터 안에서 중립 타입으로 바뀐다. 두 번째 공급자를 붙일 때 도메인과
/// 오케스트레이션이 한 줄도 안 바뀌게 하는 것이 이 경계의 값이다.
/// </summary>
public interface IMeshProvider
{
    /// <summary>입력 한 장을 올리고 불투명한 참조를 받는다.</summary>
    Task<MeshInputHandle> UploadInputAsync(MeshInputUpload input, CancellationToken ct);

    /// <summary>
    /// **유료 호출.** 부르기 전에 실행 상태를 저장해 두어야 한다 (Plan D-06).
    /// </summary>
    Task<MeshSubmission> SubmitAsync(MultiviewMeshRequest request, CancellationToken ct);

    Task<MeshTaskSnapshot> GetTaskAsync(string providerTaskId, CancellationToken ct);

    /// <summary>
    /// 결과 스트림을 연다.
    ///
    /// **URL 을 돌려주지 않는다.** 공급자 URL 은 5분이면 만료되므로, 받아서 어딘가
    /// 저장하면 그 값은 이미 죽어 있다. 이 호출 안에서 작업을 다시 조회해 갓 나온 URL 로
    /// 스트림을 열고, 그 스트림의 수명만 호출자에게 넘긴다 (§7.5).
    /// </summary>
    Task<IMeshResultDownload> OpenResultAsync(string providerTaskId, CancellationToken ct);
}

/// <summary>
/// 올릴 입력 한 장.
///
/// <paramref name="SafeFileName"/> 는 **방향 이름만** 쓴다 (§7.2). 파츠 이름이나 사용자
/// 파일명을 넣으면 우리 쪽 어휘가 공급자 로그에 남는다.
/// </summary>
public sealed record MeshInputUpload(
    ViewDirection Direction,
    Stream Content,
    string ContentType,
    string SafeFileName,
    long Length);

/// <summary>
/// 공급자가 준 입력 참조. 내용은 해석하지 않는다.
///
/// Design Ref: §4.1 · D-10
///
/// <paramref name="IsDurable"/> 은 **이 값을 저장해 두고 다시 써도 되는가**다.
///
/// | 공급자 | Value | IsDurable |
/// |---|---|---|
/// | Tripo | 업로드한 <c>file_token</c> | <c>true</c> |
/// | Meshy | 요청 본문에 실릴 base64 data URI | <c>false</c> |
///
/// **<c>false</c> 는 "저장하지 말라" 는 뜻이다.** 값이 열 폭을 넘고, 저장하면 DB 가 이미지
/// 저장소가 되며, 본문을 남기지 않는다는 규칙(NFR-02)과도 어긋난다.
///
/// 기본값을 두지 않는다 — 셋째 공급자를 붙이는 사람이 이 값을 의식적으로 정해야 한다.
/// </summary>
public sealed record MeshInputHandle(string Value, bool IsDurable);

/// <summary>
/// 4방향 제출.
///
/// **방향이 지도의 키다** (Plan D-05). 배열이면 순서로 해석되는데, Tripo 의 위치 기반
/// 순서는 우리 내부 순서와 좌우가 다르다.
/// </summary>
public sealed record MultiviewMeshRequest(
    IReadOnlyDictionary<ViewDirection, MeshInputHandle> Inputs,
    string Model,
    int ModelSeed,
    int TextureSeed);

/// <summary>제출 결과 — 외부 작업 ID 하나뿐이다.</summary>
public sealed record MeshSubmission(string ProviderTaskId);

/// <summary>공급자 상태 문자열을 중립 값으로 옮긴 것.</summary>
public enum MeshTaskState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>
/// 작업 조회 결과.
///
/// <paramref name="FailureCode"/> 는 사용자에게 보여도 안전한 내부 코드다. 공급자 원문
/// 메시지는 키가 되비쳐 오는 경우가 있어 여기까지 오지 않는다 (§13.1).
/// </summary>
public sealed record MeshTaskSnapshot(
    MeshTaskState State,
    int Progress,
    int? CreditsConsumed,
    string? FailureCode,
    int? ProviderCode,
    string? ProviderRequestId);

/// <summary>
/// 결과 스트림 묶음.
///
/// Design Ref: §4.2 · D-08
///
/// <see cref="IAsyncDisposable"/> 인 이유는 안에 살아 있는 HTTP 응답이 있기 때문이다.
/// 스트림을 다 쓰기 전에 응답을 닫으면 내려받기가 끊긴다.
///
/// **`Model` 과 `Preview` 프로퍼티가 아니라 목록이다.** GLB 가 필수라는 것은 이제 타입이
/// 아니라 저장 단계가 확인한다 — <see cref="Parts"/> 에 GLB 가 없으면 확정 실패다.
/// 호출자가 목록을 순회하므로 **형식이 늘어도 오케스트레이션이 안 바뀐다.**
/// </summary>
public interface IMeshResultDownload : IAsyncDisposable
{
    IReadOnlyList<MeshResultPart> Parts { get; }
}

/// <summary>
/// 내려받는 산출물 하나.
///
/// <paramref name="ContentType"/> 은 공급자가 선언한 값이라 믿지 않는다 — 저장 단계가
/// magic bytes 로 다시 판정한다.
/// </summary>
public sealed record MeshResultPart(
    MeshArtifactKind Kind, Stream Content, string? ContentType);
