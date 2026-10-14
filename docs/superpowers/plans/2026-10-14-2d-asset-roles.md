# 2D 타일 작업 에셋 역할 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 타일 작업 한 개에 반복 타일·원경 배경·배치 오브젝트 역할을 두고, 역할별 투명도와 타일 기준 월드 축척 캔버스로 분석·생성·검수·내보내기 한다.

**Architecture:** 역할·격자·에셋 캔버스 계산은 `SpriteRules`의 순수 함수로 두고, `PipelineJob`이 계획 확정 때 에셋마다 캔버스를 계산해 `SpriteAsset`에 저장한다. 생성 입력·검증·내보내기는 작업 단위 캔버스 대신 에셋 캔버스를 쓴다(없으면 작업 단위 값). 분석 v3·생성 v2 프롬프트는 기존 개정 migration 패턴으로 넣는다.

**Tech Stack:** .NET 10, EF Core migration(SQL Server), xUnit, Testcontainers, SkiaSharp, React·TanStack Query, Vitest, Playwright.

**Spec:** [2026-10-14-2d-asset-roles-design.md](../specs/2026-10-14-2d-asset-roles-design.md)

## Global Constraints

- 역할: `Layer`, `Tile`, `Backdrop`, `Object`(JSON `layer`·`tile`·`backdrop`·`object`). 레이어 작업은 `Layer`만, 타일 작업은 `Tile`·`Backdrop`·`Object`만. `Backdrop`은 최대 1개이며 가장 작은 `Order`.
- 역할별 투명도: `Tile`은 아이소메트릭이면 필수, `Object`는 항상 필수, `Backdrop`은 금지(불투명), `Layer`는 기존 규칙(가장 뒤 제외 필수).
- 역할이 없는(`null`) 기존 계획은 유형 기본값(`Layers`→`Layer`, `Tiles`→`Tile`)으로 읽는다. 비교·검증은 항상 유효 역할(`SpriteRules.RoleOf`)로 한다.
- 격자 `cellWidth`는 원본 너비 대비 비율, `0 < cellWidth <= 1`. 없으면 `Tile` 에셋 `SourceBounds.W` 중앙값, `Tile`이 없으면 `Object`·`Backdrop`이 있는 계획은 "격자 칸 너비가 필요합니다"로 거부.
- 월드 축척 `s = tileWidth / (cellWidth × source.Width)`. `Object` 출력 = `round(bounds px × s)`, 한 변 1~1024가 아니면 계획 거부. `Backdrop` 출력 = `round(source × s)`, 긴 변 1024 초과면 1024로 맞추고 `Scale = 1024 / 긴 변(px × s)`. 나머지 `Scale = 1`. 반올림은 `MidpointRounding.AwayFromZero`.
- `Tile`·`Layer` 에셋 캔버스는 작업 단위 `OutputCanvas`·`GenerationCanvas`·`Transform`과 같다. `Object`·`Backdrop`의 생성 캔버스는 `SpriteRules.GenerationCanvas(output, 지원 크기)`, transform은 `SpriteRules.Transform`.
- 지원 크기 조회 실패는 계획 확정 실패다. 기본 크기로 대체하지 않는다.
- 새 에셋 앵커: `Layer`·`Backdrop`은 `(0,0)`, `Tile`·`Object`는 출력 캔버스 중심.
- 다이아몬드 clip·`Diamond` layout은 아이소메트릭 `Tile`에만.
- 새 오류 코드를 만들지 않는다. 계획 위반은 `SpritePlanInvalid` 메시지로 구분한다.
- 시드: 기존 seed·migration 수정 금지. 개정은 슬롯 비활성화 후 `ISNULL(MAX([Version]),0)+1`, Down은 새 행 삭제 후 최고 버전 재활성화.
- 모든 새 DB 열은 nullable. 기존 JSON(계획·승인·내보내기 입력)은 새 필드 없이 읽혀야 한다.
- 검증은 Fake 공급자. 실제 AI·유료 smoke 금지(구현 후 사용자 승인 시 1회 별도).
- 각 Task 커밋은 빌드·관련 테스트 통과. `docs/xHuman` 수정은 해당 Task 커밋. 주석은 `karpathy-guidelines`의 `## Comments`.

## Review Focus

1. 기존 작업 호환: 역할·에셋 캔버스·격자가 없는 행과 기존 승인·내보내기 JSON을 열고 검수·내보내기 하면 이전과 같은 결과다 → Task 2 영속성 테스트, Task 5 기존 입력 ZIP, Task 7 구형 응답 파서.
2. 기존 작업 역할 변경: 격자 없는 기존 타일 작업에서 한 에셋을 `Object`로 바꾸면 중앙값 격자로 캔버스가 계산되고 그 에셋만 무효화된다. 모든 에셋을 `Object`로 바꾸면 격자 필요 오류다 → Task 1·2 테스트.
3. 격자 수정으로 오브젝트가 1024px를 넘으면 계획이 거부되고 화면 해당 행에 오류가 보인다 → Task 1 테스트, Task 8 e2e.
4. 분석 결과 반영 중 지원 크기 조회 실패: 공정은 재시도로 분류되고 계획이 기본 크기로 저장되지 않는다 → Task 2 테스트.
5. 혼합 작업 내보내기: 역할별 경로, 에셋 캔버스 기준 크기 검사·시트 분할, 크기 불일치 거부 → Task 5 테스트.

## 실행 방식

**Subagent-driven.** Task 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 순서(앞 Task의 계약을 뒤 Task가 소비). 작업마다 새 구현 에이전트와 별도 리뷰 에이전트, 마지막에 전체 변경 리뷰와 양쪽 스택 전체 회귀.

공통 명령(저장소 루트):

```bash
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~<TestClass>'
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'
dotnet ef migrations add <Name> --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure
dotnet ef migrations has-pending-model-changes --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure
pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e
pnpm docs:check
```

Playwright 기본 포트 4173을 무관한 프로세스가 점유하고 있으면 종료하지 말고 포트만 바꾼 임시 config로 실행한 뒤 삭제한다.

## Task 1: 역할·격자·에셋 캔버스 규칙

**Files:**

- Modify: `apps/backend/Noxtend.Domain/Sprites/SpriteTypes.cs` — `SpriteAssetRole`, `SpriteGrid`, `SpriteAssetCanvas`, `SpriteAssetPlan.Role`
- Modify: `apps/backend/Noxtend.Domain/Sprites/SpriteRules.cs` — 아래 함수, `Validate`에 격자·역할 규칙
- Test: `apps/backend/Noxtend.Tests/Domain/SpriteRulesTests.cs`

**Interfaces (Produces):**

```csharp
public enum SpriteAssetRole { Layer, Tile, Backdrop, Object }
public sealed record SpriteGrid(double CellWidth);
public sealed record SpriteAssetCanvas(SpriteCanvas Output, SpriteCanvas Generation, SpriteTransform Transform, double Scale);
public sealed record SpriteAssetPlan(Guid Id, string Name, int Order, Bounds SourceBounds,
    bool RequiresTransparency, bool Loop = false, int FrameCount = 8, int Fps = 8,
    string MotionNotes = "", SpriteAssetRole? Role = null);

SpriteRules.RoleOf(SpriteSettings settings, SpriteAssetPlan plan) : SpriteAssetRole
SpriteRules.CellWidth(SpriteGrid? grid, IReadOnlyList<SpriteAssetPlan> plans, SpriteSettings settings) : double?
    // grid 값 → Tile W 중앙값 → null
SpriteRules.AssetOutput(SpriteSettings settings, SpriteCanvas source, double? cellWidth, SpriteAssetPlan plan)
    : Result<(SpriteCanvas Canvas, double Scale)>
SpriteRules.AssetCanvas(SpriteSettings settings, SpriteCanvas source, SpriteCanvas jobGeneration,
    double? cellWidth, SpriteAssetPlan plan, IReadOnlyList<SpriteCanvas> generationSizes) : Result<SpriteAssetCanvas>
    // Tile·Layer: (OutputCanvas(settings, source), jobGeneration, Transform(jobGeneration, output), 1)
SpriteRules.Validate(SpriteSettings settings, SpriteCanvas source,
    IReadOnlyList<SpriteAssetPlan> assets, SpriteGrid? grid = null) : Result<bool>
```

- [ ] **Step 1: 실패 테스트** — `RoleOf`: null 역할 기본값 두 유형, 명시 역할 유지. `Validate`: 레이어 작업의 `Tile`·`Object` 거부, 타일 작업의 `Layer` 거부, `Backdrop` 2개 거부, `Backdrop`이 가장 뒤가 아니면 거부, `Object` 투명 false 거부, `Backdrop` 투명 true 거부, 비아이소 `Tile` 투명 false 허용, `grid.CellWidth` 0·1.01·NaN 거부, `Tile` 없이 `Object`만 있고 grid null이면 "격자 칸 너비가 필요합니다". `AssetOutput`: 원본 1536×1024·cellWidth 128/1536·tileWidth 128에서 `Object` bounds(0.1,0.1,0.2,0.25) → 307×256·Scale 1; 같은 조건 `Backdrop` → 1024×683·Scale 1024/1536; `Object`가 1025px이 되는 경계에서 실패 메시지에 "오브젝트"; `CellWidth` 중앙값(짝수 개면 두 중앙값 평균). `AssetCanvas`: `Tile`은 작업 단위 값과 같음, `Object`는 지원 크기 중 비율이 가장 가까운 생성 캔버스와 contain transform. 기존 `Validate_EnforcesTransparencyBackToFront`와 아이소메트릭 타일 사례는 그대로 통과해야 한다.
- [ ] **Step 2: 실패 확인** — `dotnet test ... --filter 'FullyQualifiedName~SpriteRulesTests'`가 새 테스트에서 컴파일 오류 또는 단정 실패.
- [ ] **Step 3: 구현** — Interfaces. 투명도 규칙은 `RoleOf` 기준으로 다시 쓰고 기존 메시지 "앞쪽 레이어와 다이아몬드 타일은 투명이 필요합니다"는 `Layer`·`Tile` 위반에 유지, `Object`·`Backdrop`은 별도 메시지. `Validate`의 격자 확인은 `Object`·`Backdrop`이 있을 때만 `AssetOutput`으로 크기까지 검사한다.
- [ ] **Step 4: 통과 확인** — `SpriteRulesTests`, `SpriteLifecycleTests`, `SpriteAnalysisTests` 통과(기존 호출부는 grid 생략으로 컴파일 유지).
- [ ] **Step 5: Commit** — `feat(sprites): add asset role and world-scale canvas rules`

## Task 2: 에셋 캔버스 상태·영속성·계획 확정

**Files:**

- Modify: `apps/backend/Noxtend.Domain/Sprites/SpritePipelineState.cs` — `SpritePipelineState.Grid`·`ReplaceGrid`, `SpriteAsset.Canvas`·`UpdateCanvas`
- Modify: `apps/backend/Noxtend.Domain/Sprites/SpriteInputs.cs` — `SpriteApprovedAsset`에 `Role`·`Canvas`·`Scale`·`SourceBounds`(기본값 있는 뒤쪽 매개변수), `SpriteExportInput`에 `Grid`·`TileWidth`(기본값)
- Modify: `apps/backend/Noxtend.Domain/Job/PipelineJob.Sprites.cs` — `ReplaceSpritePlan` 서명, `GenerationChanged`(유효 역할), 에셋 캔버스 변경을 affected로, 새 에셋 앵커, `SpriteInput`·`MatchesSpriteInput`(에셋 캔버스), `SpriteApproval`(역할·캔버스·Scale·SourceBounds, `Diamond`는 아이소메트릭 `Tile`만), `CaptureSpriteExport`(SchemaVersion 2, Grid, TileWidth), `SameApprovedAsset`·`SameExportInput`
- Create: `apps/backend/Noxtend.Application/Sprites/SpriteGenerationSizes.cs`
- Modify: `apps/backend/Noxtend.Application/Sprites/SpriteCommandsHandler.cs` — `UpdatePlanAsync(context, plans, grid, ct)`와 생성 명령 capability 확인이 `SpriteGenerationSizes` 사용
- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpriteAnalysisTaskHandler.cs` — 지원 크기를 조회해 `ReplaceSpritePlan`에 전달(grid는 Task 3까지 null), 조회 실패는 `ProviderCallFailedException`(transient)로 재시도
- Modify: `apps/backend/Noxtend.Infrastructure/Persistence/Configurations/SpritePipelineConfiguration.cs` — `SpriteGridJson`(Jobs), `CanvasJson`(SpriteAssets)
- Modify: `apps/backend/Noxtend.Infrastructure/Persistence/SpriteJsonSerializer.cs` — 새 필드 없는 기존 JSON 허용, 값이 있으면 검증
- Create: migration `AddSpriteAssetRoles`(nullable `Jobs.SpriteGridJson`, `SpriteAssets.CanvasJson`)
- Modify(컴파일 유지): `Noxtend.Api` 계획 명령 호출부는 grid `null` 전달(계약 변경은 Task 6)
- Test: `Noxtend.Tests/Domain/SpriteLifecycleTests.cs`, `Application/SpriteCommandTests.cs`, `Application/SpriteAnalysisTests.cs`, `Infrastructure/SpritePersistenceTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces:**

- Consumes: Task 1 전체.
- Produces:

```csharp
// SpritePipelineState
public SpriteGrid? Grid { get; }
// SpriteAsset
public SpriteAssetCanvas? Canvas { get; }                 // null = 작업 단위 캔버스
PipelineJob.ReplaceSpritePlan(IReadOnlyList<SpriteAssetPlan> plans, SpriteGrid? grid,
    IReadOnlyList<SpriteCanvas> generationSizes, int expectedRevision) : Result<bool>
PipelineJob.SpriteAssetCanvasOf(SpriteAsset asset) : SpriteAssetCanvas   // Canvas ?? 작업 단위 값
public sealed record SpriteApprovedAsset(Guid Id, string Name, int Order, int Fps, bool Loop, SpriteAnchor Anchor,
    SpriteRepeat Repeat, SpriteTileLayout Layout, Guid BaseImageId, IReadOnlyList<Guid> ImageIds,
    SpriteAssetRole? Role = null, SpriteCanvas? Canvas = null, double Scale = 1, Bounds? SourceBounds = null);
public sealed record SpriteExportInput(int SchemaVersion, Guid ExportId, int ReviewRevision, SpriteView View,
    SpriteOutputKind OutputKind, SpriteCanvas SourceCanvas, SpriteCanvas OutputCanvas,
    IReadOnlyList<SpriteApprovedAsset> Assets, IReadOnlyList<Guid> ExcludedAssetIds,
    SpriteGrid? Grid = null, int? TileWidth = null);
public static class SpriteGenerationSizes
{
    // 활성 공급자·모델의 투명 지원과 양수 크기 확인
    public static Task<Result<IReadOnlyList<SpriteCanvas>>> ResolveAsync(PipelineJob job,
        IProviderConfigRepository providers, IModelCatalog catalog, CancellationToken ct);
}
SpriteCommandsHandler.UpdatePlanAsync(SpriteCommandContext context, IReadOnlyList<SpriteAssetPlan> plans,
    SpriteGrid? grid, CancellationToken ct)
```

- [ ] **Step 1: 실패 테스트(도메인)** — 계획 확정이 `Object`·`Backdrop` 에셋에 `Canvas`를 저장하고 `Tile`은 작업 단위와 같은 값; `Object` 생성 입력(`SpriteFrameInput.Canvas`·`GenerationCanvas`·`Transform`)이 에셋 캔버스; 역할만 `Tile`→`Object`로 바꾸면 그 에셋만 `PlanRevision` 증가·이미지 무효화·`PlanReview`; 격자 변경은 `Object`·`Backdrop`만 무효화하고 `Tile` 이미지 유지; 이름만 바꾸면 무효화 없음(기존 단정 유지); 새 `Object` 앵커가 캔버스 중심, `Backdrop`은 (0,0); 승인 스냅숏에 역할·캔버스·Scale·SourceBounds, 아이소메트릭 `Object`의 `Layout`은 `Square`; `CaptureSpriteExport`가 SchemaVersion 2·Grid·TileWidth; 격자 없는 기존 타일 작업(Review Focus 2) 역할 변경 시 중앙값 격자 사용, 모든 에셋 `Object`면 격자 필요 오류.
- [ ] **Step 2: 실패 테스트(Application)** — `UpdatePlanAsync`가 지원 크기를 조회해 전달하고, 공급자 비활성·모델 없음·크기 없음이면 `JobImageModelUnavailable` 계열로 실패하고 계획이 바뀌지 않음. 분석 공정에서 크기 조회 실패 시 Retry로 분류되고 계획 미저장(Review Focus 4).
- [ ] **Step 3: 실패 테스트(영속성)** — 역할·격자·에셋 캔버스·v2 승인·내보내기 입력 왕복; 새 열이 null인 기존 행과 `Role`·`Canvas` 없는 기존 `PlanJson`·`ApprovalJson`·내보내기 입력 JSON을 읽으면 `Role` null·`Canvas` null·`Scale` 1(Review Focus 1).
- [ ] **Step 4: 구현** — Files 전체. `SpriteGenerationSizes`는 `SpriteCommandsHandler`의 기존 capability 조건(활성 공급자, `SupportsTransparency`, 양수 크기)을 옮긴다. 생성 명령의 "작업 생성 캔버스가 지원 크기에 있음" 확인은 유지한다. migration 생성 후 `has-pending-model-changes`가 변경 없음.
- [ ] **Step 5: 검증** — `SpriteLifecycleTests`·`SpriteRulesTests`·`SpriteCommandTests`·`SpriteAnalysisTests`·`SpriteGenerationTests`·`SpritePersistenceTests`·`SpriteRequestConcurrencyTests`·`SpriteApiTests` 통과, `dotnet build` 오류 0.
- [ ] **Step 6: Commit** — `feat(sprites): store per-asset canvases and grid`

## Task 3: 분석 v3 프롬프트와 파서

**Files:**

- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `AnalyzeSpritesV3()`(기존 V1·V2 유지)
- Create: migration `ReviseSpriteAnalyzePromptV3`(새 고정 Id, Background 슬롯 개정)
- Modify: `apps/backend/Noxtend.Application/Sprites/SpritePlanParser.cs` — v3 schema(`role` 필수, 루트 `grid` 필수: 타일 작업 `{cellWidth}`, 레이어 작업 `null`), 반환에 grid
- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpriteAnalysisTaskHandler.cs` — 파싱된 grid 전달
- Modify: `apps/backend/Noxtend.Infrastructure/Llm/FakeLlmProvider.cs` — `SpriteJson`: 타일 작업은 `tile`·`backdrop`·`object` 각 1개와 `grid`, 레이어 작업은 `layer`와 `grid: null`
- Test: `Application/SpriteAnalysisTests.cs`, `Infrastructure/SpriteAnalyzePromptMigrationTests.cs`(V3 사례 추가), `Infrastructure/SeedPromptVariableTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`

**Interfaces:**

- Consumes: Task 2 `ReplaceSpritePlan(plans, grid, sizes, revision)`.
- Produces:

```csharp
public sealed record SpriteAnalysisPlan(IReadOnlyList<SpriteAssetPlan> Assets, SpriteGrid? Grid);
SpritePlanParser.Parse(string json, SpriteSettings settings, SpriteCanvas source) : Result<SpriteAnalysisPlan>
SeedPrompts.AnalyzeSpritesV3() : (string System, string User, string Schema, string Note)
```

- [ ] **Step 1: 실패 테스트** — 파서: 역할 누락·알 수 없는 역할·유형에 없는 역할·`backdrop` 2개·역할과 투명도 불일치·타일 작업 grid 누락·레이어 작업 grid 값 존재가 모두 `SpritePlanInvalid`; 정상 v3 JSON이 역할·grid를 그대로 반환(값을 고치지 않음). Fake 기본 분석이 타일 작업에서 세 역할을 만들고 계획 검증 통과. migration: 활성 행이 V3, 버전 `MAX+1`(운영자 v9 → 10), 다른 슬롯 불변, Down 복원. V3 변수가 허용 변수 안.
- [ ] **Step 2: 구현** — V3 System은 V2 정규화 좌표 안내를 유지하고 spec "프롬프트" 절의 분류 규칙·반례·`cellWidth` 정의와 예시를 추가한다. Schema는 V2에 `role` enum(`layer`,`tile`,`backdrop`,`object`)과 루트 `grid`(`oneOf` null 또는 `{cellWidth: number}`)를 더한다.
- [ ] **Step 3: 검증** — 위 테스트와 `SpriteRulesTests`·`SpriteLifecycleTests`, migration 테스트(Testcontainers) 통과.
- [ ] **Step 4: Commit** — `feat(sprites): analyze sprite asset roles and grid`

## Task 4: 역할별 생성 요청·정규화와 생성 프롬프트 개정

**Files:**

- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpriteGenerationTaskHandler.cs` — 배경 요청은 계획 투명도(역할 규칙으로 이미 결정), `layout`은 아이소메트릭 `Tile`만 `Diamond`, `frame.anchor`는 역할 기준(`Layer`·`Backdrop` (0,0), 그 외 출력 중심)
- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `GenerateSpriteV2()`
- Create: migration `ReviseSpriteGeneratePrompt`(GenerateSprite Background 슬롯 첫 개정)
- Test: `Application/SpriteGenerationTests.cs`, `Application/SpriteProviderRequestTests.cs`, `Infrastructure/SpritePixelTests.cs`, 새 `Infrastructure/SpriteGeneratePromptMigrationTests.cs`(`SpriteAnalyzePromptMigrationTests` 패턴), `SeedPromptVariableTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`

**Interfaces:** Consumes Task 2 에셋 캔버스(`SpriteFrameInput`에 이미 고정). Produces `SeedPrompts.GenerateSpriteV2()`.

- [ ] **Step 1: 실패 테스트** — 아이소메트릭 타일 작업의 `Object` 요청이 에셋 생성 캔버스 크기 문자열·`Transparent`, `Backdrop`은 `Opaque`, `Object` 정규화 layout `Square`(다이아몬드 clip 없음, 모서리 픽셀 보존), `Tile`은 기존과 같음; 변수 `asset`에 `role`, `outputCanvas`에 에셋 캔버스; 투명 픽셀 없는 `Object` 결과는 재시도 분류. migration은 Task 3과 같은 네 단정.
- [ ] **Step 2: 구현** — V2 System을 역할별 문단으로 나눈다(`tile` 기존 지침, `backdrop` 캔버스를 채우는 불투명 원경·전경 물체 금지, `object` 투명 배경 위 물체 하나·잘림 없음·원본 시점과 조명 유지, `layer` 기존 지침). 변수 목록은 바꾸지 않는다.
- [ ] **Step 3: 검증** — 위 테스트와 `SpriteLifecycleTests` 통과.
- [ ] **Step 4: Commit** — `feat(sprites): generate sprites by asset role`

## Task 5: manifest v2 내보내기

**Files:**

- Modify: `apps/backend/Noxtend.Application/Sprites/SpritePackageWriter.cs` — 에셋 캔버스(`asset.Canvas ?? input.OutputCanvas`)로 크기 검사·시트 분할, 경로 `tiles/`·`objects/`·`backdrop/`·`layers/`(유효 역할), manifest `SchemaVersion`은 `input.SchemaVersion`
- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpritePackTaskHandler.cs`(필요 시)
- Test: `Infrastructure/SpritePackageTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces:** Consumes Task 2 `SpriteApprovedAsset.Role/Canvas/Scale/SourceBounds`, `SpriteExportInput.Grid/TileWidth`.

- [ ] **Step 1: 실패 테스트** — 혼합 작업(타일 128×64, 오브젝트 307×256, 원경 1024×683) ZIP: 역할별 기본 경로, 시트 셀 크기가 에셋 캔버스, manifest에 역할·캔버스·scale·sourceBounds·grid·tileWidth, schemaVersion 2; 오브젝트 이미지 크기가 캔버스와 다르면 거부(Review Focus 5); SchemaVersion 1 기존 입력은 이전과 같은 경로(`layers/`·`tiles/`)와 manifest(Review Focus 1). 기존 `Zip_ContainsManifestBasesFramesAndSheets` 단정 유지.
- [ ] **Step 2: 구현** — Files.
- [ ] **Step 3: 검증** — `SpritePackageTests`, `SpriteCommandTests` 통과.
- [ ] **Step 4: Commit** — `feat(sprites): export role-aware sprite manifest v2`

## Task 6: API 계약

**Files:**

- Modify: `apps/backend/Noxtend.Api/Contracts/SpriteContracts.cs` — `SpriteAssetPlanRequest.Role`(선택, camelCase 문자열), `SpritePlanRequest.Grid`(선택), `SpriteResponse.Grid`, `SpriteAssetResponse.Canvas`·`Scale`, `SpriteApprovedAssetResponse` 역할·캔버스·scale
- Modify: `apps/backend/Noxtend.Api/Controllers/SpriteJobsController.cs` — 계획 수정에 grid 전달, 잘못된 역할 400
- Test: `Api/SpriteApiTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces:**

- Consumes: Task 2 `UpdatePlanAsync(context, plans, grid, ct)`.
- Produces(JSON): plan asset `role?: 'layer'|'tile'|'backdrop'|'object'`(응답은 항상 유효 역할), plan 요청 `grid?: { cellWidth: number } | null`, sprite 응답 `grid: { cellWidth } | null`, asset 응답 `canvas: { width, height }`·`scale: number`, approval snapshot `role`·`canvas`·`scale`.

- [ ] **Step 1: 실패 테스트** — 새 필드 직렬화; 요청 `role` 생략 시 유형 기본값으로 저장; `role: "unknown"` 400; 레이어 작업에 `object` 400(`SPRITE_PLAN_INVALID`); 기존 작업 응답의 `role`이 기본값·`canvas`가 작업 단위 값·`scale` 1; 알 수 없는 필드는 기존처럼 400.
- [ ] **Step 2: 구현** — Files.
- [ ] **Step 3: 검증** — `SpriteApiTests`, `JsonBinding_*` 통과, Backend 전체 회귀(유료 smoke 제외).
- [ ] **Step 4: Commit** — `feat(api): expose sprite asset roles and canvases`

## Task 7: Frontend 도메인·API·Fake parity

**Files:**

- Modify: `apps/frontend/src/domain/sprites/types.ts` — `SpriteAssetRole`, `SpriteGrid`, `SpriteAssetPlan.role`, `SpriteAsset.canvas`·`scale`, `SpriteState.grid`, `SpriteApprovedAsset` 역할·캔버스·scale, `readPlan`·`readState`·`readApprovedAsset`
- Modify: `apps/frontend/src/domain/sprites/rules.ts` — `assetRole(settings, plan)`, `cellWidthOf(grid, plans)`, `assetOutputSize(settings, source, cellWidth, plan)`(서버와 같은 반올림·1024 규칙, 결과 또는 오류 문구)
- Modify: `apps/frontend/src/domain/sprites/labels.ts` — `spriteRoleLabel`: `tile`=반복 타일, `object`=오브젝트, `backdrop`=원경 배경, `layer`=배경 레이어
- Modify: `apps/frontend/src/infra/api/spriteApi.ts` — `updateSpritePlan` 본문에 `role`·`grid`
- Modify: `apps/frontend/src/app/queries/useSprites.ts`(입력 타입 전달만)
- Modify: `apps/frontend/tests/e2e/spriteFakeApi.ts` — 역할별 투명도·캔버스·앵커·경로·manifest v2, 타일 작업 seed에 세 역할
- Test: `src/domain/sprites/rules.test.ts`, `src/domain/sprites/labels.test.ts`, `src/infra/api/spriteApi.test.ts`, `src/app/queries/useSprites.test.ts`

**Interfaces (Produces):**

```ts
export type SpriteAssetRole = 'layer' | 'tile' | 'backdrop' | 'object'
export interface SpriteGrid { cellWidth: number }
// SpriteAssetPlan.role: SpriteAssetRole  (구형 응답 누락 시 유형 기본값으로 채워 읽음)
// SpriteAsset.canvas: SpriteCanvas, SpriteAsset.scale: number, SpriteState.grid: SpriteGrid | null
export function assetRole(settings: SpriteSettings, plan: Pick<SpriteAssetPlan, 'role'>): SpriteAssetRole
export function cellWidthOf(grid: SpriteGrid | null, plans: readonly SpriteAssetPlan[], settings: SpriteSettings): number | null
export function assetOutputSize(settings: SpriteSettings, source: SpriteCanvas, cellWidth: number | null,
  plan: SpriteAssetPlan): { canvas: SpriteCanvas; scale: number } | { error: string }
export function spriteRoleLabel(role: SpriteAssetRole): string
// updateSpritePlan(input: SpriteMutationContext & { assets: SpriteAssetPlan[]; grid: SpriteGrid | null })
```

- [ ] **Step 1: 실패 테스트** — 새 필드 읽기, 알 수 없는 역할 거부, 구형 응답(역할·canvas·grid 없음) 기본값 읽기(Review Focus 1), `assetOutputSize`가 Task 1과 같은 수치 사례(307×256, 1024×683·scale, 1025 경계 오류), 요청 본문에 허용 필드만(`role`·`grid` 포함).
- [ ] **Step 2: 구현** — Files. Fake는 서버 규칙을 그대로 따른다.
- [ ] **Step 3: 검증** — `pnpm test && pnpm typecheck`, 기존 sprite e2e 통과.
- [ ] **Step 4: Commit** — `feat(frontend): read sprite asset roles and canvases`

## Task 8: Frontend 화면

**Files:**

- Modify: `apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx` — 타일 작업 행에 역할 select(라벨 `spriteRoleLabel`), 투명도 체크박스 읽기 전용(역할 결정), 격자 칸 너비 입력, 행별 `출력 W×Hpx`, 클라이언트 검증 오류 행 표시, 오버레이 라벨에 역할
- Modify: `apps/frontend/src/features/screens/sprites/SpritePreview.tsx` — 에셋 단위 분기(`tile` 3×3, `object`·`backdrop` 단일 이미지·자기 캔버스)
- Modify: `apps/frontend/src/features/screens/sprites/SpriteFrameReview.tsx` — 역할 배지
- Modify: `apps/frontend/src/features/screens/sprites/SpriteAnalysisPanel.tsx` — 역할별 수 요약(`spriteAnalysisSummary`)
- Test: `src/features/screens/sprites/SpriteAnalysisPanel.test.ts`, `tests/e2e/sprites-static.spec.ts`(또는 새 `sprites-roles.spec.ts`)
- Modify: `docs/xHuman/frontend.md`

**Interfaces:** Consumes Task 7 전체.

- [ ] **Step 1: 실패 테스트** — 단위: 요약 `반복 타일 1 · 오브젝트 1 · 원경 배경 1`. e2e(Fake): 타일 작업 계획 검수에 세 역할과 출력 크기 표시 → 계획 승인 → 생성 → 검수(역할 배지) → 내보내기 ZIP manifest v2; 한 에셋 역할 변경 시 그 에셋만 다시 생성 대기; 격자 칸 너비를 줄여 오브젝트가 1024px를 넘으면 해당 행 오류와 승인 비활성(Review Focus 3); 레이어 작업 화면은 역할 select 없음; 390px 가로 넘침 없음.
- [ ] **Step 2: 구현** — Files. 역할 select·격자 입력은 명시적 라벨과 키보드 조작, 오류는 텍스트로 전달.
- [ ] **Step 3: 검증** — Frontend 전체 회귀(`pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`).
- [ ] **Step 4: Commit** — `feat(frontend): edit and review sprite asset roles`

## Task 9: 문서와 전체 회귀

**Files:**

- Modify: `DESIGN.md`(2D 계획 검수 역할 선택·출력 크기), `PRODUCT.md`(2D 배경 결과 설명에 타일 작업의 역할), `docs/superpowers/specs/2026-10-06-2d-background-sprites-design.md`(“한 작업은 레이어 또는 타일 중 한 결과 유형” 문장 옆 이 설계 링크), `docs/xHuman/CODEMAP.md`(필요 시)
- Modify: 이 계획의 실행 기록

- [ ] **Step 1:** 문서 갱신 후 `pnpm docs:check`.
- [ ] **Step 2:** Backend 전체 회귀(유료 smoke 제외), `has-pending-model-changes` 변경 없음, Frontend 전체 회귀. 결과 수치를 실행 기록에 남긴다.
- [ ] **Step 3: Commit** — `docs: record 2d asset roles implementation`

## 실행 기록

(진행하며 기록)
