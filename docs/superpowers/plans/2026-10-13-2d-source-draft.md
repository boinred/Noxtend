# 2D 배경 기준 준비 단계 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 2D 배경 프롬프트 모드를 "AI 보완 → 확인·수정 → 기준 이미지 생성 → 선택" 단계로 바꾸고, 이 과정을 2D 작업의 첫 단계(`SourceDraft`)로 SQL에 저장해 새로고침 후에도 복원한다. 2D 분석 프롬프트의 정규화 좌표 안내도 보강한다.

**Architecture:** 기준 준비는 기존 2D 작업(`PipelineJob`)의 새 단계다. 보완·생성은 새 공정 `EnhanceSpriteSource`·`GenerateSpriteSource`로 기존 `TaskExecution`·워커·Sweeper·재시도·취소를 재사용한다. 기준 준비 정보는 nullable 컬럼 `Jobs.SpriteSourceDraftJson`, 후보는 `StoredImage`로 저장한다. 원본 확정 시 기존 2D 시작 검증을 공유해 `Analyzing`으로 넘어간다.

**Tech Stack:** .NET 10, EF Core migration(SQL Server), xUnit, Testcontainers, React·TanStack Query, Vitest, Playwright.

**Spec:** [2026-10-13-2d-source-draft-design.md](../specs/2026-10-13-2d-source-draft-design.md)

## Global Constraints

- 기준 준비(`SpritePhase.SourceDraft`)에서만 `SourceImageId`가 null일 수 있다. 다른 단계·3D 작업은 null 원본을 도메인 위반으로 거부한다.
- 설명 trim 후 1~1000자, 프롬프트 trim 후 1~4000자, 후보는 작업당 8장. 설정(시점·형식·타일 크기·반복)은 작업 생성 시 고정한다.
- 한 작업에서 기준 준비 공정(보완·생성)은 동시에 하나만 실행한다. 실행·대기 중 enhance·prompt·generate·select는 `SPRITE_BUSY`, 단계 불일치는 `SPRITE_NOT_READY`, 후보 상한은 새 코드 `SPRITE_SOURCE_LIMIT`(409).
- 변경 요청은 `requestId` receipt와 `expectedRevision`(= `Sprites.ReviewRevision`)을 쓰고 202 `SpriteAcceptedResponse`를 반환한다.
- 새 enum 값: `TaskKind.EnhanceSpriteSource = 10`, `TaskKind.GenerateSpriteSource = 11`, `LlmOperationKind.EnhanceSpriteSource = 8`. 기존 숫자는 바꾸지 않는다.
- 기준 준비 공정 실패는 작업을 종료시키지 않는다(`PendingReview` 유지).
- 시드 프롬프트는 기존 seed 메서드·migration을 수정하지 않고 새 메서드(`...V2`)와 새 migration으로 추가·개정한다. 개정은 비활성화 + `MAX(Version)+1` 삽입 패턴(`BackgroundGeneratePromptV3`)을 따른다.
- 검증은 Fake 공급자·Fake API로 한다. 실제 AI 호출·유료 smoke 금지(구현 후 사용자 승인 시 1회 별도).
- 각 Task의 `docs/xHuman` 수정은 그 Task 코드와 같은 커밋에 넣는다. 주석은 루트 `AGENTS.md`와 `karpathy-guidelines`의 `## Comments`를 따른다.

## Review Focus

1. 늦은 보완 결과: 보완 공정 실행 중 사용자가 프롬프트를 저장하면 그 뒤 도착한 보완 결과가 사용자 프롬프트를 덮어쓰지 않는다 → Task 2 도메인 테스트.
2. 기준 준비 공정 실패: 보완·생성이 최종 실패해도 작업은 `PendingReview`이고 재시도·다시 생성이 된다 → Task 2·4 테스트.
3. 실제 크기 차이: 선택한 후보의 실제 크기가 잠정 원본 캔버스와 다르면 생성·출력 캔버스를 다시 계산한다 → Task 4 테스트.
4. 삭제 정리: 기준 준비 작업 삭제 시 선택되지 않은 후보 `StoredImage`·Blob을 지우되 다른 작업의 원본은 보존한다 → Task 2 persistence 테스트.
5. 새로고침 복원: 생성 공정 실행 중 새로고침하면 진행 표시·기존 후보·비활성 버튼이 그대로다 → Task 6 e2e.

## 실행 방식

**Subagent-driven.** Task 1은 독립이다. Task 2 → 3 → 4 → 5 → 6은 앞 작업의 계약에 의존한다. Task 6 시작 전에 2D 진행 표시 외관 목업을 사용자에게 승인받는다(`apps/frontend/AGENTS.md` 작업 진행 표시 규칙). 작업마다 새 구현 에이전트와 별도 리뷰 에이전트, 마지막에 전체 변경 리뷰와 양쪽 스택 전체 회귀.

공통 명령(저장소 루트):

```bash
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~<TestClass>'
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'
pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e
pnpm docs:check
```

Migration 생성: `dotnet ef migrations add <Name> --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure`(Designer 포함, 손으로 쓰지 않는다).

## Task 1: 2D 분석 정규화 좌표 안내

**Files:**

- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `AnalyzeSpritesV2()` 추가(기존 `AnalyzeSprites()`·상수 유지)
- Create: migration `ReviseSpriteAnalyzePrompt` (Background 슬롯 비활성화 + 새 버전 삽입, Down은 새 행 삭제 후 이전 최신 버전 재활성화)
- Test: `apps/backend/Noxtend.Tests/Infrastructure/SpriteAnalyzePromptMigrationTests.cs`(새 파일, `BackgroundGeneratePromptV3MigrationTests` 패턴), `SeedPromptVariableTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`

**Interfaces:** Produces `SeedPrompts.AnalyzeSpritesV2() : (string System, string User, string Schema, string Note)`.

- [ ] **Step 1: 실패 테스트** — migration 전후로 `AnalyzeSprites`/`Background` 활성 행이 `AnalyzeSpritesV2().System`과 같고, 버전이 `MAX+1`(운영자 v9가 있으면 10), 다른 Kind·Category는 그대로, Down 후 이전 버전이 다시 활성인지 단정한다. `SeedPromptVariableTests`에 V2 변수 집합(`settings`, `sourceCanvas`)이 `PromptTemplate.AllowedVariables(AnalyzeSprites)` 안인지 추가한다.
- [ ] **Step 2: 구현** — V2 System에 "`sourceBounds` x·y·w·h는 원본 너비·높이 대비 0~1 비율이며 픽셀이 아니다. 예: 오른쪽 아래 1/4은 `{x:0.5,y:0.5,w:0.5,h:0.5}`"를 넣고, V2 스키마의 `sourceBounds` 각 숫자 필드에 같은 뜻의 `description`을 둔다. 다른 문장·제약은 V1과 같게 유지한다.
- [ ] **Step 3: 검증** — 새 테스트·`SeedPromptVariableTests`·`SpritePersistenceTests` 통과, `pnpm docs:check`.
- [ ] **Step 4: Commit** — `fix(prompts): clarify normalized sprite analysis bounds`

## Task 2: 기준 준비 도메인·영속성

**Files:**

- Modify: `apps/backend/Noxtend.Domain/Job/TaskKind.cs`, `Domain/Llm/LlmOperationKind.cs`(`FromTask` 매핑 포함), `Domain/Sprites/SpriteTypes.cs`(`SpritePhase.SourceDraft`, `SpriteRequestKind` 5개 추가), `Domain/Common/Result.cs`(`SpriteSourceLimit`)
- Create: `apps/backend/Noxtend.Domain/Sprites/SpriteSourceDraft.cs`
- Modify: `Domain/Job/PipelineJob.cs`(`SourceImageId` → `Guid?`, `RetryOutputTask` 2D 분기), `Domain/Job/PipelineJob.Sprites.cs`(생성·명령·`IsCurrentTask`·`UpdateSpriteState`·`ReconcileSpriteTasks`), `Domain/Sprites/SpritePipelineState.cs`(`SourceDraft` 속성, `ReplaceCanvases`)
- Modify: 원본을 읽는 곳의 null 가드 — `Application/Pipeline/RunTaskHandler.cs`, `Application/Similarity/EvaluateSimilarityHandler.cs`, `Application/Sprites/RunSpriteAnalysisTaskHandler.cs`, `Application/Sprites/RunSpriteGenerationTaskHandler.cs`, `Application/Generation/RunGenerationTaskHandler.cs`(null이면 `ProviderCallFailedException("원본 이미지가 없습니다")`이 아니라 공정 시작 전 도메인 위반으로 Fail)
- Modify: `Infrastructure/Persistence/Configurations/PipelineJobConfiguration.cs`, `SpritePipelineConfiguration.cs`(`SpriteSourceDraftJson`), `Infrastructure/Persistence/SpriteJsonSerializer.cs`(`Validate`에 새 타입), `Repositories/EfJobRepository.cs`·`InMemory/InMemoryJobRepository.cs`(삭제 시 후보 정리, null 원본)
- Create: migration `AddSpriteSourceDraft`(`SourceImageId` nullable, `SpriteSourceDraftJson nvarchar(max) null`)
- Test: `Noxtend.Tests/Domain/SpriteSourceDraftTests.cs`(새 파일), `Infrastructure/SpritePersistenceTests.cs`, `Infrastructure/JobDeletionPersistenceTests.cs`, `Domain/LlmOperationTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces:**

- Produces:
  - `record SpriteSourceCandidate(Guid ImageId, string Prompt, DateTimeOffset CreatedAt)`
  - `record SpriteSourceDraft(string Description, string? Prompt, IReadOnlyList<SpriteSourceCandidate> Candidates)` (`MaxCandidates = 8`, `MaxDescription = 1000`, `MaxPrompt = 4000`)
  - `PipelineJob.CreateSpriteDraft(Guid imageProviderConfigId, string imageModel, SpriteSettings settings, SpriteCanvas provisionalSource, SpriteCanvas generationCanvas, string description, DateTimeOffset now) : Result<PipelineJob>` — Phase `SourceDraft`, `SourceImageId = null`, Status `PendingReview`
  - `Result<PipelineTask> PlanSourceEnhance(string description, Guid providerConfigId, string model, Guid requestId)`
  - `Result<bool> SaveSourcePrompt(string prompt)`
  - `Result<PipelineTask> PlanSourceGeneration(Guid requestId)` — 현재 프롬프트 snapshot을 공정에 고정(`PipelineTask.BindSourcePrompt(string)`)
  - `bool TryApplySourcePrompt(Guid taskId, string prompt)` — 현재 공정이고 그 사이 `SaveSourcePrompt`가 없었을 때만 반영
  - `bool TryAddSourceCandidate(Guid taskId, SpriteSourceCandidate candidate)`
  - `Result<PipelineTask> SelectSource(Guid imageId, SpriteCanvas actualSource, SpriteCanvas generationCanvas, Guid analysisProviderConfigId, string analysisModel, Guid requestId)` — `SourceImageId` 설정, 캔버스 재계산, Phase `Analyzing`, `AnalyzeSprites` 공정 계획
  - `ErrorCode.SpriteSourceLimit = "SPRITE_SOURCE_LIMIT"`, `SpriteRequestKind.{CreateDraft, EnhanceSource, SaveSourcePrompt, GenerateSource, SelectSource}`
  - `LlmOperation.FromTask`: `EnhanceSpriteSource → LlmOperationKind.EnhanceSpriteSource`, `GenerateSpriteSource → LlmOperationKind.GenerateSpriteSource`

- [ ] **Step 1: 실패 테스트(도메인)** — `SpriteSourceDraftTests`: 생성 직후 Phase·Status·null 원본; 설명·프롬프트 길이 경계(0·1001, 0·4001자); 실행 중 공정이 있으면 `SaveSourcePrompt`·`PlanSourceGeneration`·`SelectSource`가 `SPRITE_BUSY`; 후보 8장 뒤 `PlanSourceGeneration`이 `SPRITE_SOURCE_LIMIT`; `SourceDraft` 아닌 작업의 기준 준비 명령이 `SPRITE_NOT_READY`; 보완 실행 중 `SaveSourcePrompt` 후 `TryApplySourcePrompt`가 false이고 프롬프트 유지; 기준 준비 공정 최종 실패 후 `ReconcileFromTasks`가 작업을 Failed로 만들지 않음; `RetryOutputTask`가 두 공정을 Pending으로 되돌리고 Phase 유지; `SelectSource`가 다른 실제 크기로 캔버스를 재계산하고 `Analyzing`·`AnalyzeSprites` 계획; `SourceDraft` 밖에서 null 원본 생성 불가.
- [ ] **Step 2: 구현(도메인)** — 위 Interfaces. `IsCurrentTask`는 두 새 공정이 Phase `SourceDraft`이고 같은 종류 중 최신 Ordinal일 때만 current. `UpdateSpriteState`는 `SourceDraft`에서 Phase를 재계산하지 않는다. `ReviewRevision`은 명령 성공마다 `Touch`로 증가한다.
- [ ] **Step 3: 실패 테스트(영속성)** — `SpritePersistenceTests`: `SourceDraft` 작업 저장·재조회 시 `SpriteSourceDraft`·null 원본·후보 왕복, 기존 2D 행(컬럼 null) 읽기. `JobDeletionPersistenceTests`: 종료된 기준 준비 작업 삭제 시 후보 `StoredImage`·Blob 키 제거, 다른 작업의 `SourceImageId`인 이미지는 보존.
- [ ] **Step 4: 구현(영속성)** — EF 설정·직렬화 `Validate`·migration·삭제 경로(Ef·InMemory 동일 규칙). 원본 null 가드.
- [ ] **Step 5: 검증** — 위 테스트 클래스와 `PipelineJobTests`·`SpriteCommandTests`·`SpriteAnalysisTests`·`ReclaimPlanTests` 통과, `dotnet build` 오류 0. `TaskWorkerRegistrationTests`의 `TaskKind`·`Stages` 일치 단정은 Task 3에서 handler를 등록할 때까지 실패한다. 이 실패만 예외로 보고서에 명령·출력과 함께 기록하고 다른 실패는 허용하지 않는다.
- [ ] **Step 6: Commit** — `feat(sprites): add source draft phase domain`

## Task 3: 보완·생성 공정과 프롬프트

**Files:**

- Modify: `Domain/Prompt/PromptTemplate.cs` — `AllowedVariables`: `EnhanceSpriteSource => {description, settings}`, `GenerateSpriteSource => {prompt, settings}`
- Modify: `Infrastructure/Llm/SeedPrompts.cs` — `EnhanceSpriteSource()`, `GenerateSpriteSourceV2()`
- Create: migrations `SeedSpriteSourceEnhancePrompt`(`IF NOT EXISTS` v1, Background), `ReviseSpriteSourcePrompt`(V2 개정)
- Create: `Application/Sprites/RunSpriteSourceEnhanceTaskHandler.cs`, `Application/Sprites/RunSpriteSourceGenerationTaskHandler.cs`
- Modify: `Api/Workers/TaskWorkerRegistration.cs`, `Api/Workers/ReclaimPlan.cs`(생성은 `GenerationOptions` 리스), `Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `Infrastructure/Llm/FakeLlmProvider.cs`(`{"prompt":"..."}` canned), `Infrastructure/Image/FakeImageProvider.cs`, `Tuning.Application/Prompts/PromptHandlers.cs`(`PipelineKinds`, Background 전용 가드)
- Test: `Noxtend.Tests/Application/SpriteSourceTaskTests.cs`(새 파일), `PipelineFixture.cs`, `Infrastructure/SpritePersistenceTests.cs`(새 seed InlineData), `SeedPromptVariableTests.cs`, `Application/PromptGridTests.cs`, `Api/TaskWorkerRegistrationTests.cs`, `Api/ReclaimPlanTests.cs`, `Domain/LlmOperationTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`, `docs/xHuman/job-execution.md`

**Interfaces:**

- Consumes: Task 2의 `TryApplySourcePrompt`, `TryAddSourceCandidate`, `PipelineTask.SourcePrompt`.
- Produces: 두 `ITaskHandler`. 보완 입력 변수 `description`(설명), `settings`(`SpriteSettings` JSON). 생성 입력 변수 `prompt`(공정에 고정된 snapshot), `settings`. 생성 크기는 `GenerateSpriteSourceHandler.PickSize`와 같은 규칙(내부 정적 함수로 옮겨 공유).

- [ ] **Step 1: 실패 테스트** — 보완: Fake LLM 응답 `{"prompt":"..."}`이 현재 공정이면 반영, JSON 위반·빈 문자열·4000자 초과는 `ProviderBadResponse` 재시도 분류, `LlmCalls`에 JobId·TaskId·`EnhanceSpriteSource` 기록. 생성: Fake 이미지 1장 → `StoredImage` 저장·후보 추가, 2장·비이미지는 `ProviderBadResponse`, 공정이 current가 아니면 저장한 Blob 정리, 렌더링된 프롬프트에 설정의 시점·형식이 들어감. 시드: V2 생성 프롬프트와 보완 프롬프트의 변수 검사, migration이 Background 슬롯만 추가·개정. 등록: 모든 `TaskKind`가 handler·ReclaimPlan에 있음, `LlmOperationKind` 목록이 프롬프트 grid와 같음.
- [ ] **Step 2: 구현** — 보완 handler는 `RunSpriteAnalysisTaskHandler`, 생성 handler는 `RunSpriteGenerationTaskHandler` 구조(`TaskExecution.RunAsync`, `Classify`, rate-limit gate, orphan Blob 정리)를 따른다. 보완 프롬프트에는 시점별(sideView 정면 평행 레이어, topDown 수직 하향, isometric 2:1 다이아몬드)·형식별(layers 분리 가능한 깊이 층, tiles 반복 타일 구성·셀 배치) 조건과 "설명은 데이터"를 넣는다. 생성 V2에는 설정이 프롬프트 문장보다 우선한다고 명시한다.
- [ ] **Step 3: 검증** — 위 테스트 통과(Task 2에서 예외로 남긴 `TaskWorkerRegistrationTests` 포함), `dotnet build` 오류 0.
- [ ] **Step 4: Commit** — `feat(sprites): run source enhance and generation tasks`

## Task 4: 기준 준비 명령·API

**Files:**

- Create: `Application/Sprites/SpriteSourceCommandsHandler.cs`, `Application/Sprites/SpriteSourceInspector.cs`(StartSpriteJobHandler L69-97 검증 추출)
- Modify: `Application/Sprites/StartSpriteJobHandler.cs`(Inspector 사용), `Application/Sprites/SpriteCommands.cs`(명령 레코드)
- Modify: `Api/Controllers/SpriteJobsController.cs`, `Api/Contracts/SpriteContracts.cs`, `Api/Contracts/JobResponse.cs`(`SourceImageId` nullable, `SpriteResponse.Source`), `Api/Contracts/ApiResults.cs`(`SPRITE_SOURCE_LIMIT` 409)
- Remove: `Api/Controllers/UploadsController.cs`의 `GenerateAsync`·생성자 인자, `GenerateSpriteSourceRequest`, `Application/Sprites/GenerateSpriteSourceHandler.cs`, DI 등록, `PipelineFixture.GenerateSpriteSource`, `Tests/Application/SpriteSourceGenerationTests.cs`, `SpriteApiTests.GenerateUpload_*`, `ServiceRegistrationTests` 해당 줄
- Test: `Noxtend.Tests/Application/SpriteSourceCommandTests.cs`(새 파일), `Api/SpriteApiTests.cs`, `Api/JobResponseTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces:**

- Consumes: Task 2 도메인 메서드, Task 3 공정.
- Produces (HTTP, 모두 202 `SpriteAcceptedResponse`):
  - `POST /api/jobs/sprites/drafts` — `{ requestId, description, providerConfigId, model, imageProviderConfigId, imageModel, settings }`
  - `POST /api/jobs/{id}/sprites/source/enhance` — `{ requestId, expectedRevision, description }`
  - `PUT /api/jobs/{id}/sprites/source/prompt` — `{ requestId, expectedRevision, prompt }`
  - `POST /api/jobs/{id}/sprites/source/generate` — `{ requestId, expectedRevision }`
  - `POST /api/jobs/{id}/sprites/source/select` — `{ requestId, expectedRevision, imageId }`
  - `JobResponse.sourceImageId: Guid?`, `SpriteResponse.source: { description, prompt, candidates: [{ imageId, prompt, createdAt }] } | null`
- 모든 요청 DTO는 `[JsonUnmappedMemberHandling(Disallow)]`. 초안 생성은 `StartSpriteJobHandler`와 같은 공급자·모델·capability·`AnalyzeSprites`·`EnhanceSpriteSource`·`GenerateSpriteSource` 활성 프롬프트 검증을 한다.

- [ ] **Step 1: 실패 테스트** — 다섯 명령의 정상 receipt·같은 requestId 재요청 시 같은 receipt·다른 본문 `SPRITE_REQUEST_CONFLICT`·낡은 revision `SPRITE_REVISION_CONFLICT`·진행 중 `SPRITE_BUSY`·단계 불일치 `SPRITE_NOT_READY`·상한 `SPRITE_SOURCE_LIMIT`·취소된 작업 `JOB_ALREADY_TERMINAL`. 선택: 다른 작업 소유가 아닌 후보만 허용(목록에 없는 imageId는 `JOB_UPLOAD_NOT_FOUND`), 크기·MIME 검증 실패 400 후 단계·후보 유지, 실제 크기가 잠정 캔버스와 다르면 재계산. API: 알 수 없는 필드 거부, `sourceImageId: null` 직렬화, `POST /api/uploads/generate` 라우트 없음.
- [ ] **Step 2: 구현** — 명령 handler는 `SpriteCommandsHandler.ApplyAsync`의 fingerprint·receipt·revision·SaveChanges·`ReloadAsync`·`orchestrator.StartAsync` 순서를 따른다. 초안 생성은 `StartSpriteJobHandler`처럼 job·task·receipt를 한 SaveChanges에 저장한다.
- [ ] **Step 3: 제거** — 동기 생성 API와 관련 테스트·등록을 지운다. `LlmOperationKind.GenerateSpriteSource`, `ForSourceGeneration`, `LlmCall.SourceGenerationId`, 기존 seed migration은 과거 기록용으로 유지한다.
- [ ] **Step 4: 검증** — 새·수정 테스트 통과 후 Backend 전체 회귀(유료 smoke 제외).
- [ ] **Step 5: Commit** — `feat(sprites): add source draft commands and api`

## Task 5: Frontend 데이터 계층

**Files:**

- Modify: `apps/frontend/src/domain/sprites/types.ts`(`SpritePhase`에 `sourceDraft`, `SpriteSourceDraft` 타입·파서, 명령 입력 타입), `src/domain/job/types.ts`(`sourceImageId: string | null`, `TaskKind`·`TASK_KIND_LABELS`에 두 공정), `src/domain/tuning/types.ts`(`enhanceSpriteSource` 프롬프트 종류·라벨·Background 카테고리)
- Modify: `src/infra/api/spriteApi.ts`(`createSpriteDraft`, `enhanceSpriteSource`, `saveSpriteSourcePrompt`, `generateSpriteSource`, `selectSpriteSource`), `src/app/queries/useSprites.ts`(대응 hook, 기존 `useSpriteMutation` 재사용; 초안 생성은 `useStartSpriteJob`과 같은 무효화)
- Remove: `src/infra/api/uploadApi.ts`의 `generateSpriteSource`·`GenerateSpriteSourceInput`, `uploadApi.test.ts`의 해당 테스트, `src/app/queries/useUpload.ts`의 `useGenerateSpriteSource`
- Modify: `src/features/screens/categoryLabels.ts`(`spritePhaseLabel.sourceDraft = '기준 준비'`), `src/features/screens/home/ActiveJobSpotlight.tsx`(기준 준비 단계의 현재 공정 매핑), `src/features/screens/admin/PromptsScreen.tsx`
- Test: `src/infra/api/spriteApi.test.ts`, `src/app/queries/useSprites.test.ts`, `src/domain/job/backendParity.test.ts`(LLM operation 목록은 TaskKind 기준으로 자동 포함되므로 수동 추가 `'generateSpriteSource'` 제거), 스프라이트 파서 테스트

**Interfaces:**

- Consumes: Task 4 HTTP 계약.
- Produces: `createSpriteDraft(input: CreateSpriteDraftInput, signal?) : Promise<SpriteAccepted>`; `CreateSpriteDraftInput = { requestId; description; providerConfigId; model; imageProviderConfigId; imageModel; settings }`; 나머지 네 함수는 `SpriteMutationContext` + 본문 필드. `SpriteState.source: SpriteSourceDraft | null`, `SpriteSourceDraft = { description: string; prompt: string | null; candidates: { imageId: string; prompt: string; createdAt: string }[] }`. hook: `useCreateSpriteDraft`, `useEnhanceSpriteSource`, `useSaveSpriteSourcePrompt`, `useGenerateSpriteSourceCandidate`, `useSelectSpriteSource`.

- [ ] **Step 1: 실패 테스트** — API client 본문·메서드·경로·signal, hook 성공·409 무효화와 `retry:false`, 파서의 `source` 검증(누락·잘못된 후보 거부, null 허용)과 `sourceImageId: null`, parity(`SpritePhase`, `TaskKind`, `LlmOperationKind`).
- [ ] **Step 2: 구현·제거** — 위 Files. 사용처가 사라진 export는 함께 지운다.
- [ ] **Step 3: 검증** — `pnpm test && pnpm lint && pnpm typecheck`.
- [ ] **Step 4: Commit** — `feat(frontend): add sprite source draft client`

## Task 6: Frontend 화면·e2e

**선행 조건:** 2D 진행 표시에 기준 준비 단계를 넣은 목업(1440px·390px)을 사용자에게 보여 승인받는다. 승인 전에는 이 Task를 시작하지 않는다.

**Files:**

- Modify: `src/features/screens/sprites/SpriteInput.tsx`(프롬프트 모드 순서: 제작 설정 → 공급자·모델 → 장면 설명 → [보완하기], 성공 시 `spriteBackgroundJobPath(id)`; 이미지 모드 유지)
- Create: `src/features/screens/sprites/SpriteSourceDraftPanel.tsx`(설명·다시 보완, 프롬프트 편집·저장, 기준 이미지 생성, 후보 그리드 `sourceImageUrl(imageId)`, 이 이미지로 분석)
- Remove/Modify: `src/features/screens/sprites/SpritePromptPanel.tsx`(입력 화면 전용 설명 입력으로 축소하거나 제거, "이 화면을 유지해 주세요" 문구 제거), `spritePromptProgress.css`(사용처 없으면 제거)
- Modify: `src/features/screens/sprites/SpriteStudioScreen.tsx`(`sourceDraft`에서 초안 패널, 다른 패널 숨김), `SpriteProgress.tsx`(승인된 목업대로 기준 준비 단계)
- Test: `tests/e2e/spriteFakeApi.ts`(draft·source 명령·공정 상태 전이·후보, 기존 `/api/uploads/generate` 제거), `tests/e2e/sprites-static.spec.ts`(프롬프트 모드 테스트를 새 흐름으로 교체)
- Modify: `docs/xHuman/frontend.md`

**Interfaces:** Consumes Task 5 hook·타입.

- [ ] **Step 1: 실패 e2e** — (1) 설정·모델 먼저 선택 후 설명 입력·보완 → 작업 URL 이동, 보완 프롬프트 표시; (2) 프롬프트 수정·저장 → 생성 2회 → 후보 2장; (3) 생성 공정 실행 중 `page.reload()` → 진행 표시·기존 후보·비활성 버튼 유지; (4) 후보 선택 → 이 이미지로 분석 → `analyzing` 화면과 파일 업로드 0회; (5) 보완 실패 표시와 재시도, 후보 8장 후 생성 비활성, 409 응답 후 서버 상태로 갱신; (6) 1440×900·390px에서 긴 설명·긴 프롬프트가 넘치지 않음.
- [ ] **Step 2: 구현** — 기존 `JobProgressPanel`·`ModelSelect`·`ProviderSelect`·`spriteStyles` 재사용. 공정 실행 중 편집·생성·선택 비활성, 저장하지 않은 편집은 서버 갱신으로 덮어쓰지 않는다.
- [ ] **Step 3: 검증** — Frontend 전체 회귀 `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`, 초기 JS 예산 통과.
- [ ] **Step 4: Commit** — `feat(frontend): add sprite source draft flow`

## Task 7: 마무리 검증·문서

**Files:**

- Modify: `docs/xHuman/CODEMAP.md` 영역 표에 "2D 기준 준비" 작업 행(backend·frontend 지도 링크), 바뀐 각 지도의 확인 기준 날짜·revision
- Modify: 이 문서 실행 기록

- [ ] **Step 1** — 마지막 Backend 소스 변경 이후 Backend 전체 회귀, 마지막 Frontend 소스 변경 이후 Frontend 전체 회귀 결과를 실행 기록에 남긴다. 건너뜀·미수집은 통과로 세지 않는다.
- [ ] **Step 2** — `pnpm docs:check`, 바뀐 지도 링크·경로 확인.
- [ ] **Step 3: Commit** — `docs: record 2d source draft implementation`

## 실행 기록
