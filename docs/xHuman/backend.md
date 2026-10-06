# Backend 코드 안내

Backend의 상시 규칙은 [apps/backend/AGENTS.md](../../apps/backend/AGENTS.md)가 정본이다. 이 문서는 서버 코드를 찾기 위한 경로 지도다. 현재 구현과 다르면 코드·테스트·실행 설정을 확인하고 이 안내를 고친다.

## 서버 구조

API와 Worker는 같은 ASP.NET Core 호스트에서 실행된다. 프로젝트 경계는 `.csproj`와 [LayerBoundaryTests](../../apps/backend/Noxtend.Tests/Architecture/LayerBoundaryTests.cs)로 확인한다.

| 책임 | 시작점 |
| --- | --- |
| 호스트 조립·HTTP 진입점·Worker 등록 | `apps/backend/Noxtend.Api/Program.cs`, `Controllers/`, `Workers/TaskWorker.cs`, `Workers/TaskWorkerRegistration.cs` |
| Use case와 작업 조정 | `apps/backend/Noxtend.Application/` |
| 상태 전이와 외부 Port | `apps/backend/Noxtend.Domain/Job/`, `apps/backend/Noxtend.Domain/Ports/` |
| DB·Redis·Blob·공급자 구현과 DI | `apps/backend/Noxtend.Infrastructure/`, `InfrastructureServiceCollectionExtensions.cs` |
| 프롬프트·호출 기록·골든 샘플·단가 | `apps/backend/Noxtend.Tuning.Domain/`, `Noxtend.Tuning.Application/` |
| Backend 테스트 | `apps/backend/Noxtend.Tests/` |

## 작업 흐름별 진입점

| 변경 대상 | 코드 흐름 | 관련 테스트 시작점 |
| --- | --- | --- |
| 텍스트 공정 | `TaskWorker` → `RunTaskHandler` → `StageRegistry`·`IStage`의 변수·스키마·응답 처리 → `ILlmProvider` | `Noxtend.Tests/Application/RunTaskHandlerTests.cs`, 단계별 테스트 |
| 파츠 이미지 생성 | Worker → `RunGenerationTaskHandler` → `GenerationStage` → `IImageProvider` | `Noxtend.Tests/Application/RunGenerationTaskHandlerTests.cs`, `ImageProviderUsageTests.cs` |
| 유사도 평가 | `EvaluateSimilarityHandler` → `ILlmProvider` | `Noxtend.Tests/Application/SimilarityEvaluateHandlerTests.cs`, `SimilarityStartHandlerTests.cs` |
| 공급자 관리 | `ProvidersController` → `ProviderHandlers` → Provider 도메인·Repository | `Noxtend.Tests/Application/ProviderHandlerTests.cs`, API 공급자 테스트 |
| 프롬프트·호출 내역·단가 | Tuning Controller → Tuning Application → Tuning Domain·Infrastructure adapter | `PromptGridTests.cs`, `TuningTests.cs`, `RecordingLlmProviderTests.cs`, 가격 관련 테스트 |

LLM·이미지 공급자 선택, 프롬프트 조회, 호출 기록, 가격 계산을 함께 바꿀 때는 [공급자와 프롬프트](providers-and-prompts.md)를 읽는다.

## 2D 배경 도메인 기반

`Noxtend.Domain/Job/ProductionMode.cs`와 `PipelineJob.Sprites.cs`가 제작 모드를 구분한다. 기존 `PipelineJob.Create`는 메시 공급자가 없어도 `ThreeD`이며, `CreateSprites`는 `Background`·`TwoD` 작업과 `Analyzing` 상태를 만든다. 이 도메인 생성 단계에서는 공정을 계획하지 않는다.

`Noxtend.Domain/Sprites/SpriteTypes.cs`, `SpriteRules.cs`, `SpritePipelineState.cs`가 설정·계획 검증과 고정 캔버스 변환을 담당한다. 현재 도메인 상한은 원본 16,777,216픽셀, 대상 1~12개, 승인 묶음 64프레임이다. 정적 대상은 기준 이미지 1장, 루프는 기준 이미지를 포함해 4·8프레임이며 FPS는 1~30, 동작 설명은 최대 500자다.

레이어는 원본 비율을 유지하고 긴 변 1024px까지 축소하며 확대하지 않는다. 타일 너비는 64·128·256px이고 아이소메트릭은 2:1 다이아몬드다. 모든 프레임은 동일한 contain 변환을 사용한다. ROI는 유한한 정규화 좌표로 원본 내부에 있어야 하며, 기존 3D `Bounds.IsWithinFrame`의 오차 여유를 바꾸지 않는다. 대상 ID와 순서는 고유해야 하고 이름은 비어 있지 않아야 한다. 순서는 오름차순으로 뒤에서 앞으로이며, 맨 뒤 이외 레이어와 모든 다이아몬드 타일은 투명이 필수다.

`PipelineJob.Sprites.cs`의 `ReplaceSpritePlan`, `ApproveSpritePlan`, `ApproveSpriteBases`, `RegenerateSpriteFrame`, `ApproveSpriteAsset`, `CaptureSpriteExport`가 검수와 고정 입력을 관리한다. 생성 입력 변경은 해당 대상의 `PlanRevision`과 슬롯을 갱신하고, FPS·이름·순서 변경은 이미지·공정을 유지하며 해당 승인만 무효화한다. `ReviewRevision`은 요청의 낡은 검수를 거부하고 no-op에서는 증가하지 않는다. 기준 교체는 후속 프레임과 승인을 무효화하며 삭제된 대상의 이미지·패키지 이력은 `SpritePipelineState`에 보존한다.

`SpriteInputs.cs`의 `SpriteFrameInput.Plan`은 접수 시점의 계획이며 비동기 결과는 `IsCurrentTask`와 `TryAttachSpriteImage`가 대상·슬롯·입력 revision·기준 이미지를 확인해 반영한다. `SpriteManifest.cs`는 파일 좌표 계약이다. 내보내기는 승인된 이미지 snapshot을 고정하며 포함 대상 변경만 패키지 자격을 무효화한다. 대상 추가·삭제는 포함·제외 목록도 변경하므로 모든 기존 패키지 자격을 무효화한다. 정적 대상의 기준 승인만으로 성공하지 않으며 패키지 완료 후 전체 대상이면 성공, 명시적 일부 대상이면 부분 성공이다. 개별 승인 후에도 다른 현재 공정이 실행 중이면 Running과 해당 생성 단계를 유지한다. 패키지 완료 자격은 표시용 Phase 대신 가장 최근 접수된 패키지 공정의 current 자격과 승인 snapshot으로 판정하므로 제외 대상의 편집·승인이 완료를 막지 않는다. `SpritePipelineState.CompletedExportId`는 작업을 종료한 마지막 패키지 ID이며, 명시적으로 다시 연 작업을 같은 이전 패키지가 재종료시키지 않도록 유지한다. 새 ExportId 완료는 정상 반영하고 이전 패키지의 current·다운로드 이력 자격은 보존한다. 생성 일부 실패에 쓸 이미지가 있으면 검수 대기, 없으면 실패이며 취소는 재개방하지 않는다.

관련 검증은 `Noxtend.Tests/Domain/SpriteRulesTests.cs`, `SpriteLifecycleTests.cs`다. 이 기반은 아직 HTTP·Worker·SQL 저장 경로에 연결되지 않았다. 실제 리스 시도 소유권은 실행기의 최신 공정 재조회와 시도 비교로 연결해야 하며 이미지 생성 시각만으로 보장하지 않는다. `PipelineJobConfiguration`은 전용 매핑·migration이 연결되기 전까지 `Sprites`·`ProductionMode`와 공정의 `SpriteInput`·`SpriteExportInput`·`RequestId`를 명시적으로 제외해 기존 EF 모델을 유지한다.

## 서버 불변 조건

- SQL Server의 작업·공정 상태가 정본이고 Redis Streams는 디스패치 수단이다. 메시지나 Worker 메모리만으로 완료·재시도를 확정하지 않는다.
- Domain은 EF·Redis·Blob·외부 SDK에 의존하지 않는다. 새 외부 연동은 Domain Port, Infrastructure adapter, DI 등록의 실제 경계를 따른다.
- 작업 취소·부분 성공·재시도·검수 승인·단계 전이를 서로 구분한다. lease·claim·retry·결과 반영의 순서는 `TaskExecution`과 해당 handler에서 확인한다.
- API 응답은 Controller 계약과 `ApiResults`를 확인한다. 기존 상태 코드·파일 응답·오류 봉투 예외를 무심코 통일하지 않는다.
- DB 모델 변경은 기존 migration을 고치지 않고 후속 migration으로 반영한다. 운영 데이터·실 DB 적용은 배포 지침과 요청 범위를 확인한다.

## 탐색과 검증

- `Noxtend.slnx`, 영향을 받는 `.csproj`, 관련 테스트와 DI 등록을 확인한다.
- API 계약이 바뀌면 Frontend의 API 파서·타입·화면 사용처도 확인하고 양쪽 스택을 검증한다.
- Backend 명령·Docker 요구·유료 smoke 조건은 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다. 유료 smoke가 조건 미충족으로 건너뛴 경우 실 공급자 성공으로 보고하지 않는다.
- 인프라·키 보존·실제 DB 적용을 건드리면 [배포 지침](../../deploy/AGENTS.md)도 확인한다.
