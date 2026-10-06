namespace Noxtend.Domain.Job;

/// <summary>
/// 공정의 종류 — 단계.
///
/// Design Ref: §0 용어 사전 · §2.3 — 단계 추가는 이 열거형 + 워커 + 스트림 세 가지로
/// 끝나야 한다 (§10.4). 사이클 #5 에서 그 주장이 검증된다: 값 둘이 늘고
/// 오케스트레이터·스위퍼·취소는 한 줄도 바뀌지 않는다.
///
/// **순서가 곧 실행 순서다.** 값의 나열 순서는 문서일 뿐이고 실제 순서는
/// `DependsOnTaskId` 가 정하지만, 읽는 사람이 헷갈리지 않게 맞춰 둔다.
/// </summary>
public enum TaskKind
{
    /// <summary>
    /// 장면 분석 — 팔레트·시점·광원·스케일을 구조화된 값으로 고정한다.
    ///
    /// 사이클 #5 신설. 파츠를 따로 생성해 한 장면으로 합치려면 이 기준이 먼저 있어야
    /// 한다 (Plan D-12·D-13). 이전에는 추출이 자유 문장으로 겸했는데, 조립에 결정적인
    /// 시점·광원 방향·수평선·스케일이 빠져 있었다.
    /// </summary>
    Analyze,

    /// <summary>파츠 식별 — 장면 안의 물체 이름을 센다.</summary>
    Extract,

    /// <summary>
    /// 파츠 분해 — 파츠마다 서술·분류·좌표·깊이·가림을 채운다.
    ///
    /// 이미지 1장에 호출 1번이다 (Plan D-2). 가림 관계는 파츠들 **사이**의 정보라
    /// 파츠별로 나눠 부르면 서로를 몰라서 낼 수 없다.
    /// </summary>
    Decompose,

    /// <summary>
    /// 서술 재작성 — 검수에서 가려지게 된 파츠의 <c>description</c> 만 다시 쓴다
    /// (occludedby-recompute §입력→출력 3).
    ///
    /// **좌표를 묻지 않는다.** 응답 스키마에 <c>bounds</c> 가 없으므로 모델이 사람이 그린
    /// 좌표를 바꿀 방법 자체가 없다. 가림 관계는 코드가 이미 정했고 여기서 물을 것은
    /// "가린 부분을 뺀 서술" 하나다.
    ///
    /// **승인당 한 번이다.** 편집을 몇 개 했든 대상 파츠를 묶어 한 번에 부른다.
    /// </summary>
    RewriteDescriptions,

    /// <summary>
    /// 파츠 이미지 생성 — 파츠·방향마다 공정 하나다 (사이클 #7 Plan D-2).
    ///
    /// Design Ref: §3.1
    ///
    /// **앞 셋과 달리 작업당 개수가 고정이 아니다.** 분해가 끝나야 파츠 수를 알기 때문에
    /// 접수 시점에 계획할 수 없다. 계획은 오케스트레이터가 분해 성공 뒤에
    /// <see cref="PipelineJob.PlanReadyFollowUpTasks"/> 로 시킨다 (§2.3 A-2).
    ///
    /// 분해와 반대 방향인 이유: 분해가 한 번인 것은 가림 관계가 파츠들 **사이**의
    /// 정보여서이고, 생성은 파츠·방향마다 **독립**이다. 독립적인 것을 묶으면 실패가 전파된다.
    /// </summary>
    Generate,

    /// <summary>
    /// 3D 재구성 — 파츠의 네 방향 이미지로 mesh 하나를 만든다 (사이클 #10).
    ///
    /// Design Ref: §4.2 · Plan D-01·D-02
    ///
    /// **앞의 모든 단계와 반대로 넷이 하나로 모인다.** 생성이 파츠×방향으로 펼친 것을
    /// 여기서 파츠 단위로 되접는다.
    ///
    /// **배치 수만큼 만들지 않는다** (Plan D-01). 가로등이 여덟 자리에 놓여도 가로등이라는
    /// 에셋은 하나다 — 배치는 그 하나를 어디에 둘지의 문제이고, 여덟 번 만들면 같은 것을
    /// 여덟 번 과금한다.
    ///
    /// 단일 `DependsOnTaskId` 로는 부모 넷을 가리킬 수 없어 **의존을 간선이 아니라
    /// 입력으로 표현한다** — 얼린 이미지 ID 넷이 곧 의존 충족의 증거다 (§4.4).
    /// </summary>
    Reconstruct,

    /// <summary>
    /// 대칭(합성) 이미지 생성 — 다른 방향 이미지를 반전해 이 방향의 이미지를 만든다
    /// (spec 20260917, ADR mirror-as-synthetic-generated-image).
    ///
    /// **`Generate`와 분리한 이유**는 `PlanSelectedViews`가 "그 파츠·방향에 `Generate`
    /// 공정이 존재하면" 건너뛰기 때문이다. 합성용으로 `Generate`를 재사용하면 사용자가
    /// 이후 그 방향의 진짜 이미지를 영구히 생성할 수 없게 된다.
    /// </summary>
    Synthesize,
    AnalyzeSprites = 7,
    GenerateSprite = 8,
}

/// <summary>공정의 상태.</summary>
public enum TaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>작업의 상태. 공정들의 결과가 모여 결정된다.</summary>
public enum JobStatus
{
    Pending,
    Running,

    /// <summary>
    /// 검수 대기 — 분해는 끝났지만 <see cref="PipelineJob.RequiresReview"/> 인 작업의
    /// 생성 팬아웃을 사람의 전체 승인까지 미룬다 (review-gate §함정1).
    ///
    /// **종료 상태가 아니다.** <see cref="PipelineJob.TerminalStatuses"/> 에 넣으면 삭제·
    /// 재시도 판정이 검수 대기 작업을 끝난 것으로 오판한다.
    /// </summary>
    PendingReview,

    Succeeded,

    /// <summary>
    /// 생성 공정 일부만 성공했다 (사이클 #7 Plan D-3).
    ///
    /// Design Ref: §3.1 · §2.3 A-3
    ///
    /// **<see cref="Succeeded"/> 로 뭉개지 않는 이유**는 이미지가 빈 파츠가 있다는 사실이
    /// 조립 단계의 입력 조건이기 때문이다. "성공 + 실패 파츠 수" 지표로 두면 기존 소비자가
    /// 전부 성공으로 읽어 화면·API·테스트가 조용히 틀린다. 값으로 두면 컴파일러가 소비자를 찾아준다.
    ///
    /// **앞 세 단계에는 이 상태가 없다** — 뒤가 못 도는 실패는 여전히 작업 실패다 (FR-07).
    /// </summary>
    PartiallySucceeded,

    Failed,
    Canceled,
}
