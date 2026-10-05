namespace Noxtend.Domain.Mesh;

/// <summary>
/// 외부 유료 제출 한 번의 진행 상태.
///
/// Design Ref: §5.3
///
/// **재기동한 워커가 "지금 무엇을 해도 되는가" 를 이 값 하나로 판단한다.** 그래서 상태가
/// 실행 단계보다 잘게 나뉜다 — 특히 <see cref="Submitting"/> 과 <see cref="Submitted"/> 를
/// 가르는 것이 이 열거형의 존재 이유다.
/// </summary>
public enum MeshRunStatus
{
    /// <summary>입력 네 장을 아직 하나도 올리지 않았다.</summary>
    PreparingInputs,

    /// <summary>일부만 올렸다. 재기동하면 빈 방향만 올린다.</summary>
    Uploading,

    /// <summary>네 token 이 다 모였다. 아직 유료 호출을 하지 않았다.</summary>
    ReadyToSubmit,

    /// <summary>
    /// **POST 직전에 저장하는 값.** 여기서 프로세스가 죽으면 작업이 만들어졌는지 알 수 없다.
    ///
    /// 이 저장이 없으면 재기동한 워커가 "아직 안 보냈다" 와 "보냈는데 응답을 못 받았다" 를
    /// 구별할 수 없어, 둘 중 하나를 골라야 하고 어느 쪽을 골라도 절반은 틀린다.
    /// </summary>
    Submitting,

    /// <summary>외부 작업 ID 를 받아 저장했다. 이제 재기동해도 POST 하지 않는다.</summary>
    Submitted,

    /// <summary>상태를 주기적으로 조회하는 중.</summary>
    Polling,

    /// <summary>성공을 확인해 결과를 내려받는 중. 공급자 URL 은 5분이면 만료된다.</summary>
    Downloading,

    /// <summary>GLB 와 미리보기가 자체 저장소에 들어갔다. 외부 호출 없이 결과를 붙일 수 있다.</summary>
    ArtifactsReady,

    /// <summary>확정 실패. 원인은 <c>FailureCode</c> 에 있다.</summary>
    Failed,

    /// <summary>
    /// 제출 결과를 알 수 없다 (Plan D-06).
    ///
    /// **자동 재제출하지 않는다.** 이미 만들어진 유료 작업이 있을 수 있어, 다시 보내면
    /// 같은 파츠에 두 번 과금된다. 운영자가 공급자 대시보드와 대조한 뒤에만 다시 시도한다.
    /// </summary>
    SubmissionUnknown,

    /// <summary>
    /// 최대 대기 시간을 넘겼다. **외부 작업 ID 는 보존한다** — 새로 만들면 또 과금이다.
    /// </summary>
    TimedOut,

    /// <summary>결과 URL 이 저장 전에 만료됐다. 자동으로 새 유료 작업을 만들지 않는다.</summary>
    ResultExpired,

    /// <summary>
    /// 사용자가 취소했다.
    ///
    /// **로컬 취소다** — 공식 v3 에 원격 취소 endpoint 가 없어 외부 작업은 계속 돌 수 있다.
    /// 우리가 보장하는 것은 그 결과를 작업에 연결하지 않는다는 것뿐이다 (Plan D-07).
    /// </summary>
    LocalCanceled,
}
