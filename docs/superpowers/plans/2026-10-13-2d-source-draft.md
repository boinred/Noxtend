# 2D 배경 원본 준비 단계 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 2D 배경 프롬프트 모드를 "AI 보완 → 확인·수정 → 원본 후보 생성 → 선택" 흐름으로 바꾸고, 이를 2D 작업의 첫 단계(`SourceDraft`, 화면 "원본 준비")로 SQL에 저장해 새로고침 후에도 복원한다. 2D 분석의 정규화 좌표 안내와 검증 중 발견한 분석 재시도 단계 결함도 고친다.

**Architecture:** 원본 준비는 기존 2D 작업의 새 단계다. 보완·생성은 새 공정 `EnhanceSpriteSource`·`GenerateSpriteSource`로 기존 `TaskExecution`·워커·Sweeper·재시도·취소를 재사용한다. 원본 준비 정보와 후보(작업 소유 Blob 참조)는 nullable 열 `Jobs.SpriteSourceDraftJson`, 공정 고정 입력은 `Tasks.SourceInputJson`에 저장한다. 원본 확정 시 후보 Blob을 `StoredImage`로 복사·검증해 `Analyzing`으로 넘어간다.

**Tech Stack:** .NET 10, EF Core migration(SQL Server), xUnit, Testcontainers, React·TanStack Query, Vitest, Playwright.

**Spec:** [2026-10-13-2d-source-draft-design.md](../specs/2026-10-13-2d-source-draft-design.md)

## Global Constraints

- `SpritePhase.SourceDraft`에서만 `SourceImageId`가 null일 수 있다. 원본을 읽는 handler는 null이면 공정을 Fail로 분류한다. 기존 2D 명령은 `SourceDraft`에서 `SPRITE_NOT_READY`.
- 설명 trim 후 1~1000자, 프롬프트 trim 후 1~4000자, 후보 작업당 8장. 설정은 작업 생성 시 고정.
- 원본 준비 공정은 작업당 하나. 실행·대기 중 명령은 `SPRITE_BUSY`, 단계 불일치는 `SPRITE_NOT_READY`, 후보 상한은 새 코드 `SPRITE_SOURCE_LIMIT`(409).
- 모든 원본 준비 명령과 공정 결과 반영은 `ReviewRevision`을 올린다(`Touch`). 공정을 계획하는 명령은 작업을 `Running`으로, `SourceDraft`의 공정 종료는 활성 공정 유무로 `Running`/`PendingReview`를 정하며 작업을 Failed로 만들지 않는다.
- 새 enum 값: `TaskKind.EnhanceSpriteSource = 10`, `TaskKind.GenerateSpriteSource = 11`, `LlmOperationKind.EnhanceSpriteSource = 8`. 기존 숫자 유지. 화면 용어는 "원본 준비"·"원본 후보 생성"·"이 원본으로 분석".
- 공정 본문은 저장소에 쓰지 않는다. Blob 저장만 본문에서 하고, 작업 반영은 commit 클로저에서 한다(리스 갱신 루프와 `DbContext` 공유).
- 시드: 기존 seed 메서드·migration 수정 금지, 신규 `IF NOT EXISTS` v1, 개정은 비활성화 + `MAX(Version)+1`.
- 검증은 Fake로 한다. 실제 AI·유료 smoke 금지(구현 후 사용자 승인 시 1회 별도).
- 각 Task 커밋은 빌드·관련 테스트가 통과해야 한다. 예외는 Task 2의 `TaskWorkerRegistrationTests` 한 건(Task 3에서 해소)뿐이다.
- 각 Task의 `docs/xHuman` 수정은 같은 커밋. 주석은 루트 `AGENTS.md`와 `karpathy-guidelines`의 `## Comments`.

## Review Focus

1. 미저장 편집으로 생성: 화면에서 프롬프트를 고친 직후 [원본 후보 생성]을 누르면 고친 프롬프트로 생성된다 → Task 7 e2e, Task 5 명령 테스트.
2. 동시 요청: 두 탭에서 동시에 generate하면 하나만 접수되고 다른 하나는 충돌·BUSY다 → Task 5 SQL 테스트.
3. 공정 실패 후 상태: 보완·생성 최종 실패 뒤 작업은 `PendingReview`, 이전 실패 행은 새 공정 성공 후 사라진다 → Task 2 도메인, Task 7 e2e.
4. 원본 확정 크기 차이: 실제 크기가 잠정 캔버스와 다르면 캔버스를 재계산하고 분석이 정상 진행된다 → Task 5 테스트.
5. 홈 목록 안정성: 원본 준비 작업이 하나라도 있을 때 홈 목록·spotlight가 오류 없이 "원본 준비 중"으로 보인다 → Task 6 파서 테스트, Task 7 e2e.

## 실행 방식

**Subagent-driven.** Task 1 독립, Task 2 → 3 → (Checkpoint 4) → 5 → 6 → 7 → 8 순서. Checkpoint 4는 사용자 목업 승인이며 승인 전 Task 5를 시작하지 않는다. 작업마다 새 구현 에이전트와 별도 리뷰 에이전트, 마지막에 전체 변경 리뷰와 양쪽 스택 전체 회귀.

공통 명령(저장소 루트):

```bash
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~<TestClass>'
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'
pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e
pnpm docs:check
dotnet ef migrations add <Name> --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure
```

## Task 1: 2D 분석 정규화 좌표 안내

**Files:**

- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `AnalyzeSpritesV2()` 추가(기존 `AnalyzeSprites()`·상수 유지)
- Create: migration `ReviseSpriteAnalyzePrompt`(Background 슬롯 개정, Down은 새 행 삭제 + 이전 최신 버전 재활성화)
- Test: `Noxtend.Tests/Infrastructure/SpriteAnalyzePromptMigrationTests.cs`(`BackgroundGeneratePromptV3MigrationTests` 패턴), `SeedPromptVariableTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`

**Interfaces:** Produces `SeedPrompts.AnalyzeSpritesV2() : (string System, string User, string Schema, string Note)`.

- [ ] **Step 1: 실패 테스트** — 활성 행이 V2와 같음, 버전 `MAX+1`(운영자 v9 → 10), 다른 슬롯 불변, Down 복원. V2 변수(`settings`, `sourceCanvas`)가 허용 변수 안.
- [ ] **Step 2: 구현** — V2 System에 "`sourceBounds` x·y·w·h는 원본 너비·높이 대비 0~1 비율이며 픽셀이 아니다. 예: 오른쪽 아래 1/4은 `{x:0.5,y:0.5,w:0.5,h:0.5}`", V2 스키마 좌표 필드에 같은 뜻의 `description`. 나머지는 V1과 동일.
- [ ] **Step 3: 검증** — 새 테스트·`SeedPromptVariableTests`·`SpritePersistenceTests` 통과, `pnpm docs:check`.
- [ ] **Step 4: Commit** — `fix(prompts): clarify normalized sprite analysis bounds`

## Task 2: 원본 준비 도메인·영속성

**Files:**

- Modify: `Domain/Job/TaskKind.cs`, `Domain/Llm/LlmOperationKind.cs`(`FromTask`: `EnhanceSpriteSource → EnhanceSpriteSource`, `GenerateSpriteSource → GenerateSpriteSource`), `Domain/Sprites/SpriteTypes.cs`(`SpritePhase.SourceDraft`, `SpriteRequestKind`의 `CreateDraft, EnhanceSource, SaveSourcePrompt, GenerateSource, SelectSource`), `Domain/Common/Result.cs`(`SpriteSourceLimit`)
- Create: `Domain/Sprites/SpriteSourceDraft.cs`
- Modify: `Domain/Job/PipelineJob.cs`(`SourceImageId: Guid?`, `RetryOutputTask` 2D 분기), `Domain/Job/PipelineJob.Sprites.cs`(생성·명령·`IsCurrentTask`·`UpdateSpriteState`·`ReconcileSpriteTasks`·기존 명령 가드), `Domain/Job/PipelineTask.cs`(`SourceInput`), `Domain/Sprites/SpritePipelineState.cs`(`SourceDraft`, `ReplaceCanvases`)
- Modify(null 가드): `Application/Pipeline/RunTaskHandler.cs`, `Application/Similarity/EvaluateSimilarityHandler.cs`, `Application/Sprites/RunSpriteAnalysisTaskHandler.cs`, `Application/Sprites/RunSpriteGenerationTaskHandler.cs`, `Application/Generation/RunGenerationTaskHandler.cs`
- Modify: `Api/Contracts/JobResponse.cs`(`JobResponse`·`JobSummaryResponse`의 `SourceImageId: Guid?`; `source` 필드는 Task 5)
- Modify: `Infrastructure/Persistence/Configurations/PipelineJobConfiguration.cs`(`SourceImageId` optional, `Tasks.SourceInputJson`), `SpritePipelineConfiguration.cs`(`SpriteSourceDraftJson`), `Infrastructure/Persistence/SpriteJsonSerializer.cs`(`Validate`), `Repositories/EfJobRepository.cs`·`InMemory/InMemoryJobRepository.cs`(삭제 시 후보 Blob 키 수집, null 원본 `is {} id` 처리)
- Modify: `Tuning.Application/Prompts/PromptHandlers.cs`(`PipelineKinds`에 `EnhanceSpriteSource`, Background 전용 가드), `Domain/Prompt/PromptTemplate.cs`(`AllowedVariables(EnhanceSpriteSource) = {description, settings}`)
- Create: migration `AddSpriteSourceDraft`(`Jobs.SourceImageId` nullable, `Jobs.SpriteSourceDraftJson`, `Tasks.SourceInputJson`; Down은 null 원본 행이 있으면 실패하도록 명시)
- Modify(parity 유지): `apps/frontend/src/domain/sprites/types.ts`(`SpritePhase` union과 `PHASES`에 `sourceDraft`), `src/domain/job/types.ts`(`TaskKind`·`TASK_KIND_LABELS`), `src/domain/tuning/types.ts`(`enhanceSpriteSource` 라벨·Background), `src/features/screens/categoryLabels.ts`(`spritePhaseLabel.sourceDraft = '원본 준비'`), `src/domain/job/backendParity.test.ts`(수동 `'generateSpriteSource'` 제거)
- Modify(컴파일 유지 테스트): `SpriteApiTests.cs`, `DeleteJobHandlerTests.cs`, `SpriteAnalysisTests.cs`, `SpriteGenerationTests.cs`, `SpriteCommandTests.cs`, `SpriteRequestConcurrencyTests.cs`의 `SourceImageId` 사용
- Test: `Noxtend.Tests/Domain/SpriteSourceDraftTests.cs`(새 파일), `Infrastructure/SpritePersistenceTests.cs`, `Infrastructure/JobDeletionPersistenceTests.cs`, `Domain/LlmOperationTests.cs`, `Application/PromptGridTests.cs`, `Application/SpriteAnalysisTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces (Produces):**

```csharp
public sealed record SpriteSourceCandidate(Guid Id, string BlobKey, string ContentType, int Width, int Height, string Prompt, DateTimeOffset CreatedAt);
public sealed record SpriteSourceDraft(string Description, string? Prompt, Guid TextProviderConfigId, string TextModel,
    IReadOnlyList<SpriteSourceCandidate> Candidates); // MaxCandidates = 8, MaxDescription = 1000, MaxPrompt = 4000
public sealed record SpriteSourceInput(string? Description, string? Prompt); // Tasks.SourceInputJson

PipelineJob.CreateSpriteDraft(Guid imageProviderConfigId, string imageModel, SpriteSettings settings,
    SpriteCanvas provisionalSource, SpriteCanvas generationCanvas, SpriteSourceDraft draft, DateTimeOffset now) : Result<PipelineJob>
Result<PipelineTask> PlanSourceEnhance(string description, Guid requestId)        // 작업 Running
Result<bool> SaveSourcePrompt(string prompt)
Result<PipelineTask> PlanSourceGeneration(string prompt, Guid requestId)          // 프롬프트 저장 + snapshot 고정, 작업 Running
bool TryApplySourcePrompt(Guid taskId, string prompt)                              // current일 때만, Touch
bool TryAddSourceCandidate(Guid taskId, SpriteSourceCandidate candidate)           // current·상한 확인, Touch
Result<PipelineTask> SelectSource(Guid storedImageId, SpriteCanvas actualSource, SpriteCanvas generationCanvas, Guid requestId)
    // SourceImageId 설정, ReplaceCanvases, Phase=Analyzing, AnalyzeSprites(텍스트 공급자·모델은 Draft 값), 작업 Running
```

- [ ] **Step 1: 실패 테스트(도메인)** — 생성 직후 Phase·`Running`·null 원본; 길이 경계(0·1001, 0·4001); 활성 공정 중 `SaveSourcePrompt`·`PlanSourceGeneration`·`PlanSourceEnhance`·`SelectSource`가 `SPRITE_BUSY`; 후보 8장 후 `SPRITE_SOURCE_LIMIT`; `SourceDraft`가 아닌 작업에 원본 준비 명령 `SPRITE_NOT_READY`; `SourceDraft`에서 `ReplaceSpritePlan`·`ApproveSpritePlan`·`ApproveSpriteBases`·`RegenerateSpriteFrame`·`ApproveSpriteAsset`·`CaptureSpriteExport`가 `SPRITE_NOT_READY`; 상태 전이 네 경로(성공 → `PendingReview`, current 아닌 결과 폐기, 최종 실패, 회수 한도 초과) 모두 Failed 아님; `RetryOutputTask`가 두 공정을 Pending으로 되돌리되 다른 미종료 원본 준비 공정이 있으면 거부; 이전 공정은 새 공정 계획 후 current 아님; `SelectSource`가 다른 실제 크기로 캔버스 재계산·`Analyzing`·`AnalyzeSprites`; `SourceDraft` 밖 null 원본 생성 불가; 각 결과 반영이 `ReviewRevision` 증가.
- [ ] **Step 2: 실패 테스트(기존 결함)** — `SpriteAnalysisTests`에 `MaxAttempts=3`, 첫 분석 일시 실패 후 재시도 대기 중 Phase가 `Analyzing`이고 다음 실행에서 분석이 적재됨.
- [ ] **Step 3: 구현(도메인)** — Interfaces. `UpdateSpriteState`는 `SourceDraft`·`Analyzing`에서 Phase를 재계산하지 않는다. `ReconcileSpriteTasks`는 `SourceDraft`에서 `UpdateSpriteState()`만 호출한다. 후보 목록은 불변 교체(EF `ValueComparer` 없음).
- [ ] **Step 4: 실패 테스트(영속성)** — 원본 준비 작업 저장·재조회(Draft·후보·null 원본·`SourceInputJson`), 기존 2D 행(새 열 null) 읽기, 종료된 원본 준비 작업 삭제 시 후보 Blob 키 포함·원본 없음 처리, 원본 확정 후 삭제 시 원본 `StoredImage`와 후보 Blob 정리.
- [ ] **Step 5: 구현(영속성·응답·parity)** — Files 전체. Frontend는 enum·라벨·`PHASES`만 바꾼다.
- [ ] **Step 6: 검증** — 위 테스트, `PipelineJobTests`·`SpriteCommandTests`·`SpriteGenerationTests`·`ReclaimPlanTests`·`PromptGridTests` 통과, `dotnet build` 오류 0, `pnpm test && pnpm typecheck`. `TaskWorkerRegistrationTests`의 `TaskKind`·`Stages` 일치 단정만 Task 3까지 실패하며 명령·출력을 보고서에 남긴다.
- [ ] **Step 7: Commit** — `feat(sprites): add source draft phase domain`

## Task 3: 보완·생성 공정과 프롬프트

**Files:**

- Modify: `Domain/Prompt/PromptTemplate.cs`(`AllowedVariables(GenerateSpriteSource) = {prompt, settings}`)
- Modify: `Infrastructure/Llm/SeedPrompts.cs` — `EnhanceSpriteSource()`, `GenerateSpriteSourceV2()`
- Create: migrations `SeedSpriteSourceEnhancePrompt`(`IF NOT EXISTS` v1, Background), `ReviseSpriteSourcePrompt`(V2 개정)
- Create: `Application/Sprites/RunSpriteSourceEnhanceTaskHandler.cs`, `Application/Sprites/RunSpriteSourceGenerationTaskHandler.cs`, `Application/Sprites/SpriteSourceSizes.cs`(`PickSize` 이동, `GenerateSpriteSourceHandler`도 사용)
- Modify: `Api/Workers/TaskWorkerRegistration.cs`(두 handler, `GenerateSpriteSource`는 `generationWorkers` 그룹), `Api/Workers/ReclaimPlan.cs`(`GenerateSpriteSource`는 generation lease), `Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `Infrastructure/Llm/FakeLlmProvider.cs`(`{"prompt":"..."}`), `Infrastructure/Image/FakeImageProvider.cs`
- Test: `Noxtend.Tests/Application/SpriteSourceTaskTests.cs`(새 파일), `PipelineFixture.cs`, `Infrastructure/SpritePersistenceTests.cs`(새 seed InlineData), `SeedPromptVariableTests.cs`(V1은 부분집합, V2는 일치로 변경), `Api/TaskWorkerRegistrationTests.cs`(워커 수 상수), `Api/ReclaimPlanTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`, `docs/xHuman/job-execution.md`

**Interfaces:** Consumes Task 2 `TryApplySourcePrompt`, `TryAddSourceCandidate`, `PipelineTask.SourceInput`, `SpriteSourceDraft.TextProviderConfigId/TextModel`. 생성 크기는 `SpriteSourceSizes.Pick(IReadOnlyList<SpriteCanvas>)`.

- [ ] **Step 1: 실패 테스트** — 보완: `{"prompt":"..."}` 반영, JSON 위반·빈 값·4000자 초과는 재시도 분류, current 아님 → 반영 없이 Succeeded, `LlmCalls`에 JobId·TaskId·`EnhanceSpriteSource`, 렌더링에 설정·설명. 생성: 이미지 1장 → Blob 저장·후보 추가(검사한 MIME·크기 기록), 2장·비이미지는 재시도 없는 Fail, current 아님·상한 도달 시 저장 Blob 정리, 렌더링된 프롬프트에 snapshot과 설정. 시드·등록·워커 수·리스.
- [ ] **Step 2: 구현** — 보완은 `RunSpriteAnalysisTaskHandler`, 생성은 `RunSpriteGenerationTaskHandler`의 구조(정책, `Classify`, rate-limit gate, finally의 Blob 정리)를 따른다. 본문은 Blob 저장까지, 작업 반영은 commit 클로저. 보완 프롬프트·생성 V2 문구는 Spec "프롬프트" 절.
- [ ] **Step 3: 검증** — 위 테스트와 `TaskWorkerRegistrationTests` 전체 통과, `dotnet build` 오류 0.
- [ ] **Step 4: Commit** — `feat(sprites): run source enhance and generation tasks`

## Checkpoint 4: 목업 승인 (사용자)

- [ ] 1440px·390px 목업을 사용자에게 보이고 승인받는다: 2D 진행 표시(프롬프트 모드 출신 작업의 "원본 준비" 단계, 이미지 모드 작업은 미표시), 원본 준비 패널(설정 요약·프롬프트 편집·후보 그리드·주 행동 버튼 규칙), 홈 "진행 중" 목록·spotlight의 "원본 준비 중"과 썸네일 자리 표시.
- [ ] 승인 내용을 이 문서 실행 기록에 남긴다. 목업이 API 응답 필드를 추가로 요구하면 Task 5 Interfaces를 갱신한 뒤 진행한다.

## Task 5: 원본 준비 명령·API

**Files:**

- Create: `Application/Sprites/SpriteSourceCommandsHandler.cs`, `Application/Sprites/SpriteSourceInspector.cs`(`StartSpriteJobHandler` L69-105 검증·복사 추출)
- Modify: `Application/Sprites/StartSpriteJobHandler.cs`(Inspector 사용), `Application/Sprites/SpriteCommands.cs`
- Modify: `Api/Controllers/SpriteJobsController.cs`(명령 5개, 후보 이미지 GET), `Api/Contracts/SpriteContracts.cs`, `Api/Contracts/JobResponse.cs`(`SpriteResponse.Source`), `Api/Contracts/ApiResults.cs`(`SPRITE_SOURCE_LIMIT` 409)
- Remove: `UploadsController.GenerateAsync`·생성자 인자, `GenerateSpriteSourceRequest`, `GenerateSpriteSourceHandler.cs`, DI 등록, `PipelineFixture.GenerateSpriteSource`, `Tests/Application/SpriteSourceGenerationTests.cs`, `SpriteApiTests.GenerateUpload_*`, `ServiceRegistrationTests` 해당 줄
- Test: `Noxtend.Tests/Application/SpriteSourceCommandTests.cs`(새 파일), `Infrastructure/SpriteRequestConcurrencyTests.cs`(동시 generate), `Api/SpriteApiTests.cs`, `Api/JobResponseTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces (Produces, HTTP 202 `SpriteAcceptedResponse`):**

- `POST /api/jobs/sprites/drafts` `{ requestId, description, providerConfigId, model, imageProviderConfigId, imageModel, settings }`
- `POST /api/jobs/{id}/sprites/source/enhance` `{ requestId, expectedRevision, description }`
- `PUT /api/jobs/{id}/sprites/source/prompt` `{ requestId, expectedRevision, prompt }`
- `POST /api/jobs/{id}/sprites/source/generate` `{ requestId, expectedRevision, prompt }`
- `POST /api/jobs/{id}/sprites/source/select` `{ requestId, expectedRevision, candidateId }`
- `GET /api/jobs/{id}/sprites/source/candidates/{candidateId}` → 후보 이미지(작업 소유 확인, 없으면 404)
- `SpriteResponse.source: { description, prompt, candidates: [{ id, prompt, width, height, createdAt }] } | null`

- [ ] **Step 1: 실패 테스트** — 다섯 명령의 정상 receipt·같은 requestId 재요청 같은 receipt·다른 본문 `SPRITE_REQUEST_CONFLICT`·낡은 revision `SPRITE_REVISION_CONFLICT`·`SPRITE_BUSY`·`SPRITE_NOT_READY`·`SPRITE_SOURCE_LIMIT`·취소 `JOB_ALREADY_TERMINAL`·텍스트 공급자 비활성. generate가 본문 프롬프트를 저장하고 그 snapshot으로 공정 계획. select: 목록에 없는 후보 404 계열, 크기·MIME 실패 400 후 단계·후보 유지, 실제 크기 차이 시 재계산, 독립 Blob 복사. SQL에서 동시 generate 2건 중 1건만 접수. API: 알 수 없는 필드 거부, `sourceImageId: null`·`source` 직렬화, 다른 작업의 후보 id로 이미지 조회 404, `POST /api/uploads/generate` 없음.
- [ ] **Step 2: 구현** — 명령은 `SpriteCommandsHandler.ApplyAsync` 순서(fingerprint → receipt → 작업 → revision → mutate → receipt 저장 → SaveChanges → 충돌 시 `ReloadAsync` → `orchestrator.StartAsync`). 초안 생성은 `StartSpriteJobHandler`처럼 job·task·receipt를 한 SaveChanges에 저장한다.
- [ ] **Step 3: 제거** — 동기 생성 API와 관련 코드·테스트. `LlmOperationKind.GenerateSpriteSource`, `ForSourceGeneration`, `LlmCall.SourceGenerationId`, 기존 seed migration은 유지.
- [ ] **Step 4: 검증** — Backend 전체 회귀(유료 smoke 제외).
- [ ] **Step 5: Commit** — `feat(sprites): add source draft commands and api`

## Task 6: Frontend 데이터 계층

**Files:**

- Modify: `src/domain/sprites/types.ts`(`SpriteSourceDraft` 타입·파서, `source` 필드는 null 허용 필수), `src/domain/job/types.ts`(`sourceImageId: string | null` — `Job`·`JobSummary`)
- Modify: `src/infra/api/spriteApi.ts`(`createSpriteDraft`, `enhanceSpriteSource`, `saveSpriteSourcePrompt`, `generateSpriteSourceCandidate`, `selectSpriteSource`, `spriteSourceCandidateUrl`), `src/app/queries/useSprites.ts`, `src/app/queries/media.ts`, `src/app/queries/useJob.ts`(원본 준비 활성 공정 2초 고정 간격, 순수 함수로 분리)
- Remove: `uploadApi.ts`의 `generateSpriteSource`·`GenerateSpriteSourceInput`, `uploadApi.test.ts` 해당 테스트, `useUpload.ts`의 `useGenerateSpriteSource`
- Modify(null 원본 타입): `features/screens/home/ActiveJobSpotlight.tsx`, `home/WorkStatusSection.tsx`, `background/BackgroundStudioScreen.tsx`, `character/CharacterStudioScreen.tsx`, `GoldenRunsScreen.tsx`, `RunResult.tsx`, `sprites/SpriteStudioScreen.tsx`(3D 화면은 타입 좁히기, 홈 두 곳은 Checkpoint 4 승인대로 자리 표시)
- Modify: `features/screens/admin/PromptsScreen.tsx`(`enhanceSpriteSource` 행), `tests/e2e/fakeApi.ts`(prompt 변수 `generateSpriteSource: ['prompt','settings']`, seed·grid 행에 `enhanceSpriteSource`), `tests/e2e/decomposition-admin.spec.ts`(종류 수)
- Test: `spriteApi.test.ts`, `useSprites.test.ts`, polling 순수 함수 테스트, 스프라이트 파서 테스트(`sourceDraft` job·summary 파싱, `PHASES`와 union 일치를 `satisfies`로 묶음)

**Interfaces:** Consumes Task 5 HTTP. Produces `createSpriteDraft(input: CreateSpriteDraftInput, signal?) : Promise<SpriteAccepted>`, 나머지는 `SpriteMutationContext` + 본문 필드, `SpriteState.source: SpriteSourceDraft | null`, hook `useCreateSpriteDraft`·`useEnhanceSpriteSource`·`useSaveSpriteSourcePrompt`·`useGenerateSpriteSourceCandidate`·`useSelectSpriteSource`.

- [ ] **Step 1: 실패 테스트** — client 본문·경로·메서드·signal, hook 무효화·409·`retry:false`, 파서(누락·잘못된 후보 거부, `source` null, `sourceImageId` null, `sourceDraft` summary), polling 간격.
- [ ] **Step 2: 구현·제거** — Files.
- [ ] **Step 3: 검증** — `pnpm test && pnpm lint && pnpm typecheck`, `pnpm test:e2e -- decomposition-admin`.
- [ ] **Step 4: Commit** — `feat(frontend): add sprite source draft client`

## Task 7: Frontend 화면·e2e

**Files:**

- Modify: `features/screens/sprites/SpriteInput.tsx`(배치 유지, 프롬프트 모드 설명 입력과 [보완하기], 설정·텍스트/이미지 모델·설명 유효 시 활성, 성공 시 `spriteBackgroundJobPath(id)`)
- Create: `features/screens/sprites/SpriteSourceDraftPanel.tsx`(Spec "화면" 절)
- Modify/Remove: `SpritePromptPanel.tsx`(입력 화면 설명 입력으로 축소, "이 화면을 유지해 주세요" 제거), `spritePromptProgress.css`(사용처 없으면 제거)
- Modify: `SpriteStudioScreen.tsx`(`sourceDraft`에서 원본 준비 패널, L148·149·160 가드에서 `sourceDraft` 제외, 공정 목록은 current 원본 준비 공정만, 단계별 취소 문구), `SpriteProgress.tsx`(승인 목업, 고정 인덱스를 Phase 탐색으로), `home/ActiveJobSpotlight.tsx`·`home/WorkStatusSection.tsx`(원본 준비 중 표시)
- Test: `tests/e2e/spriteFakeApi.ts`(draft·원본 준비 명령, 공정 완료 시점 제어(N번째 GET), `SPRITE_BUSY`·`SPRITE_SOURCE_LIMIT` 코드, 후보 이미지 route, `/api/uploads/generate` 제거), `tests/e2e/sprites-static.spec.ts`
- Modify: `docs/xHuman/frontend.md`, `PRODUCT.md`·`DESIGN.md`의 프롬프트 모드 서술

**기존 프롬프트 모드 e2e 대응(`sprites-static.spec.ts`):**

| 기존 테스트(행) | 처리 |
| --- | --- |
| 누적 후 업로드 없이 시작(789) | 교체: 보완 → 생성 2회 → 선택 → 분석, 파일 업로드 0회 |
| 실패 재시도(830)·이후 실패에도 결과 유지(843) | 교체: 생성 실패 후 재시도, 기존 후보 유지, 이전 실패 행 없음 |
| 탭 전환 유지(876) | 유지: 입력 화면 설명·설정 유지 |
| 원본 쿼리 → 이미지 모드(901) | 유지 |
| 진행 중 탭·제출 차단(908)·폭·테마(951)·이전 오류 숨김(992) | 교체: [보완하기] 진행 중 중복 제출 차단, 원본 준비 패널 진행 중 비활성, 1440×900·390px·dark |
| 검증 차단(1021) | 교체: 텍스트 모델 필수 포함 활성 조건 |
| 캐시된 catalog 실패(1039) | 유지 |
| 패널 이미지·긴 텍스트(1080) | 교체: 후보 그리드·긴 설명·긴 프롬프트 넘침 없음 |

- [ ] **Step 1: 실패 e2e** — 위 표와 추가 항목: 프롬프트 수정 후 바로 생성 시 요청 본문이 화면 프롬프트, 다시 보완 확인 취소 시 요청 0회, 생성 공정 실행 중 `page.reload()` 후 진행 표시·후보·비활성 유지, 첫 보완 실패 후 직접 입력해 생성, 후보 8장 후 생성 비활성, 홈 목록·spotlight "원본 준비 중".
- [ ] **Step 2: 구현** — `JobProgressPanel`·`ModelSelect`·`ProviderSelect`·`spriteStyles` 재사용, 목업대로.
- [ ] **Step 3: 검증** — Frontend 전체 회귀 `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`, 초기 JS 예산 통과.
- [ ] **Step 4: Commit** — `feat(frontend): add sprite source draft flow`

## Task 8: 마무리 검증·문서

**Files:** `docs/xHuman/CODEMAP.md` 영역 표에 "2D 원본 준비" 작업 행(backend·frontend 지도 링크), 바뀐 각 지도의 확인 기준 날짜·revision, 이 문서 실행 기록.

- [ ] **Step 1** — 마지막 Backend·Frontend 소스 변경 이후 각 스택 전체 회귀 결과를 실행 기록에 남긴다. 건너뜀·미수집은 통과로 세지 않는다.
- [ ] **Step 2** — `pnpm docs:check`, 바뀐 지도 링크·경로 확인.
- [ ] **Step 3: Commit** — `docs: record 2d source draft implementation`

## 실행 기록

- 2026-10-13 설계 검증: 독립 리뷰 2회(Backend 실현 가능성, Frontend·계약)에서 막히는 문제 6건(커밋 단위 빌드 실패, 후보 저장과 공정 `DbContext` 충돌, 기존 명령의 단계 우회, 상태 전이 미정, 규칙 모순, 미저장 편집 생성)과 빈틈을 찾아 이 계획과 설계에 반영했다. 사용자 결정: 입력 배치 유지 + 활성 조건, 용어 "원본 준비", 생성 요청에 프롬프트 포함, 방치 작업은 홈 표시만.
