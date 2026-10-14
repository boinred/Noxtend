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
- 도메인에 들어가는 계획(계획 수정·분석 반영)은 `SpriteRules.NormalizeRole`로 기본 역할을 null로 바꾼다. fingerprint도 같은 함수를 쓴다. `ReplaceSpritePlan`의 record 동등성 비교가 명시적 기본 역할과 null을 다르게 보아 승인을 해제하지 않게 하기 위함이다.
- 격자 `cellWidth`는 원본 너비 대비 비율, `0 < cellWidth <= 1`. 없으면 `Tile` 에셋 `SourceBounds.W` 중앙값, `Tile`이 없으면 `Object`·`Backdrop`이 있는 계획은 "격자 칸 너비가 필요합니다"로 거부.
- 월드 축척 `s = tileWidth / (cellWidth × source.Width)`. `Object` 출력 = `round(bounds px × s)`, 한 변 1~1024가 아니면 계획 거부. `Backdrop` 출력 = `round(source × s)`, 긴 변 1024 초과면 1024로 맞추고 `Scale = 1024 / 긴 변(px × s)`. 나머지 `Scale = 1`. 반올림은 `MidpointRounding.AwayFromZero`.
- `Tile`·`Layer` 에셋 캔버스는 작업 단위 `OutputCanvas`·`GenerationCanvas`·`Transform`과 같다. `Object`·`Backdrop`의 생성 캔버스는 `SpriteRules.GenerationCanvas(output, 지원 크기)`, transform은 `SpriteRules.Transform`.
- 지원 크기 조회 실패는 계획 확정 실패다. 기본 크기로 대체하지 않는다.
- 신규·변경 에셋 앵커: `Layer`·`Backdrop`은 `(0,0)`, `Tile`·`Object`는 출력 캔버스 중심. 역할·출력 크기가 바뀌면 저장 앵커도 갱신한다.
- 앵커 계산은 `SpriteRules.Anchor(role, outputCanvas)` 하나만 쓴다(도메인 저장·생성 핸들러 `frame.anchor`). 앵커는 역할·출력 캔버스 변경 때만 바뀌므로 승인 snapshot 앵커는 항상 저장 앵커와 같다.
- 출력 캔버스가 같은 에셋은 저장된 생성 캔버스·transform을 재사용한다. 지원 크기 조회는 출력 캔버스가 새로 생기거나 바뀐 `Object`·`Backdrop`이 있을 때만 한다.
- 계획 수정 fingerprint는 작업의 불변 OutputKind를 읽은 뒤 정규화 본문으로 계산한다. 정규화는 기본 역할을 null로 두고, grid는 생략과 null을 같게 보며, 에셋을 ID 순으로 정렬한다. 기존 receipt 비교·반환은 revision·취소·모델 상태 검사보다 우선한다. 레이어 작업의 grid 값은 `SpritePlanInvalid`.
- hash는 하나만 계산한다. 직렬화에서 null `Role`만 생략하고, body는 grid가 null이면 기존 에셋 배열, 값이 있으면 `{ plans, grid }`로 둔다. 그러면 기본값 요청은 배포 전과 같은 바이트가 되므로 구형 계산 분기를 두지 않는다. 다른 명령의 계산 방식은 바꾸지 않는다.
- 설계의 불변식 I1(현재 ZIP)·I2(승인)·I3(역할 정규화)에서 벗어나는 예외 규칙을 구현 중에 추가하지 않는다. 새 경계 사례는 불변식으로 판정하고, 판정이 안 되면 설계부터 고친다.
- 내보내기 현재 판정은 기존 비교에 v2만 입력 `Grid` = 현재 유효 격자를 더한다(I1). v1은 기존 판정이다. 격자를 되돌려 옛 공정이 다시 현재가 되는 것은 허용한다. `CompletedExportId`는 해제하지 않고, 응답 `completedExportId`는 그 export가 현재일 때만 낸다.
- 생성 명령의 사용 가능 검사는 대상 에셋 생성 캔버스(`SpriteAssetCanvasOf(...).Generation`)를 지원 크기에서 확인한다. 대상은 계획 승인이면 전체, 기준 승인이면 요청 ID, 재생성이면 해당 에셋이다. 작업 단위 생성 캔버스 검사를 이것으로 대체한다.
- 다이아몬드 clip·`Diamond` layout은 아이소메트릭 `Tile`에만.
- 새 오류 코드를 만들지 않는다. 계획 위반은 `SpritePlanInvalid` 메시지로 구분한다.
- 시드: 기존 seed·migration 수정 금지. 개정은 슬롯 비활성화 후 `ISNULL(MAX([Version]),0)+1`, Down은 새 행 삭제 후 최고 버전 재활성화.
- 모든 새 DB 열은 nullable. 기존 JSON(계획·승인·내보내기 입력)은 새 필드 없이 읽혀야 한다.
- 승인 snapshot(`SpriteApprovedAsset`)에는 새 필드를 두지 않는다. 내보내기 접수가 현재 에셋 상태에서 역할·캔버스·Scale·SourceBounds를 읽어 v2 입력의 `SpriteExportAsset` 목록에 둔다(I2). 구형 승인 보완 규칙은 없다. `SameExportInput`은 새 필드까지 엄격히 비교한다.
- 저장 envelope는 v1을 유지하고 내보내기 입력·manifest의 v1/v2를 별도 검증한다. 신규 내보내기 v2 활성화는 writer·저장 검증과 함께 Task 5에서 수행한다.
- 격자 변경은 계획 목록 변경과 별도로 저장·revision에 반영한다. 캔버스가 같은 에셋의 이미지·승인은 유지하고, 작업 수준 격자가 바뀐 기존 ZIP은 모두 이전 결과로 표시한다.
- 생성 결과 검증 오류는 기존 `Fail(ProviderBadResponse)` 유지. 분석 schema 오류의 Retry와 구분하고 자동 이미지 재호출을 추가하지 않는다.
- 검증은 Fake 공급자. 실제 AI·유료 smoke 금지(구현 후 사용자 승인 시 1회 별도).
- 각 Task 커밋은 빌드·관련 테스트 통과. `docs/xHuman` 수정은 해당 Task 커밋. 주석은 `karpathy-guidelines`의 `## Comments`.

## Review Focus

1. 기존 작업 호환: 구형 행·승인을 읽고, 새 내보내기는 현재 에셋 상태로 v2를 만들되 이미지·승인값은 유지한다. 접수된 v1 공정은 고정 입력·기존 manifest 형태를 유지한다 → Task 2 영속성, Task 5 두 내보내기 경로·v2 manifest 재조회, Task 7 구형 응답 파서.
2. 기존 작업 역할 변경: 격자 없는 기존 타일 작업에서 한 에셋을 `Object`로 바꾸면 중앙값 격자로 캔버스가 계산되고 그 에셋만 무효화된다. 모든 에셋을 `Object`로 바꾸면 격자 필요 오류다 → Task 1·2 테스트.
3. 격자 수정으로 오브젝트가 1024px를 넘으면 계획이 거부되고 화면 해당 행에 오류가 보인다 → Task 1 테스트, Task 8 e2e.
4. 분석 결과 반영 중 지원 크기 조회 실패: 공정은 재시도로 분류되고 계획이 기본 크기로 저장되지 않는다 → Task 2 조회 조건·Task 3 혼합 분석 테스트.
5. 혼합 작업 내보내기: 역할별 경로, 에셋 캔버스 기준 크기 검사·시트 분할, 크기 불일치 거부 → Task 5 테스트.

2026-10-14 리뷰 보완 추적:

| 항목 | 구현·검증 위치 |
|---|---|
| 개별 저장값 검증의 Layers 고정 가정 제거 | Task 2 저장 검증·세 역할 SQL 왕복 |
| grid 포함 fingerprint·중복 접수 우선 | Task 2 Application·요청 경합 테스트 |
| 격자만 변경 시 저장·revision·ZIP·draft 처리 | Task 2 도메인, Task 8 E2E |
| 기존 에셋 앵커 갱신 | Task 2 도메인, Task 4 프롬프트, Task 5 manifest |
| 구형 승인 → 새 v2, 접수 v1 유지, v2 활성화 시점 | Task 5 및 Task 2 준비 계약 |
| 투명도 불량의 기존 즉시 실패 유지 | Task 4 오류 분류·공급자 호출 수 |
| 실제 서버 JSON → 실제 Frontend reader | Task 6 fixture 생성, Task 7 연결 검사 |
| 현재 ZIP 불변식(v2 Grid 비교), `CompletedExportId` 미해제·응답 현재 export만 | Task 2 Step 1b, Task 5 Step 1b, Task 6 Step 1 |
| 승인 불변식으로 구형 승인 보완 제거, 현재 에셋 상태로 v2 에셋별 정보 | Task 2 Interfaces, Task 5 Step 1a·Step 2 |
| 역할 정규화로 명시적 기본 역할의 승인 해제 방지 | Task 1 `NormalizeRole`, Task 2 Step 1a |
| 생성 명령의 대상 에셋 생성 캔버스 확인 | Task 2 Step 2·Step 4 |
| 구형 호환 hash를 단일 정규화 hash로 대체 | Task 2 Step 2c |

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
SpriteRules.NormalizeRole(SpriteSettings settings, SpriteAssetPlan plan) : SpriteAssetPlan
    // 기본 역할이면 Role = null, 그 외 그대로
SpriteRules.CellWidth(SpriteGrid? grid, IReadOnlyList<SpriteAssetPlan> plans, SpriteSettings settings) : double?
    // grid 값 → Tile W 중앙값 → null
SpriteRules.AssetOutput(SpriteSettings settings, SpriteCanvas source, double? cellWidth, SpriteAssetPlan plan)
    : Result<(SpriteCanvas Canvas, double Scale)>
SpriteRules.AssetCanvas(SpriteSettings settings, SpriteCanvas source, SpriteCanvas jobGeneration,
    double? cellWidth, SpriteAssetPlan plan, IReadOnlyList<SpriteCanvas> generationSizes) : Result<SpriteAssetCanvas>
    // Tile·Layer: (OutputCanvas(settings, source), jobGeneration, Transform(jobGeneration, output), 1)
SpriteRules.Validate(SpriteSettings settings, SpriteCanvas source,
    IReadOnlyList<SpriteAssetPlan> assets, SpriteGrid? grid = null) : Result<bool>
SpriteRules.Anchor(SpriteAssetRole role, SpriteCanvas output) : SpriteAnchor
    // Layer·Backdrop (0,0), Tile·Object (W/2, H/2)
```

- [x] **Step 1: 실패 테스트** — `RoleOf`: null 역할 기본값 두 유형, 명시 역할 유지. `Validate`: 레이어 작업의 `Tile`·`Object` 거부, 타일 작업의 `Layer` 거부, `Backdrop` 2개 거부, `Backdrop`이 가장 뒤가 아니면 거부, `Object` 투명 false 거부, `Backdrop` 투명 true 거부, 비아이소 `Tile` 투명 false 허용, `grid.CellWidth` 0·1.01·NaN 거부, `Tile` 없이 `Object`만 있고 grid null이면 "격자 칸 너비가 필요합니다". `AssetOutput`: 원본 1536×1024·cellWidth 128/1536·tileWidth 128에서 `Object` bounds(0.1,0.1,0.2,0.25) → 307×256·Scale 1; 같은 조건 `Backdrop` → 1024×683·Scale 1024/1536; `Object`가 1025px이 되는 경계에서 실패 메시지에 "오브젝트"; `CellWidth` 중앙값(짝수 개면 두 중앙값 평균). `Anchor`: 네 역할의 기준점, 홀수 크기의 중심은 소수(예: 307×256 → 153.5, 128). 레이어 작업에 grid 값이 있으면 거부. `AssetCanvas`: `Tile`은 작업 단위 값과 같음, `Object`는 지원 크기 중 비율이 가장 가까운 생성 캔버스와 contain transform. 기존 `Validate_EnforcesTransparencyBackToFront`와 아이소메트릭 타일 사례는 그대로 통과해야 한다.
- [x] **Step 2: 실패 확인** — `dotnet test ... --filter 'FullyQualifiedName~SpriteRulesTests'`가 새 테스트에서 컴파일 오류 또는 단정 실패.
- [x] **Step 3: 구현** — Interfaces. 투명도 규칙은 `RoleOf` 기준으로 다시 쓰고 기존 메시지 "앞쪽 레이어와 다이아몬드 타일은 투명이 필요합니다"는 `Layer`·`Tile` 위반에 유지, `Object`·`Backdrop`은 별도 메시지. `Validate`의 격자 확인은 `Object`·`Backdrop`이 있을 때만 `AssetOutput`으로 크기까지 검사한다.
- [x] **Step 4: 통과 확인** — `SpriteRulesTests`, `SpriteLifecycleTests`, `SpriteAnalysisTests` 통과(기존 호출부는 grid 생략으로 컴파일 유지).
- [x] **Step 5: Commit** — `feat(sprites): add asset role and world-scale canvas rules`

## Task 2: 에셋 캔버스 상태·영속성·계획 확정

**Files:**

- Modify: `apps/backend/Noxtend.Domain/Sprites/SpritePipelineState.cs` — `SpritePipelineState.Grid`·`ReplaceGrid`, `SpriteAsset.Canvas`·`UpdateCanvas`(캔버스 변경 시 프레임·승인 해제, I2), 기존 에셋 앵커 갱신과 격자 변경 시 `Touch(null)`
- Modify: `apps/backend/Noxtend.Domain/Sprites/SpriteInputs.cs` — `SpriteExportAsset` 추가, `SpriteExportInput`에 `Grid`·`TileWidth`·`AssetDetails`(기본값 null, v1은 null). `SpriteApprovedAsset`은 변경 없음
- Modify: `apps/backend/Noxtend.Domain/Job/PipelineJob.Sprites.cs` — `ReplaceSpritePlan` 서명(출력 캔버스가 같으면 저장 생성 캔버스 재사용, 지원 크기는 필요할 때만 사용), `GenerationChanged`(유효 역할), 캔버스 변경을 affected로, 격자 포함 변경 없음 판정, 신규·기존 앵커 갱신, `SpriteInput`·`MatchesSpriteInput`(에셋 캔버스), `SpriteApproval`(`Diamond`는 아이소메트릭 `Tile`만, 나머지 필드는 기존), `SameExportInput`(Grid·TileWidth·AssetDetails 엄격 비교), 진입 계획 `NormalizeRole`. `SameApprovedAsset`·v1 `IsCurrentTask`는 변경 없음. `CaptureSpriteExport`의 신규 v2 접수와 v2 Grid 현재 판정은 Task 5
- Create: `apps/backend/Noxtend.Application/Sprites/SpriteGenerationSizes.cs`
- Modify: `apps/backend/Noxtend.Application/Sprites/SpriteCommandsHandler.cs` — `UpdatePlanAsync(context, plans, grid, ct)`와 생성 명령 capability 확인이 `SpriteGenerationSizes` 사용(생성 명령은 대상 에셋 생성 캔버스 확인), fingerprint 본문은 정규화한 plans(기본 역할 null·null `Role` 생략, ID 정렬)이고 grid 값이 있을 때만 `{ plans, grid }`, 작업 조회 후 정규화·receipt 비교를 revision/취소 검사 앞에 배치, 경합 후 승자도 같은 hash로 비교, 지원 크기 조회는 `SpritePlanNeedsGenerationSizes`로 필요 시에만
- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpriteAnalysisTaskHandler.cs` — 파싱·전체 계획 검증 → `SpritePlanNeedsGenerationSizes` → 필요할 때만 크기 조회 → `ReplaceSpritePlan` 순서(grid는 Task 3까지 null). 불필요하면 빈 목록 전달, 필요한 조회 실패는 `ProviderCallFailedException`(transient)로 재시도
- Modify: `apps/backend/Noxtend.Infrastructure/Persistence/Configurations/SpritePipelineConfiguration.cs` — `SpriteGridJson`(Jobs), `CanvasJson`(SpriteAssets)
- Modify: `apps/backend/Noxtend.Infrastructure/Persistence/SpriteJsonSerializer.cs` — 새 필드 없는 기존 JSON 허용, 개별 계획 검증에서 임의 Layers 설정 제거. ID·이름·유한한 영역·프레임·선택 역할 enum 검증은 유지하고 작업 전체 규칙은 실제 설정을 받는 `SpriteRules.Validate`에 유지. 저장 envelope v1 유지, v2 내보내기 입력 읽기·쓰기는 준비하되 manifest v2 활성화는 Task 5
- Create: migration `AddSpriteAssetRoles`(nullable `Jobs.SpriteGridJson`, `SpriteAssets.CanvasJson`)
- Modify(컴파일 유지): `Noxtend.Api` 계획 명령 호출부는 grid `null` 전달(계약 변경은 Task 6)
- Test: `Noxtend.Tests/Domain/SpriteLifecycleTests.cs`, `Application/SpriteCommandTests.cs`, `Application/SpriteAnalysisTests.cs`, `Infrastructure/SpritePersistenceTests.cs`, `Infrastructure/SpriteRequestConcurrencyTests.cs`
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
    // 조회가 필요 없으면 빈 목록, 필요한데 비어 있으면 SpritePlanInvalid 없이 JobImageModelUnavailable로 실패·미변경
PipelineJob.SpriteAssetCanvasOf(SpriteAsset asset) : SpriteAssetCanvas   // Canvas ?? 작업 단위 값
PipelineJob.SpritePlanNeedsGenerationSizes(IReadOnlyList<SpriteAssetPlan> plans, SpriteGrid? grid) : bool
    // 전체 계획 검증 통과 후 호출, 신규·크기 변경 Object·Backdrop 존재 여부
// SpriteApprovedAsset는 기존 그대로(승인 snapshot에 새 필드 없음)
public sealed record SpriteExportAsset(Guid Id, SpriteAssetRole Role, SpriteCanvas Canvas, double Scale,
    Bounds SourceBounds);                                     // 접수 시 현재 에셋 상태(I2)
public sealed record SpriteExportInput(int SchemaVersion, Guid ExportId, int ReviewRevision, SpriteView View,
    SpriteOutputKind OutputKind, SpriteCanvas SourceCanvas, SpriteCanvas OutputCanvas,
    IReadOnlyList<SpriteApprovedAsset> Assets, IReadOnlyList<Guid> ExcludedAssetIds,
    SpriteGrid? Grid = null, int? TileWidth = null, IReadOnlyList<SpriteExportAsset>? AssetDetails = null);
    // v1: 새 필드 모두 null. v2: AssetDetails는 Assets와 같은 ID 집합
public static class SpriteGenerationSizes
{
    // 활성 공급자·모델의 투명 지원과 양수 크기 확인
    public static Task<Result<IReadOnlyList<SpriteCanvas>>> ResolveAsync(PipelineJob job,
        IProviderConfigRepository providers, IModelCatalog catalog, CancellationToken ct);
}
SpriteCommandsHandler.UpdatePlanAsync(SpriteCommandContext context, IReadOnlyList<SpriteAssetPlan> plans,
    SpriteGrid? grid, CancellationToken ct)
```

- [x] **Step 1: 실패 테스트(도메인)** — 계획 확정이 `Object`·`Backdrop` 에셋에 `Canvas`를 저장하고 `Tile`은 작업 단위와 같은 값; `Object` 생성 입력(`SpriteFrameInput.Canvas`·`GenerationCanvas`·`Transform`)이 에셋 캔버스; 역할만 `Tile`→`Object`로 바꾸면 그 에셋만 `PlanRevision` 증가·이미지 무효화·`PlanReview`; 격자 변경은 `Object`·`Backdrop`만 무효화하고 `Tile` 이미지 유지; 이름만 바꾸면 무효화 없음(기존 단정 유지); 신규·기존 `Object` 앵커가 캔버스 중심, `Backdrop` 전환 시 (0,0); 승인 스냅숏 필드는 기존과 같고 아이소메트릭 `Object`의 `Layout`은 `Square`; 출력 캔버스가 바뀐 에셋은 계획이 같아도 승인 해제(I2), 상한에 걸려 `Scale`만 바뀐 `Backdrop`은 저장 Scale만 갱신하고 이미지·승인 유지; `CaptureSpriteExport`는 Task 5 전까지 SchemaVersion 1 유지; 격자 없는 기존 타일 작업(Review Focus 2) 역할 변경 시 중앙값 격자 사용, 모든 에셋 `Object`면 격자 필요 오류.
- [x] **Step 1a: 격자·앵커 회귀** — plans가 같고 grid만 다른 경우 저장·`ReviewRevision` 증가; 타일만 있으면 이미지·승인·`PlanRevision` 유지, 새 생성 공정 없음, 모든 기존 ZIP은 이전 표시. 같은 plans·grid·유효 캔버스 재저장은 no-op. 캔버스가 달라진 대상만 프레임·승인을 지우고 `PlanRevision` 증가·`PlanReview` 전이. 기존 Tile→Object, Object 크기 변경, Object→Backdrop 각각 저장 앵커와 승인 snapshot 앵커 확인. 계산·검증 실패 시 격자·계획·캔버스 모두 미변경. 역할 정규화: `Role` null인 기존 승인 행에 같은 계획을 명시적 기본 역할로 저장하면 no-op(revision·승인·ZIP 현재 표시 유지)이고, 명시적 기본 역할에 격자만 바꾼 저장은 승인·이미지 유지. 분석 반영도 같은 정규화로 기본 역할을 null로 저장.
- [x] **Step 1b: 생성 캔버스 재사용·완료 상태** — 출력 캔버스가 같은 `Object`는 지원 크기 목록이 바뀐 상태로 재저장해도 생성 캔버스·이미지·`PlanRevision` 유지. 격자만 바뀐 `Completed` 작업은 모든 ZIP 이전 표시·`ExportReady`·`PendingReview`이고 `CompletedExportId` 값은 유지(응답 null은 Task 6). 캔버스가 바뀌는 `Object`가 생성 중이면 `SPRITE_BUSY`이고 격자·계획·캔버스 미변경. 승인 snapshot 앵커 = 저장 앵커 불변식을 역할 전환 시나리오 전체에 단정. 실패 처리된 v1 내보내기 뒤 격자만 바꾸고 `ReconcileFromTasks`를 실행해도 `PendingReview`이고 옛 `FailureReason`이 다시 쓰이지 않음(`CompletedExportId` 유지로 재처리 없음). v1 공정 현재 판정은 격자 변경과 무관하게 기존과 같음. 대상 밖 에셋 수정은 부분 내보내기 현재 판정을 유지.
- [x] **Step 2: 실패 테스트(Application)** — 크기 조회가 필요한 새 Object 계획의 `UpdatePlanAsync`가 지원 크기를 조회해 전달하고, 공급자 비활성·모델 없음·크기 없음이면 `JobImageModelUnavailable` 계열로 실패하고 계획이 바뀌지 않음. 생성 명령: 지원 목록에서 빠진 생성 캔버스를 가진 `Object`의 재생성·기준 승인·계획 승인은 `JobImageModelUnavailable`이고 공정 없음·revision 불변이다. 같은 작업의 `Tile` 재생성은 통과하고, 레이어·타일만 있는 작업의 기존 검사 결과도 그대로다. 혼합 분석의 조회 실패 검증은 Task 3에서 수행(Review Focus 4·Step 2d).
- [x] **Step 2a: 요청 중복·경합 회귀** — 동일 requestId·정렬 순서만 다른 plans·같은 grid는 같은 receipt, grid만 다르면 `SPRITE_REQUEST_CONFLICT`; null과 생략 grid는 동일. 접수 후 revision·모델 상태가 바뀌어도 같은 요청 재전송은 기존 receipt이며 capability 재조회 없음. SQL 경합에서도 다른 grid의 같은 requestId가 성공으로 합쳐지지 않음.
- [x] **Step 2b: 조회 조건·정규화** — 이름·FPS만 바꾼 `Object` 계획은 공급자 비활성이어도 저장되고 capability 조회 호출 0회. 새 `Object` 추가·`Object` 크기 변경은 조회 1회, 조회 실패 시 미변경. 생략한 role과 명시적 기본 role로 같은 requestId를 재전송하면 같은 receipt.
- [x] **Step 2c: 접수 순서·단일 hash 호환 회귀** — Layers와 Tiles 각각 역할 생략·명시적 기본값의 신규 접수/재전송 비교. 변경 전 코드로 계산한 고정 hash 상수와 receipt를 SQL에 저장·재조회한다(Layers·Tiles 한 건씩). 원래 본문과 명시적 기본 역할 재전송은 같은 receipt이고, 비기본 역할·값 있는 grid·이름 등 기존 필드 변경은 `SPRITE_REQUEST_CONFLICT`다. 같은 상수가 새 정규화 함수의 결과와 일치하는지 직접 단정해 바이트 호환을 고정한다. 다른 grid는 항상 충돌이다. 현재 revision 변경·취소·공급자 비활성 후 재전송은 receipt 우선·capability 조회 0회·신규 반영 없음. 다른 JobId/Kind 요청과 SQL 경합 후 승자 비교도 검사한다. 고정 hash 상수를 새 구현 함수로 생성해 기대값을 자기 검증하지 않는다.
- [x] **Step 2d: 분석 조회 조건** — Task 2에서는 기존 레이어/타일만 분석이 조회 0회·빈 지원 목록으로 계획 저장됨을 검증한다. Task 3에서 신규 Object·Backdrop 혼합 계획의 조회 1회와 실패 시 Retry·계획 미저장을 추가한다. 잘못된 분석 계획은 조회 0회·`SpritePlanInvalid` Retry다. 기존 이미지 모델이 비활성이어도 크기 조회가 불필요한 분석은 완료한다(실제 이미지 생성 명령의 사용 가능 검사는 유지).
- [x] **Step 3: 실패 테스트(영속성)** — 역할·격자·에셋 캔버스·v2 내보내기 입력(`Grid`·`TileWidth`·`AssetDetails`) 왕복; 새 열이 null인 기존 행과 `Role`·`Canvas` 없는 기존 `PlanJson`, 기존 `ApprovalJson`, 새 필드 없는 v1 내보내기 입력 JSON을 읽으면 `Role` null·`Canvas` null·v1 새 필드 null(Review Focus 1).
- [x] **Step 3a: 저장 검증 회귀** — Tile·Object·Backdrop을 포함한 계획·캔버스·승인을 실제 SQL에 저장하고 새 DbContext로 재조회. 개별 계획의 잘못된 enum·영역·프레임 값은 저장 경계에서 거부, 레이어 작업의 Object 등 전체 규칙은 실제 설정의 도메인 검증에서 거부. envelope v1 안의 v1/v2 내보내기 입력을 읽고 지원하지 않는 payload 버전은 거부.
- [x] **Step 4: 구현** — Files 전체. `SpriteGenerationSizes`는 `SpriteCommandsHandler`의 기존 capability 조건(활성 공급자, `SupportsTransparency`, 양수 크기)을 옮긴다. 생성 명령의 "작업 생성 캔버스가 지원 크기에 있음" 확인은 명령 대상 에셋 생성 캔버스 확인으로 바꾸고 도메인 반영 전에 수행한다(`Tile`·`Layer`는 작업 값과 같아 기존 결과 유지). migration 생성 후 `has-pending-model-changes`가 변경 없음.
- [x] **Step 5: 검증** — `SpriteLifecycleTests`·`SpriteRulesTests`·`SpriteCommandTests`·`SpriteAnalysisTests`·`SpriteGenerationTests`·`SpritePersistenceTests`·`SpriteRequestConcurrencyTests`·`SpriteApiTests` 통과, `dotnet build` 오류 0.
- [x] **Step 6: Commit** — `feat(sprites): store per-asset canvases and grid`

## Task 3: 분석 v3 프롬프트와 파서

**Files:**

- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `AnalyzeSpritesV3()`(기존 V1·V2 유지)
- Create: migration `ReviseSpriteAnalyzePromptV3`(새 고정 Id, Background 슬롯 개정)
- Modify: `apps/backend/Noxtend.Application/Sprites/SpritePlanParser.cs` — v3 schema(`role` 필수, 루트 `grid` 필수: 타일 작업 `{cellWidth}`, 레이어 작업 `null`), 반환에 grid
- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpriteAnalysisTaskHandler.cs` — 파싱된 grid를 Task 2의 전체 계획 검증·조건부 크기 조회·계획 반영에 전달
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

- [x] **Step 1: 실패 테스트** — 파서: 역할 누락·알 수 없는 역할·유형에 없는 역할·`backdrop` 2개·역할과 투명도 불일치·타일 작업 grid 누락·레이어 작업 grid 값 존재가 모두 `SpritePlanInvalid`; 정상 v3 JSON이 역할·grid를 그대로 반환(값을 고치지 않음). Fake 기본 분석이 타일 작업에서 세 역할을 만들고 계획 검증 통과. 분석 조회 조건은 Task 2 Step 2d의 혼합 계획 사례까지 검증(정상 1회, 조회 실패 Retry·미저장, 잘못된 계획 0회). migration: 활성 행이 V3, 버전 `MAX+1`(운영자 v9 → 10), 다른 슬롯 불변, Down 복원. V3 변수가 허용 변수 안.
- [x] **Step 2: 구현** — V3 System은 V2 정규화 좌표 안내를 유지하고 spec "프롬프트" 절의 분류 규칙·반례·`cellWidth` 정의와 예시를 추가한다. Schema는 V2에 `role` enum(`layer`,`tile`,`backdrop`,`object`)과 루트 `grid`(`oneOf` null 또는 `{cellWidth: number}`)를 더한다.
- [x] **Step 3: 검증** — 위 테스트와 `SpriteRulesTests`·`SpriteLifecycleTests`, migration 테스트(Testcontainers) 통과.
- [x] **Step 4: Commit** — `feat(sprites): analyze sprite asset roles and grid`

## Task 4: 역할별 생성 요청·정규화와 생성 프롬프트 개정

**Files:**

- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpriteGenerationTaskHandler.cs` — 배경 요청은 계획 투명도(역할 규칙으로 이미 결정), `layout`은 아이소메트릭 `Tile`만 `Diamond`, `frame.anchor`는 `SpriteRules.Anchor(RoleOf, input.Canvas)`(작업 유형 분기 제거)
- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `GenerateSpriteV2()`
- Create: migration `ReviseSpriteGeneratePrompt`(GenerateSprite Background 슬롯 첫 개정)
- Test: `Application/SpriteGenerationTests.cs`, `Application/SpriteProviderRequestTests.cs`, `Infrastructure/SpritePixelTests.cs`, 새 `Infrastructure/SpriteGeneratePromptMigrationTests.cs`(`SpriteAnalyzePromptMigrationTests` 패턴), `SeedPromptVariableTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md`

**Interfaces:** Consumes Task 2 에셋 캔버스(`SpriteFrameInput`에 이미 고정). Produces `SeedPrompts.GenerateSpriteV2()`.

- [x] **Step 1: 실패 테스트** — 아이소메트릭 타일 작업의 `Object` 요청이 에셋 생성 캔버스 크기 문자열·`Transparent`, `Backdrop`은 `Opaque`, `Object` 정규화 layout `Square`(다이아몬드 clip 없음, 모서리 픽셀 보존), `Tile`은 기존과 같음; 변수 `asset`에 `role`, `outputCanvas`에 에셋 캔버스; 투명 픽셀 없는 `Object` 결과는 `Fail(ProviderBadResponse)`이며 공급자 호출 1회·자동 재시도 없음. 역할·크기 변경 후 `frame.anchor`가 Task 2 저장 앵커와 일치. 기존 일시적 공급자 오류만 Retry이고 인증·quota·decode 오류의 기존 분류 유지. migration은 Task 3과 같은 네 단정.
- [x] **Step 2: 구현** — V2 System을 역할별 문단으로 나눈다(`tile` 기존 지침, `backdrop` 캔버스를 채우는 불투명 원경·전경 물체 금지, `object` 투명 배경 위 물체 하나·잘림 없음·원본 시점과 조명 유지, `layer` 기존 지침). 변수 목록은 바꾸지 않는다.
- [x] **Step 3: 검증** — 위 테스트와 `SpriteLifecycleTests` 통과.
- [x] **Step 4: Commit** — `feat(sprites): generate sprites by asset role`

## Task 5: manifest v2 내보내기

**Files:**

- Modify: `apps/backend/Noxtend.Application/Sprites/SpritePackageWriter.cs` — v2는 `AssetDetails`의 캔버스로 크기 검사·시트 분할하고 역할로 경로 `tiles/`·`objects/`·`backdrop/`·`layers/`를 정한다. v1은 기존대로 `input.OutputCanvas`·작업 유형 경로를 쓴다. manifest `SchemaVersion`은 `input.SchemaVersion`
- Modify: `apps/backend/Noxtend.Application/Sprites/RunSpritePackTaskHandler.cs`(필요 시)
- Modify: `apps/backend/Noxtend.Domain/Job/PipelineJob.Sprites.cs` — `CaptureSpriteExport`의 신규 v2 접수(현재 에셋 상태로 `AssetDetails`, 저장값 또는 중앙값 `Grid`, 설정 `TileWidth`), 내보내기 공정 `IsCurrentTask`에 v2만 `input.Grid` = 현재 유효 격자 비교 추가. `SameExportInput`의 엄격한 값 비교는 유지
- Modify: `apps/backend/Noxtend.Infrastructure/Persistence/SpriteJsonSerializer.cs` — v1/v2 입력·manifest 검증과 버전 일치, 새 v2 필수값 확인
- Test: `Infrastructure/SpritePackageTests.cs`, `Infrastructure/SpritePersistenceTests.cs`, `Application/SpriteCommandTests.cs`, `Domain/SpriteLifecycleTests.cs`
- Modify: `docs/xHuman/backend.md`

**Interfaces:** Consumes Task 2 `SpriteExportAsset`, `SpriteExportInput.Grid/TileWidth/AssetDetails`, `SpriteAssetCanvasOf`, Task 1 `RoleOf`·`CellWidth`. Produces: 새 `CaptureSpriteExport`는 v2; 기존 `task.SpriteExportInput`은 버전과 고정 입력을 유지.

- [x] **Step 1: 실패 테스트** — 혼합 작업(타일 128×64, 오브젝트 307×256, 원경 1024×683) ZIP: 역할별 기본 경로, 시트 셀 크기가 에셋 캔버스, manifest에 역할·캔버스·scale·sourceBounds·grid·tileWidth, schemaVersion 2; 오브젝트 이미지 크기가 캔버스와 다르면 거부(Review Focus 5); SchemaVersion 1 기존 입력은 이전과 같은 경로(`layers/`·`tiles/`)와 manifest(Review Focus 1). 기존 `Zip_ContainsManifestBasesFramesAndSheets` 단정 유지.
- [x] **Step 1a: 구형 승인 호환** — 새 필드 없는 승인 JSON을 SQL에서 읽고 재생성·재승인 없이 새 내보내기. `AssetDetails`는 현재 에셋 상태(유효 역할·작업 캔버스·Scale 1·현재 SourceBounds), 승인 snapshot은 저장값 그대로이며 이미지 ID·이름·순서·FPS 유지. 타일 Grid는 저장값 또는 중앙값, TileWidth는 설정값; 레이어 둘 다 null. 새 v2 공정의 `IsCurrentTask == true` → ZIP 첨부(`IsCurrent == true`) → 전체 `Succeeded`/일부 `PartiallySucceeded` 완료까지 검사하고 SQL 재조회 뒤에도 동일함을 확인한다. 저장 승인 JSON은 불변. 접수된 v1 재시도는 계획 변경 후에도 고정 입력·v1 경로·기존 manifest 필드 구성 유지(새 필드 미출력).
- [x] **Step 1b: 저장·연결 회귀** — 완성된 v2 manifest·입력·ZIP 참조를 SQL에 저장하고 새 DbContext로 읽어 버전·`AssetDetails`·Grid·TileWidth·앵커 비교. manifest/input 버전 불일치·새 필드 값 차이·v2 필수값 누락(`AssetDetails`와 `Assets`의 ID 집합 불일치 포함)을 거부한다. 격자만 수정한 승인 작업은 기존 ZIP 이전 표시 후 승인·이미지 유지 상태로 새 v2 ZIP 생성. v2 현재 판정(I1): 진행 중 v2 내보내기 뒤 격자만 바꾸면 이전 결과로 붙고 작업 미완료; 격자 변경 후 옛 v2 실패 공정의 수동 재시도 거부; `A → B → A` 뒤 옛 v2 공정은 현재; 격자 변경 후 새 접수는 정상 완료.
- [x] **Step 2: 구현** — Files. writer의 버전 허용·버전별 manifest 출력과 저장 검증을 마련한 뒤 같은 Task에서 `CaptureSpriteExport`를 v2로 전환. 접수는 현재 에셋 상태로 `AssetDetails`를 만들며 승인 snapshot은 바꾸지 않는다. 저장된 v1 작업 입력은 수정하지 않는다.
- [x] **Step 3: 검증** — `SpritePackageTests`, `SpriteCommandTests`, `SpriteLifecycleTests`, `SpritePersistenceTests` 통과.
- [x] **Step 4: Commit** — `feat(sprites): export role-aware sprite manifest v2`

## Task 6: API 계약

**Files:**

- Modify: `apps/backend/Noxtend.Api/Contracts/SpriteContracts.cs` — `SpriteAssetPlanRequest.Role`(선택, camelCase 문자열), `SpritePlanRequest.Grid`(선택), `SpriteResponse.Grid`, `SpriteAssetResponse.Canvas`·`Scale`, `SpriteResponse.From`의 `CompletedExportId`는 해당 export가 `IsCurrent`일 때만(승인 snapshot 응답은 변경 없음)
- Modify: `apps/backend/Noxtend.Api/Controllers/SpriteJobsController.cs` — 계획 수정에 grid 전달, 잘못된 역할 400
- Test: `Api/SpriteApiTests.cs` — 실제 DTO·서버 JSON 설정을 사용하는 연결 fixture 생성/재생성 검사
- Create: `apps/frontend/src/infra/api/fixtures/sprite-roles.server.json` — 서버 테스트에서 생성하는 새 혼합 작업·구형 작업·구형 승인·grid null 응답 fixture
- Create: `apps/frontend/src/infra/api/fixtures/sprite-roles.request.json` — 타일 작업 계획 수정 요청(명시적 세 역할·grid)과 레이어 작업 요청(`role: "layer"`, `grid: null`)
- Create: `apps/frontend/src/infra/api/fixtures/sprite-roles.server.md` — 생성 명령·기준 revision·시나리오·fixture 비교 방법
- Modify: `docs/xHuman/backend.md`

**Interfaces:**

- Consumes: Task 2 `UpdatePlanAsync(context, plans, grid, ct)`.
- Produces(JSON): plan asset `role?: 'layer'|'tile'|'backdrop'|'object'`(응답은 항상 유효 역할), plan 요청 `grid?: { cellWidth: number } | null`, sprite 응답 `grid: { cellWidth } | null`, asset 응답 `canvas: { width, height }`·`scale: number`, `completedExportId`는 현재 export일 때만 값.

- [x] **Step 1: 실패 테스트** — 새 필드 직렬화; 요청 `role` 생략 시 유형 기본값으로 저장; `role: "unknown"` 400; 레이어 작업에 `object` 400(`SPRITE_PLAN_INVALID`); 기존 작업 응답의 `role`이 기본값·`canvas`가 작업 단위 값·`scale` 1; 알 수 없는 필드는 기존처럼 400. 완료 작업에서 격자만 바꾼 뒤와 이름만 바꾼 뒤 `completedExportId`가 null이고, 새 내보내기 완료 후 새 ID. 기존 `FailedConsumedExportId_DoesNotAdvertiseStoredZip` 유지.
- [x] **Step 1a: 서버 연결 fixture** — 실제 `SpriteResponse.From`을 포함한 작업 응답 DTO 매핑과 서버 JSON 설정으로 네 시나리오를 직렬화한다. 고정 ID·시간을 사용하고 생성 결과와 저장 fixture의 구조·값 일치를 검사한다. 구형 DB 행의 새 서버 응답과 과거 필드 생략 응답은 구분한다. 생성 명령·기준 revision을 인접 문서에 기록한다.
- [x] **Step 1b: 요청 fixture 바인딩** — `sprite-roles.request.json`의 명시적 필드가 있는 두 요청을 실제 API(`JsonUnmappedMemberHandling.Disallow`)로 보내 성공하고 저장된 역할·grid가 요청과 같음. 별도 호환 테스트에서는 role·grid를 생략한 본문을 보내 작업 기본 역할·grid null로 처리되는지 확인한다. 생략형은 Frontend 요청 비교 fixture에 넣지 않는다. fixture 경로는 `apps/backend/Noxtend.slnx`에서 저장소 루트를 찾아 계산하고, 응답 fixture 갱신은 문서에 적은 환경 변수를 켠 실행으로만 한다.
- [x] **Step 2: 구현** — Files.
- [x] **Step 3: 검증** — `SpriteApiTests`, `JsonBinding_*` 통과, Backend 전체 회귀(유료 smoke 제외).
- [x] **Step 4: Commit** — `feat(api): expose sprite asset roles and canvases`

## Task 7: Frontend 도메인·API·Fake parity

**Files:**

- Modify: `apps/frontend/src/domain/sprites/types.ts` — `SpriteAssetRole`, `SpriteGrid`, `SpriteAssetPlan.role`, `SpriteAsset.canvas`·`scale`, `SpriteState.grid`, `readPlan`·`readState`(승인 snapshot reader는 변경 없음)
- Modify: `apps/frontend/src/domain/sprites/rules.ts` — `assetRole(settings, plan)`, `cellWidthOf(grid, plans)`, `assetOutputSize(settings, source, cellWidth, plan)`(서버와 같은 반올림·1024 규칙, 결과 또는 오류 문구)
- Modify: `apps/frontend/src/domain/sprites/labels.ts` — `spriteRoleLabel`: `tile`=반복 타일, `object`=오브젝트, `backdrop`=원경 배경, `layer`=배경 레이어
- Modify: `apps/frontend/src/infra/api/spriteApi.ts` — `updateSpritePlan` 본문에 `role`·`grid`
- Modify: `apps/frontend/src/app/queries/useSprites.ts`(입력 타입 전달만)
- Modify: `apps/frontend/tests/e2e/spriteFakeApi.ts` — 역할별 투명도·캔버스·앵커·경로·manifest v2, 타일 작업 seed에 세 역할, `completedExportId`는 현재 export일 때만
- Create: `apps/frontend/src/infra/api/spriteContract.test.ts` — Task 6의 서버 생성 fixture를 실제 작업 응답 reader로 검증
- Modify: `apps/frontend/src/domain/job/backendParity.test.ts` — `SpriteAssetRole` enum parity 추가
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

- [x] **Step 1: 실패 테스트** — 새 필드 읽기, 알 수 없는 역할 거부, 구형 응답(역할·canvas·grid 없음) 기본값 읽기(Review Focus 1), `assetOutputSize`가 Task 1과 같은 수치 사례(307×256, 1024×683·scale, 1025 경계 오류), 요청 본문에 허용 필드만(`role`·`grid` 포함).
- [x] **Step 1a: 실제 응답 연결** — Task 6 fixture를 HTTP 응답으로 제공해 `getJob` → `apiRequest` → `readSpriteJob` 경로로 읽는다. 새 혼합 작업·구형 작업·구형 승인 snapshot·grid null의 역할·캔버스·scale·grid·승인값을 단정한다. 과거 필드 생략 응답의 기본값 검사는 별도로 유지한다. 잘못된 role·canvas·scale 거부도 확인한다. Fake UI 검사와 enum parity는 이 연결 검사를 대체하지 않는다.
- [x] **Step 1b: 요청 본문 연결** — `updateSpritePlan`이 만든 명시적 role·grid를 가진 두 요청 본문(레이어는 layer·null)이 `sprite-roles.request.json`과 깊은 비교로 같음. 한쪽만 필드를 바꾸면 이 테스트나 Task 6 바인딩 테스트가 실패한다.
- [x] **Step 2: 구현** — Files. Fake는 서버 규칙을 그대로 따른다.
- [x] **Step 3: 검증** — `pnpm test && pnpm typecheck`, 기존 sprite e2e 통과.
- [x] **Step 4: Commit** — `feat(frontend): read sprite asset roles and canvases`

## Task 8: Frontend 화면

**Files:**

- Modify: `apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx` — 타일 작업 행에 역할 select(라벨 `spriteRoleLabel`), 투명도 체크박스 읽기 전용(역할 결정), 격자 칸 너비 입력(원본 px 단위로 입력·비율로 저장, 저장값 없으면 `자동 Npx` 자리표시, 비우면 null)과 plans·grid 통합 draft/dirty/동기화, 행별 `출력 W×Hpx`, 클라이언트 검증 오류 행 표시, 오버레이 라벨에 역할
- Modify: `apps/frontend/src/features/screens/sprites/SpritePreview.tsx` — 에셋 단위 분기(`tile` 3×3, `object`·`backdrop` 단일 이미지·자기 캔버스)
- Modify: `apps/frontend/src/features/screens/sprites/SpriteFrameReview.tsx` — 역할 배지
- Modify: `apps/frontend/src/features/screens/sprites/SpriteAnalysisPanel.tsx` — 역할별 수 요약(`spriteAnalysisSummary`)
- Test: `src/features/screens/sprites/SpriteAnalysisPanel.test.ts`, `tests/e2e/sprites-static.spec.ts`(또는 새 `sprites-roles.spec.ts`)
- Modify: `docs/xHuman/frontend.md`

**Interfaces:** Consumes Task 7 전체.

- [x] **Step 1: 실패 테스트** — 단위: 요약 `반복 타일 1 · 오브젝트 1 · 원경 배경 1`. e2e(Fake): 타일 작업 계획 검수에 세 역할과 출력 크기 표시 → 계획 승인 → 생성 → 검수(역할 배지) → 내보내기 ZIP manifest v2; 한 에셋 역할 변경 시 그 에셋만 다시 생성 대기; 격자 칸 너비를 줄여 오브젝트가 1024px를 넘으면 해당 행 오류와 승인 비활성(Review Focus 3); 레이어 작업 화면은 역할 select 없음; 390px 가로 넘침 없음.
- [x] **Step 1a: 격자 draft E2E** — 타일만 있는 작업에서 격자만 수정해 저장 활성화·요청 본문·새 revision 확인, 이미지·승인 유지·기존 ZIP 이전 표시. 미저장 격자는 폴링·409 뒤에도 보존하고 자동 재제출 없음. 최신 계획 다시 불러오기는 plans와 grid를 함께 복원하며 저장 성공 응답 동기화 후 dirty 해제.
- [x] **Step 2: 구현** — Files. 역할 select·격자 입력은 명시적 라벨과 키보드 조작, 오류는 텍스트로 전달.
- [x] **Step 3: 검증** — Frontend 전체 회귀(`pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`).
- [x] **Step 4: Commit** — `feat(frontend): edit and review sprite asset roles`

## Task 9: 문서와 전체 회귀

**Files:**

- Modify: `DESIGN.md`(2D 계획 검수 역할 선택·출력 크기), `PRODUCT.md`(2D 배경 결과 설명에 타일 작업의 역할), `docs/superpowers/specs/2026-10-06-2d-background-sprites-design.md`(“한 작업은 레이어 또는 타일 중 한 결과 유형” 문장 옆 이 설계 링크), `docs/xHuman/CODEMAP.md`(필요 시)
- Modify: 이 계획의 실행 기록

- [x] **Step 1:** 문서 갱신 후 `pnpm docs:check`.
- [x] **Step 2:** Backend 전체 회귀(유료 smoke 제외), `has-pending-model-changes` 변경 없음, Frontend 전체 회귀. 결과 수치를 실행 기록에 남긴다.
- [x] **Step 3: Commit** — `docs: record 2d asset roles implementation`

## 실행 기록

### 설계 단계의 과거 기록

아래 미착수 기록은 구현 전 결정 이력이며 현재 구현 상태는 뒤의 실행 결과를 따른다.

- 2026-10-14: 승인된 리뷰 7항목을 설계와 Task별 변경·검증 범위에 반영. 애플리케이션 구현은 미착수.
- 문서 검증: 두 문서 대상 `pnpm docs:check`와 `git diff --check` 통과. 설계·Task·검증 사례의 대응과 실제 reader 경로를 재확인했다. 애플리케이션 테스트·DB migration 적용·실제 AI 호출은 미실행.
- 2026-10-14 추가 리뷰 4건: 작업 설정 조회 후 정규화·receipt 우선 비교, 제한된 구형 fingerprint 호환, 명시형 요청 fixture와 서버 생략형 검사 분리, 분석의 조건부 지원 크기 조회를 반영. 문서만 보완하며 애플리케이션 구현은 미착수.
- 2026-10-14 코드 대조 리뷰 4건을 반영했다. 첫째, 내보내기 공정 현재 판정에 격자를 더했다(현재 `IsCurrentTask`는 승인 snapshot만 비교해 격자 변경 후에도 옛 공정이 현재로 남음). 둘째, 이 판정을 전제로 `CompletedExportId` 해제가 실패한 내보내기를 다시 처리하지 않게 했다. 셋째, 생성 명령이 대상 에셋 생성 캔버스를 확인하게 했다. 넷째, 구형 hash 이중 비교를 바이트 호환 단일 정규화 hash로 대체했다. 문서만 보완하며 애플리케이션 구현은 미착수.
- 2026-10-14 추가 리뷰 2건 반영: 마지막 격자 변경 revision으로 같은 유효 값·원래 값 복원 시 옛 내보내기 재활성화를 차단하고, 구형 승인 보완을 v2 접수·현재성 비교에 일관되게 적용하도록 설계·Task 2·Task 5·SQL/완료 경계 검증을 보완했다. 애플리케이션 구현은 미착수.
- 2026-10-14 전체 재검토: 설계에 불변식 I1(현재 ZIP)·I2(승인)·I3(역할 정규화)를 두고 규칙을 그로부터 다시 정리했다. 바로 위 기록의 격자 변경 revision과 구형 승인 보완은 다음으로 대체했다.
  - v2 Grid 현재 판정과 `CompletedExportId` 미해제. 응답은 현재 export만 낸다.
  - 승인 snapshot은 바꾸지 않고, 접수 때 현재 에셋 상태로 `AssetDetails`를 만든다.
  - 역할 정규화(`NormalizeRole`)를 추가했다. 정규화가 없으면 명시적 기본 역할 저장이 기존 승인을 해제한다(`ReplaceSpritePlan` record 비교·`UpdatePlan` 승인 해제 확인).
  - 문서만 보완했으며 애플리케이션 구현은 미착수.


### 2026-10-14 구현과 검증 결과

실행 방식은 작업별 구현·독립 리뷰(Subagent-driven)다. 기존 `claude/2d-asset-roles` checkout을 재사용했으며 기준은 `568aeb6`이다. Tasks 1–8 구현·각 작업 리뷰는 완료했다. Task 9는 문서 정리와 이미 실행한 전체 회귀 근거 확인이며 애플리케이션·의존성·검사 설정을 변경하지 않았다. **이 2026-10-14 기록 당시에는 전체 변경의 마지막 독립 리뷰와 아래 보강 검사가 남아 있었다.** 이후 최종 리뷰·수정·재리뷰 상태는 아래 2026-10-15 기록을 따른다. 위 체크는 당시 작업별 실행 근거다.

| Task | 최종 커밋 | 구현·작업별 검증 근거 |
| --- | --- | --- |
| 1 | `55469bb` | 역할·월드 축척 순수 규칙, focused 139 통과·build 성공 |
| 2 | `21eb405` | 역할/grid/에셋 canvas/앵커 저장·receipt 바이트 호환, focused 297 및 보강 10 통과. 역할·캔버스·승인 SQL 왕복. 당시 v2 envelope 검사는 메모리 직렬화였고 v2 SQL 완료 경계는 Task 5에서 검사 |
| 3 | `20bc033` | 분석 v3·Fake 혼합 역할·프롬프트 migration, focused 189·SQL 영속성 47·최종 분석 42 통과 |
| 4 | `b803f0c` | 역할별 생성·투명도·자기 캔버스·프롬프트 개정, focused 146 통과·build 성공 |
| 5 | `c2dc3f9` | 신규 export v2·실제 ZIP writer·구형 승인 SQL 호환·완료 snapshot SQL 왕복·현재성, focused 129 및 공유 실행 경계 71 통과·build 성공 |
| 6 | `99094b9` | role/grid/canvas API·현재 completed ID·실제 서버 직렬화 fixture·MVC 요청 바인딩, API 67·JSON 23 통과·build 성공 |
| 7 | `98baea6` | 실제 서버 fixture→HTTP client reader·요청 fixture 연결·역할별 검증·Fake, 단위 525·sprite E2E 72 통과. 추가 에셋 저장 동기화 리뷰 수정 focused E2E 1 통과 |
| 8 | `0362628` | 통합 plans/grid draft·역할 입력·크기 오류·검수·미리보기, 전체 단위 526·E2E 368 통과 및 lint/typecheck/build 성공 |

숫자는 서로 중복되는 테스트를 포함하므로 합산하지 않는다. Task 8의 첫 전체 E2E는 367 통과·1 실패였다. 자동 격자 중앙값 변화로 다른 에셋 크기도 바뀌는 정상 동작을 단일 대상 무효화 사례와 혼동한 fixture를 고정 grid 320px로 수정한 뒤 전체 368 통과했다. 실패 기록을 최종 통과로 덮어쓰지 않는다.

#### 구현 중 확정한 경계

- `Tile`·`Layer`는 항상 작업 단위 출력/생성 캔버스·transform을 쓴다. 같은 출력이면 생성 캔버스를 재사용한다는 규칙은 `Object`·`Backdrop`에 적용한다. Object→Tile의 출력 크기가 같아도 Tile은 작업 생성 캔버스로 돌아간다. 명시적인 역할 캔버스 계약을 일반 재사용 문장보다 우선했다.
- 이미 실행 중인 v2 패키징에서 대상 목록·승인 snapshot은 같고 격자만 달라지면 성공 ZIP을 이전 결과로 보존한다. `CanPreserveSpriteExportTask`를 lease 갱신과 최종 반영 경계가 함께 사용한다. 최초 claim 현재성·취소·lease 소유권·attempt·rowversion과 metadata stale 거부는 유지한다. 과거 ZIP은 작업 전체 완료를 만들지 않는다. A→B→A 격자 복원은 값 비교에 따라 옛 공정을 다시 현재로 볼 수 있다.
- Task 7은 기존 작업 유형 단위 투명도 검증을 유효 역할 기준으로 최소 수정했다. Backdrop은 불투명·Object는 투명이며 Tile/Layer는 기존 규칙을 유지한다. 유효한 혼합 계획을 기존 타일 전체 투명도 가드가 거부하지 않게 했다. Task 8도 같은 클라이언트 규칙을 쓴다.
- 승인 snapshot은 확장·보완하지 않는다. 접수 시 현재 에셋에서 v2 `AssetDetails`를 고정하고 저장 envelope v1과 내보내기 payload/manifest v1·v2를 별도로 검증한다. 격자 변경은 `CompletedExportId`를 지우지 않으며 API는 현재 export일 때만 ID를 공개한다.

#### 전체 회귀 실행 근거

아래 명령의 실행 위치는 저장소 루트다. 원본 로그는 당시 `.superpowers/sdd/2026-10-14-2d-asset-roles/`에 수집했으며 이 절에 명령·revision·결과·범위를 남겨 임시 기록 정리 뒤에도 근거가 유지되게 했다. 같은 애플리케이션 코드·의존성·검사 설정에 대한 결과를 재사용하며 Task 9에서 비싼 전체 회귀를 중복 실행하지 않았다.

| 검증 revision | 실제 명령 | 결과·범위 | 당시 로그 |
| --- | --- | --- | --- |
| Backend `99094b9` | `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'` | 1971 통과·0 실패·0 건너뜀, 12분53초. 격리 SQL Server·Redis Testcontainers 포함, 유료 smoke 제외. 이후 `0362628`까지 Backend 코드 동일 | `backend-full.log` |
| `a2cfdaa` | `dotnet ef migrations has-pending-model-changes --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure` | Build 성공, `No changes have been made to the model since the last migration.` 이후 Backend 모델 동일 | `model-check.log` |
| Frontend `0362628` 구현 코드 | `pnpm test` | 60파일·526 통과. `.test.ts` 수집이며 `.test.tsx` 미수집 | `task8-unit.log` |
| Frontend `0362628` 구현 코드 | `pnpm lint` / `pnpm typecheck` / `pnpm build` | 모두 성공. 초기 JS gzip 115.00kB / 예산 122.55kB | `task8-lint.log`·`task8-typecheck.log`·`task8-build.log` |
| Frontend `0362628` 구현 코드 | `pnpm test:e2e --config playwright.task8.config.ts` | 368 통과, 7.2분. Fake API UI·PNG·ZIP/manifest 검사 | `task8-e2e.log` |

Task 8 E2E의 실행 당시 임시 설정은 기존 Playwright config를 import하고 baseURL·webServer preview 포트·준비 URL을 모두 `127.0.0.1:4188`로 맞췄으며 `reuseExistingServer:false`를 썼다. 프로젝트·testDir·assertion·worker는 유지하고 fresh build를 사용했다. 기존 4173 서버는 재사용·종료하지 않았다. 임시 `playwright.task8.config.ts`는 커밋하지 않고 검증 후 제거했다. 실행 중 소스 변경은 없었으며 최종 포맷과 커밋 훅도 통과했다.

연결 근거는 Task 6 실제 DTO·서버 JSON 옵션으로 만든 fixture를 Task 7의 `getJob`→`apiRequest`→`readSpriteJob`로 읽고, 생성한 요청을 fixture와 깊게 비교한 뒤 Task 6 실제 MVC 바인딩으로 검사한 것이다. 실제 Backend HTTP 호스트 호출은 아니다. Task 2 역할·격자·캔버스 SQL 왕복과 Task 5 구형 승인→v2 완료 SQL 왕복·실제 ZIP writer 검사도 수행했다. Fake E2E만으로 SQL/서버 연결이 검증되었다고 계산하지 않는다.

#### 남은 검증과 제약

- 2026-10-14 당시 전체 변경의 마지막 독립 리뷰와 Task 1 Layers 역할 검사의 grid 선검사 제거, Task 6 유한 grid API 검사의 유효 에셋 입력, Task 8 Object 자기 캔버스 단정이 남아 있었다. 해당 항목의 수정·최종 검증은 아래 2026-10-15 기록에 보존한다.
- v1 frozen retry와 실제 v1 ZIP writer 회귀는 각각 통과했다. 실제 SQL의 실패한 v1 공정→writer 재시도를 하나로 연결한 신규 통합 사례는 추가하지 않았다.
- 1440px·390px 역할/격자 화면 PNG를 확인했고 390px `document.scrollWidth <= 390` E2E 단정은 통과했다. PNG는 앱 내부 스크롤의 현재 viewport이며 전체 패널 높이 캡처가 아니다.
- 실제 AI·유료 smoke·운영 DB migration 적용·실 HTTP 호출·push·merge·배포는 수행하지 않았다. Fake 검증은 실제 공급자의 역할별 생성 품질을 보장하지 않는다.
- 기존 SSH.NET `NU1903`, Skia `DrawBitmap` obsolete·xUnit warning과 Vite 큰 chunk warning은 이번 범위에서 변경하지 않았다. 초기 JS budget은 통과했다.

Task 9 문서 검증: `pnpm docs:check`와 `git diff --check` 통과. 문서 검사는 기존 두 계획의 500행 초과 경고만 냈다. `DESIGN.md`·`PRODUCT.md`·기존 배경 설계의 후속 링크·코드 지도·이 실행 기록만 경로 지정 stage하여 `docs: record 2d asset roles implementation`으로 커밋한다. 애플리케이션 소스 변경·전체 회귀 재실행·push·merge·배포는 없다.

### 2026-10-15 최종 리뷰 보완

최초 전체 리뷰에서 P2 1건과 P3 3건을 확인했다. 새 격자 B의 export가 먼저 완료하면 `TaskExecution`의 종료 작업 가드가 격자 A의 실행 중 ZIP을 버리는 문제가 있었고, Layers 잘못된 역할 검사는 grid 선검사에, 유한 grid API 검사는 빈 assets·Layers 입력에 가려졌다. Object 미리보기는 명시적인 자기 캔버스 단정이 부족했다. 아래는 수정·검증 기록이며 독립 재리뷰 결과는 이 절의 마지막에 별도로 기록한다.

- 공통 lease 갱신·최종 반영에서 소유권이 유지되는 실행 중 v2 grid-only pack에 한해 성공·부분 성공 종료를 허용한다. 실패·취소 종료, 다른 공정, 최초 claim 현재성, attempt·rowversion·lease, 승인·대상 목록 검사는 유지한다. 새 export의 작업 상태와 `CompletedExportId`를 유지한다.
- 실제 `ExportAsync`·`RunSpritePackTaskHandler`로 B를 완료한 뒤 A를 마무리하는 즉시 반영(0ms)과 lease 갱신(1200ms) 회귀를 추가했다. A는 과거·B는 현재이며 두 Blob을 유지한다. 기존 취소·attempt 변경·승인 metadata 변경 거부 사례도 유지한다.
- 역할 검사는 Layers grid를 null로 하고 정확한 역할 오류를 단정한다. API 검사는 유효 Tiles assets로 0·음수·1 초과의 격자 오류·revision 불변을 단정하며, `1e400`은 fingerprint 전 비유한 입력 방어 메시지를 별도로 단정한다.
- Object를 직접 선택하고 단일 이미지 수와 SVG `viewBox=0 0 64 36`, 이미지 width/height 64/36을 단정했다. Frontend 애플리케이션 코드는 바꾸지 않았다.

검증 기준은 `0fa653e` 위의 최종 보완 diff다. 아래 로그는 `.superpowers/sdd/2026-10-14-2d-asset-roles/`에 있으며 ignored 로그는 커밋하지 않는다.

| 명령 | 결과 | 로그 |
| --- | --- | --- |
| `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~GridChangedDuringPack\|FullyQualifiedName~Validate_RejectsRolesOutsideOutputKind\|FullyQualifiedName~JsonBinding_InvalidGrid'` | RED 11 통과·3 실패. B 완료 후 A의 두 경로가 Skipped. 나머지 1건은 비유한 grid의 기존 일반 오류 메시지에 대한 테스트 기대 수정 | `final-fix-red.log` |
| `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpriteCommandTests\|FullyQualifiedName~SpriteRulesTests\|FullyQualifiedName~SpriteApiTests\|FullyQualifiedName~RunTaskHandlerTests\|FullyQualifiedName~Concurrent'` | GREEN 동작 관련 포함 210 통과·SQL 초기화 2 실패·0 건너뜀. `StateOnlyConcurrentEdits_UseJobsRowVersion`, `ConcurrentReconstructCreate_ViolatesUniqueIndex_TranslatesToConcurrencyConflictException`의 migration 단계 SqlException timeout | `final-fix-green.log` |
| `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~StateOnlyConcurrentEdits_UseJobsRowVersion\|FullyQualifiedName~ConcurrentReconstructCreate_ViolatesUniqueIndex_TranslatesToConcurrencyConflictException\|FullyQualifiedName~GridChangedDuringPack\|FullyQualifiedName~RunTaskHandlerTests'` | 실패 2건 재실행과 공유 실행 경계 26 통과·0 실패·0 건너뜀, 45초 | `final-fix-retry.log` |
| `dotnet build apps/backend/Noxtend.slnx` | 성공, 오류 0·기존 NU1903 경고 4 | `final-fix-build.log` |
| `pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-roles.spec.ts --config playwright.finalfix.config.ts` | 3 통과, 13.8초. 기존 config 기반 fresh build·전용 4195·`reuseExistingServer:false`, testDir/project/assertion 유지. 임시 config는 실행 후 제거 | `final-fix-e2e.log` |
| `pnpm lint` / `pnpm typecheck` | 모두 통과 | `final-fix-lint.log`·`final-fix-typecheck.log` |
| `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'` | 1973 통과·0 실패·0 건너뜀, 11분2초. 격리 SQL Server·Redis 포함, exit 0 | `final-fix-full.log` |

첫 GREEN 로그의 xUnit 실패 시각은 55초·69초이지만 최종 경과는 46분19초다. 장시간 지연 원인·호스트 잠자기는 확인되지 않았으며 환경 원인으로 단정하지 않는다. 동일 소스의 실패 2건 재실행은 통과했다. 검사 중 애플리케이션·테스트 소스 변경은 없었다. Frontend 전체 526 단위·368 E2E는 앞선 동일 애플리케이션 검증 기록을 유지하고 이번에는 역할 E2E만 다시 실행했다. 실제 AI·유료 smoke·push·merge·배포는 수행하지 않았다. 수정 실행자는 독립 재리뷰를 수행하지 않았으며 아래 별도 리뷰어의 결과를 구분한다.


#### 최종 상태 — 2026-10-15

최초 전체 리뷰 → P2 1건·P3 3건 수정 → 별도 `roles_final_rereview`의 수정 범위 재리뷰를 완료했다. 재리뷰는 네 지적 모두 ADDRESSED, 신규 Critical/Important 없음, code quality approved로 보고했다. 수정·지도 커밋은 `916e1b5`이며 이 코드의 전체 Backend 회귀 1973건과 관련 Frontend 역할 E2E 3건·lint·typecheck·fresh build가 통과했다. 검사 실행 중 애플리케이션·테스트 소스는 바꾸지 않았다. `pnpm docs:check`, `git diff --check`, 경로 지정 stage의 비밀값 패턴 점검과 커밋 훅도 통과했다.

Tasks 1–9 구현·전체 리뷰 지적 보완·수정 범위 독립 재리뷰·필수 로컬 검증은 완료했다. 10월 14일의 검증과 최초 실패 기록은 당시 근거로 보존한다. 실제 AI 품질·유료 smoke·운영 DB migration·실 HTTP 호출·push·merge·배포는 미수행이며, v1 실패 공정 SQL→writer 재시도를 하나로 연결한 신규 통합 사례도 추가하지 않았다. 기존 NU1903·Skia/xUnit·Vite chunk 경고는 별도 제약으로 남는다.
