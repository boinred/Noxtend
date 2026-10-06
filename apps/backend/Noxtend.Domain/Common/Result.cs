namespace Noxtend.Domain.Common;

/// <summary>
/// 유스케이스 결과.
///
/// **Domain 에 있는 이유** (사이클 #5): `Noxtend.Application` 과
/// `Noxtend.Tuning.Application` 이 둘 다 이 타입을 쓴다. 둘은 서로를 참조하지
/// 않으므로(설계 §2.4) 공통 조상인 Domain 에 있어야 한다. 의존이 없는 값 타입이라
/// "Domain 은 아무것도 참조하지 않는다" 규칙을 깨지 않는다.
///
/// Design Ref: §4.0 — 실패는 예외가 아니라 값이다. 검증 실패·상태 충돌은 정상 흐름의
/// 일부이고, 예외로 다루면 컨트롤러가 예외 종류로 상태 코드를 정하게 된다.
/// 코드가 결과 안에 있으면 매핑이 한 곳에 남는다.
/// </summary>
public readonly record struct Result<T>
{
    private Result(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static Result<T> Ok(T value) => new(true, value, null, null);

    public static Result<T> Fail(string code, string message) => new(false, default, code, message);
}

/// <summary>
/// Design Ref: §4.0 · §10.1 — SCREAMING_SNAKE. 화면과 계약을 이루는 값이므로
/// 문자열 리터럴을 흩어두지 않는다.
/// </summary>
public static class ErrorCode
{
    public const string UploadEmpty = "UPLOAD_EMPTY";
    public const string UploadUnsupportedType = "UPLOAD_UNSUPPORTED_TYPE";
    public const string UploadTooLarge = "UPLOAD_TOO_LARGE";

    public const string JobNotFound = "JOB_NOT_FOUND";
    public const string JobUploadNotFound = "JOB_UPLOAD_NOT_FOUND";
    public const string JobProviderNotFound = "JOB_PROVIDER_NOT_FOUND";
    public const string JobProviderDisabled = "JOB_PROVIDER_DISABLED";
    public const string JobAlreadyTerminal = "JOB_ALREADY_TERMINAL";
    public const string JobActiveCannotDelete = "JOB_ACTIVE_CANNOT_DELETE";

    public const string ProviderNotFound = "PROVIDER_NOT_FOUND";
    public const string ProviderKeyRequired = "PROVIDER_KEY_REQUIRED";
    public const string ProviderKindInvalid = "PROVIDER_KIND_INVALID";
    public const string JobCategoryInvalid = "JOB_CATEGORY_INVALID";

    public const string SpriteSettingsInvalid = "SPRITE_SETTINGS_INVALID";
    public const string SpritePlanInvalid = "SPRITE_PLAN_INVALID";

    // character-studio §5.2 — 캐릭터 고유 입력 검증
    public const string JobGenderRequired = "JOB_GENDER_REQUIRED";
    public const string JobGenderInvalid = "JOB_GENDER_INVALID";
    public const string JobPartHintInvalid = "JOB_PART_HINT_INVALID";

    /// <summary>공급자가 계약을 어긴 응답을 냈다. 원문은 싣지 않는다 (§4.2 #13).</summary>
    public const string ProviderBadResponse = "PROVIDER_BAD_RESPONSE";

    public const string ProviderCallFailed = "PROVIDER_CALL_FAILED";

    /// <summary>
    /// 키는 유효하지만 이미지를 읽을 수 있는 모델이 하나도 없다.
    ///
    /// 인증 실패와 구별해야 사용자가 할 일이 달라진다 — 키를 다시 넣을 일이 아니라
    /// 계정 권한이나 공급자 선택을 봐야 한다.
    /// </summary>
    public const string ProviderNoVisionModels = "PROVIDER_NO_VISION_MODELS";

    /// <summary>요청한 모델이 그 공급자의 목록에 없다. 오래된 화면이 보낸 값일 수 있다.</summary>
    public const string JobModelUnavailable = "JOB_MODEL_UNAVAILABLE";

    // ─── 사이클 #5 — 장면·분해 유효성 (Design §4.2) ───
    // 유효성은 품질 판단과 다르다 (Plan D-9). 여기 있는 것은 전부 명백한 오류이고,
    // 사람의 눈은 "서술이 쓸 만한가" 에 써야 한다.

    /// <summary>장면 명세에 camera·light·scale 중 하나라도 없다.</summary>
    public const string SceneIncomplete = "SCENE_INCOMPLETE";

    /// <summary>파츠 좌표가 정규화 범위 0~1 을 벗어났다.</summary>
    public const string PartBoundsOutOfRange = "PART_BOUNDS_OUT_OF_RANGE";

    /// <summary><c>occludedBy</c> 가 존재하지 않는 파츠를 가리킨다.</summary>
    public const string PartUnknownReference = "PART_UNKNOWN_REFERENCE";

    /// <summary>분해가 추출과 다른 파츠 이름을 냈다 — 두 단계의 결과가 어긋난다.</summary>
    public const string PartNameMismatch = "PART_NAME_MISMATCH";

    /// <summary>같은 파츠를 두 번 냈다. 이름 불일치와 다르며 고칠 지시도 다르다.</summary>
    public const string PartDuplicate = "PART_DUPLICATE";

    /// <summary>깊이 순서가 중복됐다. 1 = 가장 앞이고 중복은 순서를 정의하지 못한다.</summary>
    public const string PartDepthDuplicate = "PART_DEPTH_DUPLICATE";

    // ─── 사이클 #5 — 프롬프트 (Design §4.2) ───

    /// <summary>해당 단계에 활성 프롬프트가 없다. 워커까지 보내지 않고 접수에서 막는다.</summary>
    public const string PromptNotActive = "PROMPT_NOT_ACTIVE";

    /// <summary>허용 목록에 없는 <c>{{변수}}</c> 를 썼다. 저장 시점에 거부한다.</summary>
    public const string PromptUnknownVariable = "PROMPT_UNKNOWN_VARIABLE";

    /// <summary>jsonSchema 가 유효한 JSON 이 아니다.</summary>
    public const string PromptSchemaInvalid = "PROMPT_SCHEMA_INVALID";

    /// <summary>프롬프트 버전을 찾을 수 없다.</summary>
    public const string PromptVersionNotFound = "PROMPT_VERSION_NOT_FOUND";

    // ─── 사이클 #5 — 골든 세트 ───

    public const string GoldenNotFound = "GOLDEN_NOT_FOUND";

    /// <summary>골든 샘플이 가리키는 업로드가 없다.</summary>
    public const string GoldenImageNotFound = "GOLDEN_IMAGE_NOT_FOUND";

    /// <summary>단가 행이 없다.</summary>
    public const string PriceNotFound = "PRICE_NOT_FOUND";

    /// <summary>같은 모델·같은 시행일 행이 이미 있다 — 둘이면 어느 쪽이 이길지 알 수 없다.</summary>
    public const string PriceDuplicate = "PRICE_DUPLICATE";

    /// <summary>단가 값이 계산 가능한 형태가 아니다 (음수, 반쪽 장문 구간 등).</summary>
    public const string PriceInvalid = "PRICE_INVALID";

    // ─── 사이클 #7 — 파츠 이미지 생성 (Design §6) ───

    /// <summary>이미지 공급자 설정을 찾을 수 없다.</summary>
    public const string JobImageProviderNotFound = "JOB_IMAGE_PROVIDER_NOT_FOUND";

    /// <summary>이미지 공급자가 사용 중지됐다.</summary>
    public const string JobImageProviderDisabled = "JOB_IMAGE_PROVIDER_DISABLED";

    /// <summary>요청한 이미지 모델이 그 공급자의 목록에 없다.</summary>
    public const string JobImageModelUnavailable = "JOB_IMAGE_MODEL_UNAVAILABLE";

    /// <summary>공급자가 이미지를 내지 않았다. 다시 걸어본다.</summary>
    public const string GenerationEmptyResponse = "GENERATION_EMPTY_RESPONSE";

    /// <summary>기대하지 않은 형식이 왔다. 다시 걸어본다.</summary>
    public const string GenerationUnsupportedFormat = "GENERATION_UNSUPPORTED_FORMAT";

    /// <summary>
    /// 응답 이미지가 상한을 넘었다 — **즉시 실패다.**
    ///
    /// 다시 걸어도 같은 모델이 같은 크기를 낸다. 재시도하면 비용만 태우고,
    /// 그 사이 메모리도 같은 양을 먹는다.
    /// </summary>
    public const string GenerationImageTooLarge = "GENERATION_IMAGE_TOO_LARGE";

    /// <summary>재시도할 수 없는 공정이다 — 실패한 생성 공정이 아니다 (§4.2 #3).</summary>
    public const string TaskNotRetryable = "TASK_NOT_RETRYABLE";

    /// <summary>생성 이미지를 찾을 수 없다.</summary>
    public const string GeneratedImageNotFound = "GENERATED_IMAGE_NOT_FOUND";

    // ─── 사이클 #10 — 3D 재구성 (Design §10.2) ───

    /// <summary>3D 공급자 설정을 찾을 수 없다.</summary>
    public const string JobMeshProviderNotFound = "JOB_MESH_PROVIDER_NOT_FOUND";

    /// <summary>3D 공급자가 사용 중지됐다.</summary>
    public const string JobMeshProviderDisabled = "JOB_MESH_PROVIDER_DISABLED";

    /// <summary>요청한 3D 모델이 그 공급자의 목록에 없거나, 공급자·모델 중 한쪽만 왔다.</summary>
    public const string JobMeshModelUnavailable = "JOB_MESH_MODEL_UNAVAILABLE";

    /// <summary>
    /// 3D 만 고르고 이미지를 고르지 않았다.
    ///
    /// 3D 의 입력이 네 방향 이미지이므로 이미지 없이는 만들 것이 없다. 접수를 받아 두면
    /// 앞 세 단계를 다 치르고 나서 아무 일도 일어나지 않는다.
    /// </summary>
    public const string JobMeshRequiresImages = "JOB_MESH_REQUIRES_IMAGES";

    /// <summary>3D 결과를 찾을 수 없다.</summary>
    public const string GeneratedMeshNotFound = "GENERATED_MESH_NOT_FOUND";

    /// <summary>
    /// 제출 결과를 알 수 없어 다시 돌릴 수 없다 (§10.5).
    ///
    /// 유료 작업이 이미 만들어졌을 수 있어 자동 재시도를 막는다. 운영자가 공급자
    /// 대시보드에서 중복 여부를 확인해야 한다.
    /// </summary>
    public const string MeshSubmissionUnknown = "MESH_SUBMISSION_UNKNOWN";

    // ─── 사이클 #11 — 3D 뒤늦게 붙이기 (Design §5.3) ───

    /// <summary>
    /// 이 작업에는 3D 를 붙일 수 없다.
    ///
    /// **사유를 나누지 않는다** (D-09). 화면이 애초에 같은 조건을 보고 버튼을 그리지
    /// 않으므로 이 오류가 실제로 나오는 것은 경합뿐이다 — 조건을 본 뒤 누르기 전에
    /// 다른 세션이 3D 를 붙인 경우. 그때 필요한 것은 사유가 아니라 "다시 불러오세요" 다.
    /// </summary>
    public const string JobMeshNotApplicable = "JOB_MESH_NOT_APPLICABLE";

    // ─── spec 20260917 — 파츠별 3D 전송 뷰 선택 + 대칭 ───

    /// <summary>
    /// 요청한 방향(또는 대칭 소스 방향)에 실제 이미지가 없다.
    ///
    /// 조용히 건너뛰지 않는다 — 사용자가 요청한 것보다 적은 뷰로 3D가 만들어지면서
    /// 아무 에러 없이 성공으로 보이면 원인을 못 찾는다.
    /// </summary>
    public const string MeshInputMissing = "MESH_INPUT_MISSING";

    /// <summary>
    /// 저장 시점에 다른 요청과 충돌했다(RowVersion 또는 유니크 제약). 재시도하면
    /// 대개 해결된다 — merge-gate 리뷰 F2, <see cref="ConcurrencyConflictException"/> 참고.
    /// </summary>
    public const string ReplanMeshConflict = "REPLAN_MESH_CONFLICT";

    /// <summary>
    /// 요청의 `leftRight`/`back` 값이 알려진 enum 이름이 아니다 — 이미지가 없어서가
    /// 아니라 요청 자체가 잘못됐다는 뜻이므로 `MeshInputMissing`과 구분한다
    /// (merge-gate 2차 리뷰 B4).
    /// </summary>
    public const string ReplanMeshSelectionInvalid = "REPLAN_MESH_SELECTION_INVALID";

    // ─── background-similarity-tuning §10.2 — 유사도 실행 ───

    /// <summary>Background/3D/prompt/model/capture 자격 미충족. 사유는 detail 로.</summary>
    public const string SimilarityNotReady = "SIMILARITY_NOT_READY";

    /// <summary>job 에 속하지 않는 run.</summary>
    public const string SimilarityRunNotFound = "SIMILARITY_RUN_NOT_FOUND";

    /// <summary>비종료 run 존재, stale base, 또는 idempotency payload 충돌.</summary>
    public const string SimilarityConflict = "SIMILARITY_CONFLICT";

    /// <summary>렌더 형식·해상도·내용 검증 실패.</summary>
    public const string SimilarityRenderInvalid = "SIMILARITY_RENDER_INVALID";

    /// <summary>렌더가 8 MiB 를 넘었다.</summary>
    public const string SimilarityRenderTooLarge = "SIMILARITY_RENDER_TOO_LARGE";

    /// <summary>공급자 구조화 응답이 계약(여섯 축·allowlist)을 어겼다.</summary>
    public const string SimilarityEvaluationInvalid = "SIMILARITY_EVALUATION_INVALID";

    /// <summary>후보 반복 한도를 넘었다.</summary>
    public const string SimilarityIterationLimit = "SIMILARITY_ITERATION_LIMIT";

    /// <summary>mesh signature 또는 active base 불일치 — 그 배치는 지금 mesh 의 것이 아니다.</summary>
    public const string SceneRevisionStale = "SCENE_REVISION_STALE";
    // ─── review-gate — 검수 게이트 (§목표) ───

    /// <summary>승인·파츠 편집을 검수 대기가 아닌 작업에 걸었다.</summary>
    public const string ReviewNotPending = "REVIEW_NOT_PENDING";

    /// <summary>검수 게이트가 적용되지 않은 작업을 승인하려 했다.</summary>
    public const string ReviewNotRequired = "REVIEW_NOT_REQUIRED";

    /// <summary>검수 화면에서 추가하려는 파츠가 기존 파츠와 겹친다 (§함정6).</summary>
    public const string PartNotOverlapping = "PART_NOT_OVERLAPPING";

    public const string PartNameDuplicate = "PART_NAME_DUPLICATE";

    /// <summary>삭제하려는 파츠 id 가 이 작업에 없다.</summary>
    public const string PartNotFound = "PART_NOT_FOUND";

    // ─── review-gate-staged — 검수 단계 ───

    /// <summary>검수 단계가 맞지 않는 동작 — 서술 단계에서 상자 편집, 상자 단계에서 서술 확정 등.</summary>
    public const string ReviewPhaseMismatch = "REVIEW_PHASE_MISMATCH";

    /// <summary>서술이 빈 파츠가 있거나 빈 서술로 편집하려 했다.</summary>
    public const string PartDescriptionEmpty = "PART_DESCRIPTION_EMPTY";

    /// <summary>팔레트 개수·이름·Hex 형식 위반.</summary>
    public const string PaletteInvalid = "PALETTE_INVALID";
}
