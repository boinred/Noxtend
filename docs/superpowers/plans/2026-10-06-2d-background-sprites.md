# 2D Background Sprites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 이미지에서 2D 배경 레이어 또는 반복 타일을 만들고, 선택한 대상의 루프 애니메이션을 검수하여 PNG·프레임·sprite sheet·manifest ZIP으로 내보낸다.

**Architecture:** 기존 SQL 작업 애그리게이트에 제작 모드와 sprite 상태를 추가하고 기존 TaskExecution·Worker·Redis 디스패치 경로를 확장한다. 이미지 공급자·기록·rate limit·Blob과 기존 IImageTranscoder를 재사용하며, 화면은 서버 상태와 기존 TanStack Query를 따른다. 새 실행 엔진·이미지 라이브러리·텍스트 입력용 추상은 만들지 않는다.

**Tech Stack:** .NET 10, EF Core SQL Server, Redis Streams, SkiaSharp 4.151.1, System.IO.Compression, React 19, TypeScript, TanStack Query, Radix, Tailwind, Vitest, Playwright; Node.js >=24, pnpm 11.9.0.

**Spec:** [승인한 설계](../specs/2026-10-06-2d-background-sprites-design.md). 기준 코드: `19bad98`. 이 문서의 신규 타입·테스트·명령은 구현할 계약이며 현재 구현 완료나 테스트 통과를 뜻하지 않는다.

## Global Constraints

- 범위는 이미지 입력의 2D 배경이며 `sideView / topDown / isometric`, `layers / tiles`를 작업마다 선택한다.
- 기존 3D URL·요청·모델·프롬프트·enum 숫자·JSON 읽기를 보존한다. 메시 없는 기존 작업도 `ThreeD`다.
- 입력은 PNG·JPEG·WebP, 최대 12 MiB; 디코딩은 최대 16,777,216픽셀이다.
- 레이어는 원본 비율·긴 변 최대 1024px·확대 없음; 반올림 후 각 변은 최소 1px이다.
- 타일 너비는 64·128·256px, 기본 128; 정사각 또는 아이소메트릭 2:1 다이아몬드다.
- 반복은 X·Y·Both, 기본 Both; 다이아몬드는 격자 두 축을 사용한다.
- 정적 기본 1프레임; 루프 opt-in은 4·8프레임, 기본 8; FPS는 1..30, 기본 8이다.
- 대상은 1..12개; 승인 묶음은 기준 이미지를 포함해 최대 64프레임이다.
- 동작 설명은 최대 500자이며 프롬프트 변수로 재해석하지 않고 데이터로 전달한다.
- ROI는 유한한 정규화 좌표, 양수 넓이·높이, 원본 내부; 레이어 앵커 (0,0), 타일 앵커 셀 중심이다.
- 원본+승인 기준 이미지를 프레임마다 참조한다. 위상은 `index/frameCount`이며 마지막 프레임을 첫 프레임 복제로 만들지 않는다.
- 모든 프레임에 같은 캔버스·앵커·contain 변환을 사용한다. Order는 back-to-front 오름차순이며 중복을 거부한다. diamond와 맨 뒤 이외 layer는 RequiresTransparency=true가 필수다. 자동 trim·회전·프레임별 재중앙 정렬을 하지 않는다.
- 생성 응답은 기존 32 MiB 바이트 상한도 유지하며 픽셀·실제 알파·비어 있지 않은 콘텐츠를 검사한다.
- 투명 지원과 생성 크기는 모델별 확인된 계약만 사용한다. 지원 정보가 없으면 승인 전에 거부한다.
- SQL 상태가 정본이다. 요청 ID·SHA-256 fingerprint·접수 응답과 상태 변경을 한 트랜잭션에 저장한다.
- 같은 요청 ID 조회는 revision 확인보다 먼저; 다른 본문은 409; 새 요청의 낡은 revision도 409다.
- 실행 중 대상의 생성 입력 변경·같은 슬롯 재생성은 409; canceled 작업은 다시 열지 않는다.
- succeeded는 전체 요청 대상의 승인과 패키지 완료 후; 일부를 명시적으로 종료하면 partiallySucceeded다.
- 생성 일부 실패는 쓸 결과가 있으면 pendingReview; 전부 쓸 결과가 없으면 failed다.
- ZIP manifest schemaVersion=1; 시트는 행 우선·2px extrusion·최대 4096×4096·다중 페이지다.
- ZIP은 최대 256 MiB, 한 페이지·한 입력 프레임씩 처리한다. 상한 초과를 성공이나 자동 축소로 숨기지 않는다.
- 검증은 Fake·격리된 Testcontainers·픽셀 fixture를 사용한다. 실 AI·배포·개발 DB migration 적용·push는 이 계획 실행에 포함하지 않는다.
- C# 스타일을 유지하며 일반 주석은 `//`와 짧은 명사구를 사용한다. 코드 변경과 관련 xHuman 문서를 같은 커밋에 넣는다.

## Review Focus

1. **원본 삭제:** 기존 결과로 시작한 2D 작업의 독립 Blob이 원본 작업 삭제 후에도 읽히는가 — Task 7 `SourceJobDeletion_KeepsCopiedSpriteInput`.
2. **위장 이미지:** PNG MIME·흰 배경·체크무늬·압축된 거대 이미지가 알파/디코딩 검증을 우회하는가 — Task 3 `OpaqueCheckerboard_IsNotTransparency`, `OversizedHeader_RejectsBeforePixelAllocation`.
3. **중복 과금:** 응답 유실 뒤 승인 재전송·서로 다른 본문·SQL 동시 접수가 새 공정을 만드는가 — Task 9 `DuplicateApproval_AfterRevisionChange_ReturnsReceipt`, `ConcurrentRequest_CreatesOneTaskSet`.
4. **늦은 결과:** 다른 대상 편집·취소·프레임 교체 중 완료한 워커가 최신 결과나 export 성공을 덮는가 — Task 8 `StaleFrame_DoesNotReplaceCurrentSlot`, Task 9 `StalePackage_IsHistoryOnly`.
5. **모드 혼동:** 홈·직접 URL·모바일 메뉴에서 동일한 배경 라벨이 잘못된 제작 화면으로 연결되는가 — Task 14 `LegacyAndSpriteJobs_RouteByMode`, `MobileGroups_HaveDistinctAccessibleNames`.

---

## 파일 배치와 책임

아래에서 `B=apps/backend`, `F=apps/frontend`다. 경로는 저장소 루트 기준이며 brace 표기는 각각의 파일이다. 새 파일은 해당 Task의 Create에만 만들고 미리 빈 파일을 만들지 않는다.

| 영역 | 파일 | 책임 |
| --- | --- | --- |
| Domain | `B/Noxtend.Domain/Job/{ProductionMode.cs,PipelineJob.Sprites.cs}` | 모드와 기존 작업의 2D 진입·준비·상태 연결 |
| Domain | `B/Noxtend.Domain/Sprites/{SpriteTypes.cs,SpriteRules.cs,SpritePipelineState.cs,SpriteInputs.cs,SpriteManifest.cs}` | 값·검증·소유 상태·고정 입력·내보내기 계약 |
| Application | `B/Noxtend.Application/Sprites/{SpriteCommands.cs,SpriteCommandsHandler.cs,StartSpriteJobHandler.cs,SpritePlanParser.cs}` | 접수·검수·중복 요청·분석 해석 |
| Application | `B/Noxtend.Application/Sprites/{RunSpriteAnalysisTaskHandler.cs,RunSpriteGenerationTaskHandler.cs,RunSpritePackTaskHandler.cs,SpritePackageWriter.cs}` | 기존 실행기 안의 단계 본문과 ZIP 작성 |
| Infrastructure | `B/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.Sprites.cs` | 기존 Port의 sprite 픽셀 처리 |
| Infrastructure | `B/Noxtend.Infrastructure/Persistence/Configurations/SpritePipelineConfiguration.cs` | SQL 소유 관계·제약·JSON |
| API | `B/Noxtend.Api/{Contracts/SpriteContracts.cs,Controllers/SpriteJobsController.cs}` | 새 요청·응답·파일 경로; 기존 JobsController 보존 |
| Frontend domain | `F/src/domain/sprites/{types.ts,rules.ts,playback.ts}` | 계약·입력 검증·프레임 선택 계산 |
| Frontend infra/app | `F/src/infra/api/spriteApi.ts`, `F/src/app/queries/useSprites.ts` | 기존 client와 job 쿼리 재사용 |
| Frontend features | `F/src/features/screens/sprites/{SpriteStudioScreen.tsx,SpriteInput.tsx,SpritePlanReview.tsx,SpriteFrameReview.tsx,SpritePreview.tsx,SpriteExport.tsx}` | 입력·검수·재생·내보내기 UI |
| 기존 파일 | `TaskKind.cs`, `LlmOperationKind.cs`, `IJobRepository.cs`, `IImageProvider.cs`, `IImageTranscoder.cs`, EF/InMemory, Worker 등록, routes/shell/home | 새 경계 연결; 단계별 정확한 경로는 아래 Files |
| 검증 | `B/Noxtend.Tests/{Domain,Application,Infrastructure,Api}/*Sprite*Tests.cs`, `F/src/**/*.test.ts`, `F/tests/e2e/sprites-*.spec.ts` | 기존 테스트 도구·fixture 재사용 |

### 공유 타입 계약

`SpriteTypes.cs`에 아래 값을 둔다. `Bounds`는 기존 `Noxtend.Domain.Job.Bounds`를 그대로 사용하고 2D의 엄격한 경계 확인은 `SpriteRules`에서 보완한다.

```csharp
public enum ProductionMode { ThreeD = 0, TwoD = 1 }
public enum SpriteView { SideView, TopDown, Isometric }
public enum SpriteOutputKind { Layers, Tiles }
public enum SpriteRepeat { X, Y, Both }
public enum SpriteTileLayout { Square, Diamond }
public enum SpriteRequestKind { Create, UpdatePlan, ApprovePlan, ApproveBases, RegenerateFrame, ApproveAsset, Export }
public enum SpritePhase { Analyzing, PlanReview, BaseGeneration, BaseReview, FrameGeneration, FrameReview, ExportReady, Packaging, Completed }
public sealed record SpriteCanvas(int Width, int Height);
public sealed record SpriteAnchor(double X, double Y);
public sealed record SpriteTransform(double Scale, double OffsetX, double OffsetY);
public sealed record SpriteSettings(SpriteView View, SpriteOutputKind OutputKind,
    int TileWidth = 128, SpriteRepeat Repeat = SpriteRepeat.Both);
public sealed record SpriteAssetPlan(Guid Id, string Name, int Order, Bounds SourceBounds,
    bool RequiresTransparency, bool Loop = false, int FrameCount = 8, int Fps = 8,
    string MotionNotes = "");
public sealed record SpriteReceipt(Guid JobId, JobStatus Status, int Revision,
    IReadOnlyList<Guid> TaskIds);
```

`SpritePipelineState`는 private setter와 소유 collection을 가진 class로 두고 EF 생성자는 기존 형식을 따른다. Settings·SourceCanvas·GenerationCanvas·OutputCanvas·Transform·Phase·ReviewRevision·nullable CompletedExportId, Assets·Images·Exports·Requests를 보관한다. `SpriteAsset`은 Plan·PlanRevision·Anchor·Frames·nullable Approval, `SpriteFrame`은 Index·CurrentTaskId·CurrentImageId를 가진다. `SpriteImage`에는 Id·TaskId·AssetId·FrameIndex·PlanRevision·BaseImageId·BlobKey·Width·Height·ContentType·CreatedAt, `SpriteExport`에는 Id·TaskId·Input·Manifest·BlobKey·IsCurrent·CreatedAt을 둔다. `SpriteAcceptedRequest`는 RequestId·JobId·Kind·Fingerprint·Receipt를 가진다. 모든 collection은 애그리게이트의 메서드로 변경한다.

`SpriteAsset.Approval`의 타입은 `SpriteAssetApproval(int PlanRevision, SpriteApprovedAsset Snapshot)?`, request Kind의 타입은 `SpriteRequestKind`다. `SpriteImage.Create(Guid taskId, SpriteFrameInput input, string blobKey, DateTimeOffset now) -> SpriteImage`는 PNG metadata를 input.Canvas로 고정하고 새 ID를 만든다. `SpriteExport.Create(Guid taskId, SpriteExportInput input, SpriteManifest manifest, string blobKey, DateTimeOffset now) -> SpriteExport`, `SpriteAcceptedRequest.Create(Guid requestId, Guid jobId, SpriteRequestKind kind, string fingerprint, SpriteReceipt receipt) -> SpriteAcceptedRequest`도 같은 파일에 둔다.

상태 변경의 정상적인 거부는 기존 `Result<T>`로 반환한다. 아래의 신규 값은 해당 Task에서 구현하며, 아직 존재하는 타입으로 간주하지 않는다.

`SpriteInputs.cs`에 고정 입력을, `SpriteManifest.cs`에 파일 좌표를 둔다.

```csharp
public sealed record SpriteFrameInput(Guid AssetId, SpriteAssetPlan Plan, int FrameIndex, int PlanRevision,
    Guid? BaseImageId, SpriteCanvas GenerationCanvas, SpriteCanvas Canvas, SpriteTransform Transform);
public sealed record SpriteApprovedAsset(Guid Id, string Name, int Order, int Fps,
    bool Loop, SpriteAnchor Anchor, SpriteRepeat Repeat, SpriteTileLayout Layout,
    Guid BaseImageId, IReadOnlyList<Guid> ImageIds);
public sealed record SpriteAssetApproval(int PlanRevision, SpriteApprovedAsset Snapshot);
public sealed record SpriteExportInput(int SchemaVersion, Guid ExportId, int ReviewRevision,
    SpriteView View, SpriteOutputKind OutputKind, SpriteCanvas SourceCanvas,
    SpriteCanvas OutputCanvas, IReadOnlyList<SpriteApprovedAsset> Assets,
    IReadOnlyList<Guid> ExcludedAssetIds);
public sealed record SpriteRect(int X, int Y, int Width, int Height);
public sealed record SpriteSheetCell(Guid ImageId, int FrameIndex, SpriteRect Rect);
public sealed record SpriteSheetLayout(int Page, SpriteCanvas Canvas,
    IReadOnlyList<SpriteSheetCell> Cells);
public sealed record SpriteManifestFrame(Guid ImageId, int Index, string Path,
    string SheetPath, int Page, SpriteRect Rect);
public sealed record SpriteManifestAsset(SpriteApprovedAsset Asset, string BasePath,
    IReadOnlyList<SpriteManifestFrame> Frames);
public sealed record SpriteManifest(int SchemaVersion, Guid JobId, ProductionMode ProductionMode, SpriteExportInput Input,
    string CoordinateOrigin, string CoordinateUnits, IReadOnlyList<SpriteManifestAsset> Assets);
```

Manifest JSON은 camelCase, `coordinateOrigin="topLeft"`, `coordinateUnits="pixels"`다. includedAssetIds는 Input.Assets의 ID, excludedAssetIds는 Input.ExcludedAssetIds로 명시한다. BlobKey·API key·절대 경로는 내보내지 않는다.

테스트 코드 블록은 이름과 핵심 assertion 계약이다. 전체 테스트 파일을 복사하는 코드가 아니며, 각 Task에서 명시한 기존 fixture 또는 작은 로컬 Arrange로 변수를 준비하고 실제 xUnit/Vitest 본문을 작성한 다음 red/green 명령을 실행한다. green은 해당 필터의 테스트가 실제 수집되어 모두 통과한 결과를 뜻한다. 사용하지 않은 테스트를 통과로 기록하지 않는다.

## Task 1: 제작 모드와 2D 입력 규칙

**Files:** Create `B/Noxtend.Domain/Job/ProductionMode.cs`, `B/Noxtend.Domain/Sprites/{SpriteTypes.cs,SpriteRules.cs,SpritePipelineState.cs}`, `B/Noxtend.Domain/Job/PipelineJob.Sprites.cs`, `B/Noxtend.Tests/Domain/SpriteRulesTests.cs`. Modify `B/Noxtend.Domain/Job/PipelineJob.cs`, `B/Noxtend.Domain/Common/Result.cs`, `docs/xHuman/backend.md`.

**Interfaces:** `SpriteRules.ValidateSettings(SpriteSettings settings, SpriteCanvas source) -> Result<bool>`는 분석 전 입력만 검사한다. 기존 `PipelineJob.Create(...)`는 ThreeD를 만든다. `CreateSprites(Guid sourceImageId, Guid imageProviderConfigId, string imageModel, SpriteSettings settings, SpriteCanvas sourceCanvas, SpriteCanvas generationCanvas, DateTimeOffset now) -> Result<PipelineJob>`. `SpriteRules.Validate(SpriteSettings settings, SpriteCanvas source, IReadOnlyList<SpriteAssetPlan> assets) -> Result<bool>`, `OutputCanvas(settings, source) -> SpriteCanvas`. 새 작업은 Background·TwoD·Analyzing이고 아직 공정을 만들지 않는다.

- [x] 아래 테스트와 입력별 Theory를 작성한다. 픽셀·범위·프레임·FPS·대상·notes·64프레임 경계의 바로 안/밖을 모두 검사한다.

```csharp
[Fact] public void OutputCanvas_Layers_PreservesRatioWithoutUpscale()
{
    var settings = new SpriteSettings(SpriteView.SideView, SpriteOutputKind.Layers);
    Assert.Equal(new SpriteCanvas(1024, 512), SpriteRules.OutputCanvas(settings, new(2048, 1024)));
    Assert.Equal(new SpriteCanvas(640, 320), SpriteRules.OutputCanvas(settings, new(640, 320)));
}
[Fact] public void OutputCanvas_IsometricTile_UsesDiamondCell()
    => Assert.Equal(new SpriteCanvas(128, 64),
        SpriteRules.OutputCanvas(new(SpriteView.Isometric, SpriteOutputKind.Tiles), new(800, 600)));
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpriteRulesTests` 실행; 처음은 신규 심볼 미정의로 실패해야 한다.
- [x] 기존 PipelineJob을 partial로 바꾸고 위 타입·검증만 구현한다. 유한값·`X+W<=1`·`Y+H<=1`을 검사하며 기존 3D Bounds의 오차 허용을 바꾸지 않는다. `Transform(SpriteCanvas generation, SpriteCanvas output) -> SpriteTransform`은 순수 계산이므로 여기서 구현해 CreateSprites가 고정 변환을 저장하게 한다. SpriteImage/Export/AcceptedRequest와 그 factory는 Task 2의 입력·manifest 타입과 함께 구현하며 Task 1에는 초기 state 필드만 둔다.
- [x] 같은 명령을 재실행해 통과시킨다. `LegacyCreate_WithoutMesh_RemainsThreeD`도 추가하여 기존 생성 경로를 검증한다.
- [x] `GeneratedImageRepositoryTests` 등에서 아직 매핑하지 않은 sprite 타입의 EF 자동 탐색 실패가 실제 재현되면 `B/Noxtend.Infrastructure/Persistence/Configurations/PipelineJobConfiguration.cs`에서 Sprites/ProductionMode만 임시 Ignore한다. Task 4가 매핑·migration과 함께 제거하며 영속화 완료로 기록하지 않는다.
- [x] backend 문서에 모드와 초기 상한을 동기화하고 이번 파일만 `feat(domain): add sprite production settings`로 커밋한다.

## Task 2: 슬롯·승인·revision·상태 도메인

**Files:** Create `B/Noxtend.Domain/Sprites/{SpriteInputs.cs,SpriteManifest.cs}`, `B/Noxtend.Tests/Domain/SpriteLifecycleTests.cs`. Modify Task 1의 state·partial 파일, `B/Noxtend.Domain/Job/{PipelineTask.cs,PipelineJob.cs}`, `docs/xHuman/backend.md`.

**Interfaces:** `PipelineTask.SpriteInput: SpriteFrameInput?`, `SpriteExportInput: SpriteExportInput?`, `RequestId: Guid?`를 추가한다. PipelineJob에 `ReplaceSpritePlan(IReadOnlyList<SpriteAssetPlan> plans, int expectedRevision) -> Result<bool>`, `ApproveSpritePlan(int expectedRevision) -> Result<IReadOnlyList<SpriteFrameInput>>`, `ApproveSpriteBases(IReadOnlyList<Guid> assetIds, int expectedRevision) -> Result<IReadOnlyList<SpriteFrameInput>>`, `ApproveSpriteAsset(Guid assetId, int expectedRevision) -> Result<bool>`, `CaptureSpriteExport(IReadOnlyList<Guid> assetIds, int expectedRevision) -> Result<SpriteExportInput>`, `TryAttachSpriteExport(SpriteExport export) -> bool`를 추가한다. `RegenerateSpriteFrame(Guid assetId, int index, int expectedRevision) -> Result<SpriteFrameInput>`, `BindSpriteFrame(Guid taskId, SpriteFrameInput input)`, `TryAttachSpriteImage(SpriteImage image) -> bool`와 `IsCurrentTask(PipelineTask task) -> bool`가 슬롯과 늦은 결과를 연결한다.

- [x] `SpriteLifecycleTests`에 최소 다음 상태 시나리오와 assertion을 구현한다. 테스트별 실제 image/slot fixture는 이 파일의 private 메서드로 만들고 새 테스트 프레임워크를 만들지 않는다.

```csharp
[Fact] public void StaticBaseApproval_RequiresExportBeforeSuccess();
[Fact] public void LoopBaseApproval_PlansOnlyFramesOneThroughSeven();
[Fact] public void GeneratingInputChange_InvalidatesOnlyAffectedAsset();
[Fact] public void MetadataChange_PreservesImagesAndInvalidatesExport();
[Fact] public void Regeneration_UsesNewTaskAndPreservesImageHistory();
[Fact] public void WrongModeAndCanceled_RejectSpriteMutations();
```

Assertions: 정적 승인 뒤 `Status==PendingReview`·`Phase==ExportReady`; 루프 입력 index는 `[1,2,3,4,5,6,7]`이고 BaseImageId 동일; FPS 변경 뒤 image IDs 불변·revision 증가; 기준 교체 뒤 후속 슬롯/승인 무효; running 대상 편집은 `SPRITE_BUSY`; canceled 재개는 `JOB_ALREADY_TERMINAL`.

```csharp
Assert.Equal(JobStatus.PendingReview, job.Status);
Assert.Equal(SpritePhase.ExportReady, job.Sprites!.Phase);
Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, inputs.Select(x => x.FrameIndex));
Assert.All(inputs, x => Assert.Equal(baseImageId, x.BaseImageId));
Assert.Equal(oldImageIds, job.Sprites.Assets[0].Frames.Select(x => x.CurrentImageId));
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpriteLifecycleTests`로 신규 동작의 실패를 확인한다.
- [x] 상태 메서드와 에러 코드를 구현한다. 생성 입력 변경만 해당 asset PlanRevision을 증가시키고 표시·결과·승인 변경은 ReviewRevision을 증가시킨다. FPS/name/order만 바뀌면 기존 프레임과 PlanRevision을 유지한다. SpriteFrameInput.Plan은 생성 접수 시의 immutable snapshot이며 worker는 frameCount/motion/transparency를 현재 편집 상태에서 다시 읽지 않는다. 공통 view/kind/canvas는 수정 계약에 넣지 않는다. 요청 expectedRevision은 전역 검수 revision을 검사하며 비동기 export 유효성은 포함 승인 snapshot과 고정 task input을 비교한다. 다른 대상의 변경은 기존·진행 export를 보존하고 대상 추가·제거는 included/excluded 의미가 바뀌므로 모두 무효화한다. CompletedExportId는 이미 종료에 사용한 export를 기록하여 명시적 reopen 후 같은 과거 결과로 다시 종료하지 않게 한다.
- [x] `PlanReadyFollowUpTasks / IsReadyToRun / ReconcileFromTasks`에 모드별 분기만 추가한다. frame input을 반환하는 승인과 실제 task 생성은 Task 8에서 같은 트랜잭션으로 연결한다.
- [x] 같은 테스트를 통과시키고 기존 `PartGenerationPlanningTests / JobLifecycleTests`도 실행한다. 도메인 문서를 동기화해 `feat(domain): add sprite review and frame lifecycle`로 커밋한다.

## Task 3: 디코딩·투명 PNG·공통 좌표 변환

**Files:** Create `B/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.Sprites.cs`, `B/Noxtend.Tests/Infrastructure/SpritePixelTests.cs`. Modify `B/Noxtend.Domain/Ports/IImageTranscoder.cs`, `B/Noxtend.Domain/Sprites/SpriteTypes.cs`, `B/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.cs`, 실제 Port 구현·테스트 stub, `docs/xHuman/providers-and-prompts.md`.

**Interfaces:** 기존 Port에 `InspectSpriteAsync(Stream image, long maxBytes, long maxPixels, CancellationToken ct) -> Task<SpriteImageInfo>`와 `NormalizeSpriteAsync(Stream image, SpriteCanvas canvas, SpriteTransform transform, bool requireTransparency, SpriteTileLayout layout, CancellationToken ct) -> Task<Stream>`를 추가한다. `SpriteImageInfo(int Width,int Height,bool HasTransparentPixels,bool HasVisiblePixels)`를 정의한다. 실패는 기존 공급자 응답/이미지 검증 예외 경로로 변환한다.

- [x] 유효 RGBA, 전부 투명, 불투명 흰색/체크무늬, PNG MIME의 비이미지, 16,777,217픽셀 헤더 fixture를 테스트 파일에서 작은 byte 배열/Skia로 만든다.

```csharp
[Fact] public Task OpaqueCheckerboard_IsNotTransparency();
[Fact] public Task OversizedHeader_RejectsBeforePixelAllocation();
[Fact] public Task AllTransparentImage_IsRejected();
[Fact] public Task Normalize_PreservesAlphaAndFixedOrigin();
[Fact] public Task DiamondCell_HasTransparentCornersAndVisibleCenter();
```

Assertions: opaque 출력의 requireTransparency 검증 실패; maxBytes 초과와 overflow 즉시 실패; alpha=128 픽셀은 PNG round-trip 후 유지; 두 프레임에서 동일한 좌표의 표식은 이동/재중앙 정렬되지 않음; diamond 네 모서리 alpha=0.

```csharp
Assert.False(info.HasTransparentPixels);
await Assert.ThrowsAsync<ProviderBadResponseException>(() => normalizeOpaqueWithTransparency);
Assert.Equal(128, decodedAlpha);
Assert.Equal(0, diamondCornerAlpha);
Assert.True(info.HasVisiblePixels);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpritePixelTests`로 실패를 확인한다.
- [x] 기존 Skia 구현을 partial로 확장한다. 헤더·64비트 픽셀 곱·상한을 allocation 전에 확인하고 EXIF 방향을 반영한다. 공급자 원본의 실제 알파를 mask/contain 전에 검사하여 인공 padding이 투명 지원을 대신하지 않도록 한다.
- [x] fixed contain transform을 한 번 정하고, 프레임별 콘텐츠 bbox를 사용하지 않는다. 평가용 `NormalizeToFrameAsync`의 불투명 배경 채우기를 재사용하지 않는다.
- [x] 신규 테스트와 `SkiaImageTranscoderTests`를 통과시키고 문서를 동기화해 `feat(image): validate and normalize sprite png output`로 커밋한다.

## Task 4: SQL 저장·고유 제약·이전 작업 호환

**Files:** Create `B/Noxtend.Infrastructure/Persistence/Configurations/SpritePipelineConfiguration.cs`, `B/Noxtend.Infrastructure/Persistence/SpriteJsonSerializer.cs`, `B/Noxtend.Tests/Infrastructure/SpritePersistenceTests.cs`. Modify `B/Noxtend.Domain/Ports/IJobRepository.cs`, `B/Noxtend.Domain/Sprites/SpritePipelineState.cs`의 영속 ID·동일 슬롯 갱신, `B/Noxtend.Infrastructure/Persistence/{Configurations/PipelineJobConfiguration.cs,Repositories/EfJobRepository.cs,InMemory/InMemoryJobRepository.cs}`, `B/Noxtend.Application/Pipeline/ListJobsHandler.cs`, `B/Noxtend.Tests/Domain/SpriteLifecycleTests.cs`의 임시 Ignore assertion과 실제 IJobRepository 테스트 구현, `docs/xHuman/backend.md`. EF 생성 파일: `Migrations/<timestamp>_AddSpriteProduction.cs`, Designer, snapshot.

**Interfaces:** `IJobRepository.GetSpriteRequestAsync(Guid requestId, CancellationToken ct) -> Task<SpriteAcceptedRequest?>`를 추가한다. CountAsync/ListAsync와 ListJobsHandler의 기존 인수 끝에 `ProductionMode? productionMode = null`을 추가해 기존 호출을 유지한다. request는 job이 소유하고 `AcceptSpriteRequest(SpriteAcceptedRequest request)`로 기록한다. 기존 `SaveChangesAsync`가 상태·task·request를 함께 저장한다.

- [x] 기존 `SqlServerFixture.FreshDatabase`와 `[Collection(SqlServerCollection.Name)]`로 round-trip·이전 migration 갱신·slot 중복·request 중복·삭제를 검증한다.

```csharp
[Fact] public Task LegacyJobWithoutMesh_MigratesToThreeD();
[Fact] public Task SpriteState_RoundTripsAllSnapshotsAndHistory();
[Fact] public Task DuplicateAssetFrameIndex_IsRejectedBySql();
[Fact] public Task DuplicateRequestId_IsRejectedBySql();
[Fact] public Task ModeFilteredListAndCount_UseSamePredicate();
```

Assertions: 이전 job의 mode=ThreeD·Sprites=null·기존 JSON 불변; (assetId,index) unique; requestId global unique; pendingReview는 active count에 포함; 알 수 없는 schemaVersion·잘못된 JSON은 명시적으로 실패.

```csharp
Assert.Equal(ProductionMode.ThreeD, legacy.ProductionMode);
Assert.Null(legacy.Sprites);
Assert.Equal(expectedActiveTwoDCount, page.Total);
await Assert.ThrowsAsync<DbUpdateException>(() => saveDuplicateSlot);
await Assert.ThrowsAsync<DbUpdateException>(() => saveDuplicateRequest);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpritePersistenceTests`로 실패를 확인한다. Docker가 없으면 환경 제약으로 기록하고 통과로 계산하지 않는다.
- [x] Task 1의 Sprites/ProductionMode와 Task 2의 SpriteInput/SpriteExportInput/RequestId 임시 Ignore가 있으면 모두 제거한다. Jobs에 nullable owned state, SpriteAssets/Frames/Images/Exports/Requests 테이블을 매핑한다. ApprovedBaseImageId와 CompletedExportId도 저장·round-trip한다. Jobs/Tasks rowversion을 유지하고 소유 관계·FK·cascade 경로를 SQL로 검증한다. JSON의 image ID·Blob key를 검증 없이 사용하지 않는다.
- [x] owned request 조회는 Jobs에서 projection으로 수행한다. 이미지·export 이력은 job이 소유하며 제작 대상 제거가 과거 snapshot의 결과를 삭제하지 않게 한다.
- [x] `dotnet ef --version`을 확인한다. 도구가 없거나 버전이 다르면 실행 환경의 임시 tool-path에 EF 패키지와 같은 `10.0.10`을 설치해 사용한다. 기존 전역 도구나 저장소 의존성을 바꾸지 않는다.
- [x] `dotnet ef migrations add AddSpriteProduction --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure`를 실행하고 생성 SQL을 리뷰한다. DesignTimeDbContextFactory를 사용하며 개발 DB에 update를 실행하지 않는다.
- [x] 같은 테스트를 통과시키고 `ReviewConcurrencyTests / JobListFilterTests / MigrationRegistrationTests`를 실행한다. 스키마 문서와 함께 `feat(storage): persist sprite state and request receipts`로 커밋한다.

## Task 5: sprite sheet와 스트리밍 ZIP

**Files:** Create `B/Noxtend.Application/Sprites/SpritePackageWriter.cs`, `B/Noxtend.Tests/{Domain/SpriteSheetLayoutTests.cs,Infrastructure/SpritePackageTests.cs}`. Modify `SpriteRules.cs`, `SpriteManifest.cs`, `IImageTranscoder.cs`, `SkiaImageTranscoder.Sprites.cs`, Port stubs, `B/Noxtend.Application/Noxtend.Application.csproj`의 테스트 assembly 가시성, `docs/xHuman/backend.md`.

**Interfaces:** `SpriteRules.SheetPages(SpriteCanvas frame,IReadOnlyList<Guid> imageIds) -> IReadOnlyList<SpriteSheetLayout>`. Port에 `WriteSpriteSheetAsync(SpriteSheetLayout layout, Func<Guid,CancellationToken,Task<Stream>> openFrame, Stream output, CancellationToken ct) -> Task`를 추가한다. `SpritePackageWriter.WriteAsync(Guid jobId,SpriteExportInput input,Func<Guid,CancellationToken,Task<Stream>> openFrame,Stream output,CancellationToken ct) -> Task<SpriteManifest>`는 실제 이미지를 읽고 정해진 상대 경로만 생성한다.

- [x] 시트 배치 단위 테스트와 작은 PNG/ZIP 픽셀 fixture를 작성한다.

```csharp
[Fact] public void Frame1024_UsesThreeColumnsAndSecondPageAfterNineFrames();
[Fact] public Task Padding_ExtrudesTwoPixelsWithoutChangingRect();
[Fact] public Task Zip_ContainsManifestBasesFramesAndSheets();
[Fact] public Task PackageLimit_StopsAt256MiBWithoutPublishingSuccess();
```

Assertions: 1024px 셀 stride=1028, 첫 rect=(2,2,1024,1024), 열 번째는 page=1; 프레임 순서는 index 오름차순; manifest와 실제 PNG 크기 일치; static은 1프레임; ZIP에 `../`·절대 경로·Blob key 없음.

```csharp
Assert.Equal(new SpriteRect(2, 2, 1024, 1024), pages[0].Cells[0].Rect);
Assert.Equal(1, pages[1].Page);
Assert.Equal(1, manifest.SchemaVersion);
Assert.Equal("topLeft", manifest.CoordinateOrigin);
Assert.Equal("pixels", manifest.CoordinateUnits);
Assert.DoesNotContain(entries, x => x.Contains("../") || Path.IsPathRooted(x));
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpriteSheetLayoutTests|FullyQualifiedName~SpritePackageTests'`로 실패를 확인한다.
- [x] 페이지를 최대 4096px에 맞추고 Skia에서 한 페이지 canvas와 한 프레임 decode만 유지한다. 회전·trim 없이 2px extrusion을 셀 안에 배치하고 rect에서 padding을 제외한다.
- [x] `ZipArchive`와 바이트 상한을 검사하는 write stream을 사용한다. worker가 전달하는 서버 임시 FileStream에 순차 기록하며 ZIP 전체를 MemoryStream에 올리지 않는다. 256MiB 경계는 상한 stream에 chunk를 쓰는 작은 테스트로 확인한다.
- [x] `manifest.json`, `layers|tiles/asset-{assetId}.png`, `frames/asset-{assetId}/frame-000.png`, `sheets/asset-{assetId}-000.png`를 생성해 같은 테스트를 통과시킨다. export 계약을 동기화하고 `feat(export): build sprite sheets and bounded zip packages`로 커밋한다.

## Task 6: 모델별 크기·투명 요청·호출 기록

**Files:** Modify `B/Noxtend.Domain/Sprites/SpriteRules.cs`, `B/Noxtend.Domain/Ports/{IImageProvider.cs,IModelCatalog.cs}`, `B/Noxtend.Infrastructure/Image/{ImageModels.cs,OpenAiImageProvider.cs,GoogleImageProvider.cs,RecordingImageProvider.cs}`, `B/Noxtend.Api/Contracts/ProviderResponse.cs`, `B/Noxtend.Tests/Application/{OpenAiImageRequestTests.cs,ImageModelCatalogTests.cs}`, `B/Noxtend.Tests/Infrastructure/{SeedModelPricesTests.cs,ModelPriceMigrationTests.cs}`, `docs/xHuman/providers-and-prompts.md`. Create `B/Noxtend.Tests/Application/SpriteProviderRequestTests.cs`.

**Interfaces:** `SpriteRules.GenerationCanvas(SpriteCanvas output, IReadOnlyList<SpriteCanvas> supported) -> SpriteCanvas`는 가장 가까운 비율의 합법적 size를 고르고, `Transform(SpriteCanvas generation, SpriteCanvas output) -> SpriteTransform`은 공통 contain 변환을 반환한다. `ImageRequest` 끝에 `ImageBackground? Background = null`, `ImageCallContext` 끝에 `TaskKind Kind = TaskKind.Generate`를 추가한다. 기존 PartId는 `Guid?`로 바꿔 sprite에서 null을 사용한다. `ImageBackground { Opaque, Transparent }`, `ReferenceRole.SpriteBase=2`를 추가한다. `ProviderModel`과 wire model에 optional `SpriteImageCapabilities? Sprite`를 추가한다. capability는 `SpriteImageCapabilities(bool SupportsTransparency, IReadOnlyList<SpriteCanvas> Sizes)` record로 정의한다. sprite 상관관계는 Tasks의 asset/index와 TaskId로 조회하며 LlmCalls에 중복 열을 추가하지 않는다.

- [x] 기존 captured HTTP handler로 legacy와 sprite의 request body·multipart를 검증한다.

```csharp
[Fact] public Task SpriteRequest_SendsExplicitPngBackgroundAndSize();
[Fact] public Task LegacyRequest_KeepsExistingQualityAndBodyDefaults();
[Fact] public Task UnknownTransparency_RejectsBeforeImageProviderCall();
[Fact] public Task Recording_LegacyContextKeepsGenerateOperation();
```

Assertions: 투명 sprite는 `background=transparent`·`output_format=png`, 지정 size는 JSON/edit 양쪽에 전달; 불투명 PNG는 투명으로 판정하지 않음; 기존 quality=medium 유지; catalog의 GPT image 모델은 response_format 없이 output_format=png로 요청하며 기존 3D의 quality·size·base64 응답 동작은 보존; 기록에는 version/model/task 포함, bytes/key 제외.

```csharp
Assert.Equal("transparent", background);
Assert.Equal("png", outputFormat);
Assert.Equal("1536x768", size);
Assert.Equal("medium", quality);
Assert.Equal(0, rejectedProviderCallCount);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpriteProviderRequestTests`로 실패를 확인한다.
- [x] [OpenAI 공식 이미지 가이드](https://developers.openai.com/api/docs/guides/image-generation)에서 투명 배경 요청이 명시된 `gpt-image-2.5-sunburst`를 기존 catalog에 추가하고 실제 remote model 목록과 교차한다. 기존 gpt-image-2는 유지한다. 초기 2D 생성 size는 `1024x1024 / 1536x1024 / 1024x1536 / 1536x768 / 768x1536 / 1536x864 / 864x1536`로 제한하고 최종 캔버스와의 비율 차이는 고정 contain으로 처리한다.
- [x] 기존 `ImageModels.FakeModels`에도 동일한 크기·투명 capability를 명시해 Fake 접수를 가능하게 한다. capability가 없는 Google/다른 모델은 2D 후보에서 '지원 미확인'으로 선택을 막는다. 기존 Google 3D 기본값을 유지하고, 명시된 미지원 배경/size를 조용히 1:1로 대체하지 않는다. 추가 모델 지원은 공식 계약 확인과 captured request 테스트를 함께 넣는 후속 변경으로 제한한다.
- [x] 같은 테스트와 `OpenAiImageRequestTests / ImageProviderUsageTests / ImageModelCatalogTests`를 통과시키고 문서와 `feat(providers): support explicit sprite image requests`로 커밋한다.

## Task 7: 2D 접수·분석 worker·독립 입력 복사

**Files:** Create `B/Noxtend.Application/Sprites/{SpriteCommands.cs,StartSpriteJobHandler.cs,SpritePlanParser.cs,RunSpriteAnalysisTaskHandler.cs}`, `B/Noxtend.Tests/Application/SpriteAnalysisTests.cs`. Modify `B/Noxtend.Domain/Sprites/SpriteTypes.cs`의 실제 decode ContentType, `B/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.Sprites.cs`, 관련 `B/Noxtend.Tests/Infrastructure/SpritePixelTests.cs` fixture, `B/Noxtend.Domain/Job/TaskKind.cs`, `B/Noxtend.Domain/Llm/LlmOperationKind.cs`, `B/Noxtend.Domain/Prompt/PromptTemplate.cs`, `B/Noxtend.Infrastructure/Llm/{SeedPrompts.cs,FakeLlmProvider.cs}`, `B/Noxtend.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `B/Noxtend.Api/Workers/TaskWorkerRegistration.cs`, `B/Noxtend.Tests/Application/{PipelineFixture.cs,PromptGridTests.cs}`, `B/Noxtend.Tests/Infrastructure/SpritePersistenceTests.cs`의 접수 경합·복사 원자성 검증, `B/Noxtend.Tests/Infrastructure/PromptCategoryPersistenceTests.cs`, `B/Noxtend.Tuning.Application/Prompts/PromptHandlers.cs`, `F/src/domain/{tuning/types.ts,job/types.ts,job/backendParity.test.ts}`, `F/src/features/screens/admin/{PromptsScreen.tsx,PromptEditScreen.tsx}`, `F/tests/e2e/{fakeApi.ts,prompts-category.spec.ts,decomposition-admin.spec.ts}`, `docs/xHuman/{backend.md,providers-and-prompts.md}`. EF 생성 파일: `SeedSpriteAnalyzePrompt` migration.

**Interfaces:** `StartSpriteJobCommand(Guid RequestId,Guid? UploadId,Guid? SourceJobId,Guid? SourceGeneratedImageId,Guid ProviderConfigId,string Model,Guid ImageProviderConfigId,string ImageModel,SpriteSettings Settings)`. `StartSpriteJobHandler.HandleAsync(command,ct) -> Task<Result<SpriteReceipt>>`. `SpritePlanParser.Parse(string json,SpriteSettings settings,SpriteCanvas source) -> Result<IReadOnlyList<SpriteAssetPlan>>`. worker는 기존 `ITaskHandler.HandleAsync(Guid taskId,CancellationToken ct) -> Task<RunTaskOutcome>`를 구현한다.

- [x] PipelineFixture의 repositories·Blob·clock·Fake provider를 재사용하고 분석 결과를 테스트용 JSON으로 지정한다.

```csharp
[Fact] public Task InputSelection_RequiresExactlyOneSource();
[Fact] public Task SourceJobDeletion_KeepsCopiedSpriteInput();
[Fact] public Task AnalysisSuccess_StopsAtPlanReviewWithoutImageCalls();
[Fact] public Task InvalidRoiOrUnknownView_FailsWithoutSilentCorrection();
[Fact] public Task SourceImageFromOtherJob_IsRejected();
```

Assertions: upload와 source pair의 동시 입력/둘 다 누락/절반 입력 거부; 복사 Blob key는 원본과 다름; 원본 job 삭제 후에도 복사본 decode 가능; 분석 성공은 pendingReview·이미지 생성 호출 0; 단가 누락은 unknown.

```csharp
Assert.NotEqual(sourceBlobKey, copiedInput.BlobKey);
Assert.True(await blobExistsAfterSourceDeletion);
Assert.Equal(JobStatus.PendingReview, job.Status);
Assert.Equal(SpritePhase.PlanReview, job.Sprites!.Phase);
Assert.Equal(0, imageCallCount);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpriteAnalysisTests`로 실패를 확인한다.
- [x] `TaskKind.AnalyzeSprites=7`, `LlmOperationKind.AnalyzeSprites=5`와 명시적 mapping을 추가한다. operation switch·PromptTemplate 변수·price/grid/fake 열거를 보완한다. 분석 prompt 변수는 `settings / sourceCanvas`이며 JSON으로 삽입한다. notes의 `{{...}}`는 재확장하지 않는다.
- [x] 서버에서 원본 소속·decode·모델·active prompt를 검증한다. 모델의 합법적 GenerationCanvas와 출력 Transform을 작업 접수 때 고정하며 이후 프레임마다 다시 고르지 않는다. 업로드는 실제 codec MIME과 저장 MIME의 일치를 검증한 뒤 기존 StoredImage·Blob을 재사용하고 기존 SourceImageId 참조 수에 따른 삭제를 유지한다. MIME 불일치는 명시적으로 거부한다. 기존 생성 결과만 새 StoredImage·새 Blob으로 복사한다. 중복 request는 복사보다 먼저 반환하고 SQL 접수 경쟁에서 진 요청은 자신이 만든 미참조 복사본만 정리하며 공유 업로드를 삭제하지 않는다.
- [x] FakeLlmProvider의 기본 AnalyzeSprites 응답도 동일한 schema로 추가한다. AnalyzeSprites만 worker/DI에 등록하고 기존 JobOptions lease를 사용한다. GenerateSprite/PackSprites의 빈 handler를 미리 만들지 않는다. parser는 schema·대상 상한·name/order/ROI를 검사하며 asset ID는 서버가 부여한다.
- [x] `SeedSpriteAnalyzePrompt` migration을 생성해 새 operation의 Background prompt만 등록한다. 기존 prompt 수정·개발 DB 적용은 하지 않는다.
- [x] 같은 테스트와 `TaskWorkerRegistrationTests / PromptCategoryWiringTests / SeedPromptVariableTests`를 통과시키고 대응 TS operation 테스트도 실행한다. 문서와 `feat(sprites): accept and analyze image-based background jobs`로 커밋한다.

## Task 8: 기준·후속 프레임 생성과 오래된 결과 차단

**Files:** Create `B/Noxtend.Application/Sprites/RunSpriteGenerationTaskHandler.cs`, `B/Noxtend.Tests/Application/SpriteGenerationTests.cs`. Modify Task 7의 enum/operation/prompt/DI/worker/fixture, `B/Noxtend.Infrastructure/Image/FakeImageProvider.cs`, `B/Noxtend.Domain/Job/{PipelineJob.Sprites.cs,PipelineTask.cs}`, `B/Noxtend.Application/Pipeline/TaskExecution.cs`, `B/Noxtend.Infrastructure/Persistence/Repositories/EfTuningRepositories.cs`의 호출 기록 Context 소유, `B/Noxtend.Tests/Infrastructure/{ServiceRegistrationTests.cs,SpritePersistenceTests.cs}`의 DI·대상 migration 검증, `B/Noxtend.Api/Workers/ReclaimPlan.cs`, `B/Noxtend.Infrastructure/ProviderHttp.cs`와 OpenAI 텍스트·이미지 어댑터의 quota 분류, 관련 `B/Noxtend.Tests/Application/OpenAiProviderTests.cs`의 텍스트·이미지 공통 quota 검증과 기존 `SeedPromptVariableTests.cs`, `docs/xHuman/{backend.md,providers-and-prompts.md}`. EF 생성 파일: `SeedSpriteGeneratePrompt` migration.

**Interfaces:** `TaskKind.GenerateSprite=8`, `LlmOperationKind.GenerateSprite=6`. `PipelineJob.PlanSpriteFrames(IReadOnlyList<SpriteFrameInput> inputs,Guid requestId) -> IReadOnlyList<PipelineTask>`가 슬롯과 공정을 연결한다. worker는 기존 ITaskHandler·ImageRequest·TaskExecution.RunAsync 계약을 유지한다. frame0은 Original, index>=1은 Original+SpriteBase를 참조한다.

- [x] `SpriteGenerationTests`에 아래 Fake provider·SQL 경쟁 테스트를 작성한다. 완료를 제어할 수 있는 Fake로 요청 시작→상태 변경→완료 순서를 고정한다.

```csharp
[Fact] public Task BaseGeneration_DoesNotStartLoopFramesBeforeApproval();
[Fact] public Task EightFrameLoop_UsesFrozenBaseAndPhasesZeroThroughSevenEighths();
[Fact] public Task StaleFrame_DoesNotReplaceCurrentSlot();
[Fact] public Task CancelBetweenFinalRenewalAndCommit_DoesNotPublishImage();
[Fact] public Task AuthenticationQuotaAndDecodeOverflow_DoNotRetry();
[Fact] public Task SpriteRecording_UsesSpriteOperationAndNoImageBytes();
```

Assertions: phase=0,0.125,..,0.875; index>=1의 base ID 동일; frame0을 마지막에 복제하지 않음; 불일치 taskId/planRevision의 결과는 slot 불변; 완료 직전 취소는 이미지 미공개; 429는 기존 backoff/rate headers, 인증/할당량 소진/미지원 shape는 즉시 실패.

```csharp
Assert.Equal(new[] { 0d, .125, .25, .375, .5, .625, .75, .875 }, phases);
Assert.Equal(oldCurrentImageId, slot.CurrentImageId);
Assert.Equal(LlmOperationKind.GenerateSprite, recorded.Context.Kind);
Assert.DoesNotContain(imageBase64, recorded.RequestPayload);
Assert.Equal(1, nonRetryableAttemptCount);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpriteGenerationTests`로 실패를 확인한다.
- [x] 원본·고정 base·SpriteFrameInput.Plan snapshot·canvas·phase를 별도 변수로 전달한다. 생성 prompt의 허용 변수는 `settings / asset / frame / sourceCanvas / outputCanvas`이며 JSON 데이터로 삽입한다. 3D ViewDirection을 프레임에 쓰지 않는다. SeedSpriteGeneratePrompt를 생성한다.
- [x] 공급자는 기존 Factory·Recording·RateLimitGate를 통과한다. raw 응답의 실제 PNG 형식·MIME과 크기가 고정 GenerationCanvas와 일치하는지 확인하고 불일치는 비재시도 오류로 처리한다. Task 3 이미지 검사·PNG 정규화·Blob 저장→SQL 결과 공개 순서를 지킨다. 미공개 새 Blob만 정리하고 기존 이력을 삭제하지 않는다. FakeImageProvider의 기본 성공·지연 응답은 GenerateSprite 요청일 때 요청 size의 디코딩 가능한 RGBA PNG를 만든다. 기존 12-byte PNG 헤더를 2D 성공 fixture로 쓰지 않고, Returning의 명시적 잘못된 응답은 그대로 유지한다.
- [x] 공통 TaskExecution의 commit 직전에 최신 aggregate/task를 reload해 `IsCurrentTask`·canceled·시도 소유권을 확인한다. Claim 저장 후 기존 Task.RowVersion을 기억하고 자기 renewal 저장 성공에서만 갱신하여, 수동 retry가 AttemptCount를 초기화해도 옛 응답을 거부한다. 루프 중지는 timer만 취소하고 진행 중인 SQL renewal은 끝까지 await한 후 최종 조회한다. sprite 조건은 Domain에 두며 body/commit 계약을 유지한다. 기존 3D 수동 재시도·병렬 asset 결과 반영·recording과 renewal의 SQL 경합을 회귀 검증한다.
- [x] GenerateSprite에 기존 GenerationOptions lease와 재시도 한도를 적용하고 ReclaimPlan idle도 해당 lease×2로 정한다. generationWorkers 등록을 재사용하면 3D+2D 합계 동시 실행 수가 늘므로 기존 공급자 RateLimitGate 공유를 테스트한다.
- [x] 같은 테스트와 `RunGenerationTaskHandlerTests / JobLifecycleTests / ReclaimPlanTests / ImageProviderUsageTests`를 통과시키고 문서와 `feat(sprites): generate reviewed bases and animation frames`로 커밋한다.

## Task 9: 중복 접수·검수 명령·pack worker

**Files:** Create `B/Noxtend.Application/Sprites/{SpriteCommandsHandler.cs,RunSpritePackTaskHandler.cs}`, `B/Noxtend.Tests/Application/SpriteCommandTests.cs`, `B/Noxtend.Tests/Infrastructure/SpriteRequestConcurrencyTests.cs`. Modify `SpriteCommands.cs`, `B/Noxtend.Domain/Common/Result.cs`의 request 충돌 상수, `StartSpriteJobHandler.cs`와 `SpriteAnalysisTests.cs`의 같은 ID·다른 본문 충돌 계약, `F/src/domain/job/{types.ts,backendParity.test.ts}`의 PackSprites label·wire와 `F/src/domain/tuning/types.ts`의 non-LLM 제외, Domain state/partial/TaskKind, DI/worker/fixture, `B/Noxtend.Application/Pipeline/RetryTaskHandler.cs`, 관련 `B/Noxtend.Tests/Domain/{LlmOperationTests.cs,SpriteLifecycleTests.cs}`, `docs/xHuman/backend.md`. 기존 DeleteJobHandler·EF/InMemory sprite PNG/ZIP key 수집은 실제 삭제 테스트로 검증해 재사용한다.

**Interfaces:** `SpriteCommandContext(Guid JobId,Guid RequestId,int ExpectedRevision)`. handler는 `UpdatePlanAsync(context,IReadOnlyList<SpriteAssetPlan> plans,ct)`, `ApprovePlanAsync(context,ct)`, `ApproveBasesAsync(context,IReadOnlyList<Guid> assetIds,ct)`, `RegenerateAsync(context,Guid assetId,int index,ct)`, `ApproveAssetAsync(context,Guid assetId,ct)`, `ExportAsync(context,IReadOnlyList<Guid> assetIds,ct)`를 제공하며 모두 `Task<Result<SpriteReceipt>>`다. `TaskKind.PackSprites=9`는 LLM operation으로 매핑하지 않는다. worker는 Task 5 writer를 사용한다.

- [x] 실제 SQL에서 같은 request를 별도 DbContext로 동시에 제출한다. 본문은 고정 property 순서·ID 집합 정렬의 JSON과 SHA256 hex로 정규화하며 route의 job/asset/index·operation 종류도 fingerprint에 포함한다.

```csharp
[Fact] public Task DuplicateApproval_AfterRevisionChange_ReturnsReceipt();
[Fact] public Task ConcurrentRequest_CreatesOneTaskSet();
[Fact] public Task SameRequestIdDifferentBody_ReturnsConflict();
[Fact] public Task PartialExport_RequiresExplicitApprovedAssetSelection();
[Fact] public Task StalePackage_IsHistoryOnly();
[Fact] public Task PackRetry_UsesFrozenInputAndMakesNoImageCalls();
```

Assertions: 중복 request의 Receipt/TaskIds 완전 일치·task 수 불변; 다른 본문=`SPRITE_REQUEST_CONFLICT`; 낡은 revision=`SPRITE_REVISION_CONFLICT`; 생성 완료 후 package 전은 pendingReview; 명시적 subset 종료는 partiallySucceeded; 오래된 export는 IsCurrent=false·현재 작업 성공으로 확정하지 않음.

```csharp
Assert.Equal(firstReceipt.JobId, duplicateReceipt.JobId);
Assert.Equal(firstReceipt.Revision, duplicateReceipt.Revision);
Assert.Equal(firstReceipt.TaskIds, duplicateReceipt.TaskIds);
Assert.Equal(firstTaskIds, tasksAfterDuplicate.Select(x => x.Id));
Assert.Equal("SPRITE_REQUEST_CONFLICT", conflictingBody.ErrorCode);
Assert.Equal("SPRITE_REVISION_CONFLICT", staleRevision.ErrorCode);
Assert.False(staleExport.IsCurrent);
Assert.Equal(0, packImageCallCount);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpriteCommandTests|FullyQualifiedName~SpriteRequestConcurrencyTests'`로 실패를 확인한다.
- [x] request 조회→fingerprint 확인→새 요청만 revision/state 검사→state/task/receipt 함께 Save→dispatch 순서를 구현한다. 진행 중인 분석이 늦게 계획을 덮어쓰지 않도록 분석 대기·실행 중 계획 편집을 거부하고, 실패 분석 retry는 더 새로운 계획이 없는 현재 분석에 한정한다. 최초 request 조회 miss와 job 조회 사이에 접수된 승자 receipt도 state/revision 검사보다 먼저 재확인한다. unique/rowversion 경쟁은 reload 후 기존 receipt를 반환하거나 409로 처리한다. plan/base approval fan-out은 같은 transaction에 저장한다.
- [x] 프레임 재생성은 새 task/result로, failed retry는 기존 규칙으로 처리한다. 종료 작업의 명시적 재생성/재시도/export는 Pending으로 되돌리고 CompletedAt을 비운다. canceled는 재개를 거부하며 무관한 asset 승인은 유지한다.
- [x] export는 서버에서 현재 Approval의 image IDs·metadata를 고정한다. pack worker는 임시 파일→상한 ZIP→Blob→snapshot 검사 순서로 공개한다. 임시 파일은 finally에서 삭제한다. 취소 후 새 export를 공개하지 않고 오래된 snapshot은 이력으로만 보존하며 pack 실패에도 PNG를 유지한다.
- [x] 전체 요청 대상의 승인·package 완료만 succeeded다. 명시적인 subset package 완료 또는 쓸 이미지가 있는 pack 확정 실패는 partiallySucceeded다. 생성 실패만으로 부분 성공 처리하지 않는다. 2D PNG/ZIP key도 기존 IBlobStorage 삭제 목록에 넣는다.
- [x] 같은 테스트와 `TaskWorkerRegistrationTests / ReclaimPlanTests / JobLifecycleTests`를 통과시키고 문서와 `feat(sprites): add idempotent review and export commands`로 커밋한다.

## Task 10: HTTP 계약·상세·파일 응답

**Files:** Create `B/Noxtend.Api/{Contracts/SpriteContracts.cs,Controllers/SpriteJobsController.cs}`, `B/Noxtend.Tests/Api/SpriteApiTests.cs`. Modify `B/Noxtend.Api/{Controllers/JobsController.cs,Contracts/{JobResponse.cs,ApiResults.cs}}`, `B/Noxtend.Tests/Api/JobResponseTests.cs`, 기존 3D 전용 접수 경계 `B/Noxtend.Application/{Scene/GetSceneLayoutHandler.cs,Scene/SceneRevisionHandlers.cs,Similarity/StartSimilarityRunHandler.cs}`와 `B/Noxtend.Tests/Application/{SceneLayoutHandlerTests.cs,SimilarityStartHandlerTests.cs}`, `docs/xHuman/backend.md`.

**Interfaces:** spec의 신규 sprite 경로와 기존 상세/목록 확장을 구현한다. mutation은 Task 7/9 command로 변환하고 accepted `{id,status,revision,taskIds}`를 기존 JSON 봉투로 반환한다. 상세에는 `productionMode: threeD|twoD`와 nullable `sprite`, 목록에는 mode와 phase/count 요약을 추가한다. productionMode query는 선택 사항이며 잘못된 값은 400이다. items와 total은 같은 조건을 사용한다.

- [x] `SpriteApiTests`로 POST 접수→GET→plan/base/asset 승인→export와 각 mutation의 409·잘못된 mode·다른 job의 ID·없는 대상을 검증한다.

```csharp
[Fact] public Task WrongModeEndpoint_RejectsWithoutPlanningTasks();
[Fact] public Task ForeignAssetImageAndExportIds_AreRejected();
[Fact] public Task ImageAndZipDownloads_SetVerifiedContentTypeAndServerFilename();
[Fact] public Task JsonEnvelope_ContainsSpriteRevisionAndLegacyFields();
```

Assertions: request에 Blob key/URL을 받지 않음; image=image/png, package=application/zip; 미완성 export 다운로드 거부; 다른 job의 ID는 기존 not-found 응답; API에 key/절대 경로 제외.

```csharp
Assert.Equal("image/png", pngResponse.Content.Headers.ContentType!.MediaType);
Assert.Equal("application/zip", zipResponse.Content.Headers.ContentType!.MediaType);
Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
Assert.DoesNotContain(blobKey, responseJson);
```

- [x] `dotnet test apps/backend/Noxtend.slnx --filter FullyQualifiedName~SpriteApiTests`로 실패를 확인한다.
- [x] request DTO를 파싱한 뒤 Domain 검증을 적용한다. status/error의 HTTP 매핑은 기존 방식을 따른다. 파일 조회는 주소 job의 소유 결과만 읽고 서버가 저장한 key로 연다.
- [x] 2D 작업에서 3D mesh/views/기존 review API 호출도 거부한다. 기존 scene-layout 생성·복원과 similarity 접수는 공통 Application 진입점에서 2D를 거부해 3D 상태 저장·평가 dispatch를 막는다. 기존 POST /api/jobs와 다운로드 계약은 유지한다.
- [x] 같은 테스트와 `JobResponseTests`를 통과시킨다. API 계약을 다음 Task의 TS와 대조하고 문서와 `feat(api): expose sprite production and artifact contracts`로 커밋한다.

## Task 11: TypeScript 계약·API·query cache

**Files:** Create `F/src/domain/sprites/{types.ts,rules.ts,rules.test.ts}`, `F/src/infra/api/{spriteApi.ts,spriteApi.test.ts}`, `F/src/app/queries/useSprites.ts`. Modify `F/src/domain/{job/types.ts,job/backendParity.test.ts,provider/types.ts}`, `F/src/infra/api/{jobApi.ts,jobApi.test.ts}`, `F/src/app/queries/{keys.ts,useJob.ts,useJobList.ts,media.ts}`, `docs/xHuman/frontend.md`.

**Interfaces:** C# records와 camelCase·nullable을 맞춘다. `startSpriteJob(input:StartSpriteJobInput,signal?:AbortSignal):Promise<SpriteAccepted>`와 `updateSpritePlan / approveSpritePlan / approveSpriteBases / regenerateSpriteFrame / approveSpriteAsset / exportSprites`는 Task 10의 요청을 사용한다. StartSpriteJobInput은 upload/source pair의 discriminated union과 공통 settings/model/requestId다. `SpriteMutationContext={jobId:string;requestId:string;expectedRevision:number}`, `SpriteAccepted={id:string;status:JobStatus;revision:number;taskIds:string[]}`.

- [ ] API 요청 본문과 순수 규칙을 Vitest로 검증한다.

```typescript
it('legacy productionMode defaults to threeD without inferring mesh', () => {
  expect(productionModeOf({ productionMode: undefined })).toBe('threeD')
})
it('job list caches separate modes and limits', () => {
  expect(queryKeys.jobList('active', 10, 'twoD')).not.toEqual(
    queryKeys.jobList('active', 20, 'threeD'),
  )
})
```

- [ ] `pnpm --filter @nextend/frontend test src/domain/sprites/rules.test.ts src/infra/api/spriteApi.test.ts`로 실패를 확인한다.
- [ ] `productionModeOf(job:{productionMode?:ProductionMode}):ProductionMode`를 domain에 둔다. 기존 apiRequest 봉투 처리를 재사용하며 HTTP 실패나 알 수 없는 sprite 값을 빈 상태로 숨기지 않는다. `getJob` 응답 경계에서 신규 필드의 enum·필수 필드·배열을 검사하는 `readSpriteJob(raw: unknown): Job`를 `domain/sprites/types.ts`에 두며, legacy 누락만 ThreeD/null로 읽는다.
- [ ] `listJobs(filter,limit,signal,productionMode?)`, `useJobList(filter,limit,productionMode?)`, `queryKeys.jobList(filter,limit=10,productionMode?)`를 맞춘다. 기존 키의 limit 누락도 이 호출 변경에서 함께 보완한다. job 상세는 기존 jobId key를 공유하며 mutation 성공/409 재조회는 job과 jobLists를 invalidate한다.
- [ ] 같은 본문의 통신 재전송은 requestId를 유지하고 사용자 새 동작에만 crypto.randomUUID()를 만든다. 자동 mutation retry를 추가하지 않는다. useJob의 기존 status polling을 확장해 pendingReview와 실행 중 task 결과를 빠뜨리지 않게 한다. useJobList의 실패→빈 성공 fallback을 제거하고 error/isError를 반환한다. useJob의 isNotFound는 실제 HTTP 404만 의미하며 연결·계약 오류는 별도 반환한다.
- [ ] 같은 테스트와 `backendParity.test.ts / jobApi.test.ts`, `pnpm typecheck`를 통과시키고 문서와 `feat(frontend): add typed sprite api and queries`로 커밋한다.

## Task 12: 정적 2D 배경 스튜디오

**Files:** Create `F/src/features/screens/sprites/{SpriteStudioScreen.tsx,SpriteInput.tsx,SpritePlanReview.tsx,SpriteFrameReview.tsx,SpritePreview.tsx,SpriteExport.tsx,spriteStyles.ts}`, `F/tests/e2e/{spriteFakeApi.ts,sprites-static.spec.ts}`. Modify `F/src/routes/{paths.ts,index.tsx,prefetch.ts}`, `F/src/domain/sprites/{rules.ts,rules.test.ts}`의 정적 tileOffsets, `F/tests/e2e/fakeApi.ts`의 필요한 handler 연결만, `docs/xHuman/frontend.md`.

**Interfaces:** `ROUTES.spriteBackground='/2d/background'`, `spriteBackgroundJob='/2d/background/:jobId'`. Studio는 URL jobId와 useJob/useSprites를 사용한다. Preview props는 `{sprite:SpriteState;timeMs:number;playing:boolean}`이며 Export는 승인된 asset ID를 받는다. 정적 3×3 square/diamond 반복의 tileOffsets는 기존 rules.ts에 먼저 구현하고 Task 13이 재사용한다. 편집 draft·재생 위치 외의 단계 상태는 job 응답에서 도출한다.

- [ ] 기존 `installFakeApi(page,options)`에 작은 sprite 시나리오를 연결한다. 정적 fixture의 접수/분석/승인/결과/export 응답과 요청 기록은 새 파일에 둔다.

```typescript
test('static layers require plan and base review before export', async ({ page }) => {
  // 고정 Fake API·이미지 선택·두 단계 승인
  await expect(page.getByRole('button', { name: '내보내기' })).toBeEnabled()
  await expect(page.getByRole('button', { name: '승인', exact: true })).toHaveCount(0)
})
```

- [ ] `pnpm build`, `pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-static.spec.ts`로 아직 없는 흐름의 실패를 확인한다.
- [ ] 기존 업로드·ProviderSelect·ModelSelect·버튼·input·tooltip·tokens를 재사용한다. view/kind는 필수이며 모델 지원 거부 이유를 표시한다. 단가 미등록은 '비용 미확인', 생성 장수·모델은 계획 승인 전에 표시한다.
- [ ] 원본 ROI·depth/name 편집과 add/remove, 1..12개·64프레임 상한을 표시한다. 기준 이미지는 checkerboard에서 검수하고 layer 합성·square/diamond tile 반복을 보여준다. 선택 시점의 재구성 안내를 입력에 표시한다.
- [ ] plan/base 승인·충돌 재조회·failed task retry·명시적 subset export와 제외 대상을 연결한다. exportReady에는 pack 동작만 표시한다. 오류를 빈 카드나 임의 0 진행률로 숨기지 않는다.
- [ ] 같은 E2E에서 세 view×두 유형, 정적 1프레임, 새로고침 재개·다운로드 파일명을 확인한다. `pnpm lint / pnpm typecheck`도 통과시키고 문서와 `feat(frontend): add static 2d background studio`로 커밋한다.

## Task 13: 애니메이션 설정·재생·프레임 검수

**Files:** Create `F/src/domain/sprites/{playback.ts,playback.test.ts}`, `F/tests/e2e/sprites-animation.spec.ts`. Modify Task 12의 sprite components/fake,`docs/xHuman/frontend.md`.

**Interfaces:** `frameAt(timeMs:number,fps:number,frameCount:number):number`는 순수 계산이며 `tileOffsets(view:SpriteView,canvas:SpriteCanvas,repeat:SpriteRepeat):readonly {x:number;y:number}[]`는 Task 12의 rules.ts 함수를 재사용한다. UI는 대상별 loop/frameCount/notes/FPS, current image ID와 승인 snapshot을 사용한다. 재생 중에만 requestAnimationFrame을 쓰고 reduced-motion의 초기 상태는 paused다.

- [ ] 순수 함수 테스트와 UI E2E를 먼저 작성한다.

```typescript
it('wraps eight frames at eight fps without duplicating the end', () => {
  expect(frameAt(0, 8, 8)).toBe(0)
  expect(frameAt(875, 8, 8)).toBe(7)
  expect(frameAt(1000, 8, 8)).toBe(0)
})
test('regenerating one frame invalidates approval and keeps other assets usable')
test('fps edit updates metadata without generation requests')
```

- [ ] `pnpm --filter @nextend/frontend test src/domain/sprites/playback.test.ts`, build 후 `pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-animation.spec.ts`로 실패를 확인한다.
- [ ] 기존 입력에 4/8프레임·loop opt-in·notes≤500·play/pause/scrub/FPS를 추가한다. 각 layer는 자신의 FPS로 재생하고 tile은 diamond 격자 offset으로 반복한다. 수동 scrub은 키보드로 조작 가능하게 한다.
- [ ] 기준 이미지 승인 전 loop 생성을 막고 누락/failed/재생성 중 프레임을 표시한다. base 재생성은 후속 무효화 안내와 기준 재검수를 제공한다. 실행 중 asset 입력은 readonly, 같은 slot 동작은 disabled다.
- [ ] FPS/name/order 저장 시 AI 요청 없음, 다른 asset 결과 유지, 재조회 후 후보 일치, reduced-motion의 자동 재생 없음과 수동 재생을 같은 테스트에서 확인한다.
- [ ] `pnpm lint / pnpm typecheck`도 통과시키고 문서와 `feat(frontend): add sprite loop review and playback`로 커밋한다.

## Task 14: 홈·3D·2D 메뉴와 기존 결과 진입

**Files:** Modify `F/src/routes/{paths.ts,navItems.ts,navItems.test.ts,prefetch.ts,index.tsx}`, `F/src/features/shell/layout/{Sidebar.tsx,SidebarItem.tsx,sidebarStyles.ts}`, `F/src/features/screens/{categoryLabels.ts,home/HomeScreen.tsx,home/ActiveJobSpotlight.tsx,home/WorkStatusSection.tsx,background/BackgroundStudioScreen.tsx,character/CharacterStudioScreen.tsx,admin/CallsScreen.tsx}`, jobPath 호출과 기존 결과의 2D 진입 버튼, `F/tests/e2e/{app-shell-responsive.spec.ts,home-active-job.spec.ts}`와 기존 호출 내역의 목록 오류 검사, `PRODUCT.md`, `DESIGN.md`, `docs/xHuman/frontend.md`.

**Interfaces:** `NavGroup={key:'threeD'|'twoD';label:string;items:readonly NavItem[]}`와 NAV_GROUPS, home/footer는 별도로 둔다. `jobPath(category:string,jobId:string,productionMode:'threeD'|'twoD'='threeD'):string`. `/2d/character`, `/2d/object`는 기존 ComingSoonScreen을 사용한다.

- [ ] route/nav 단위 테스트와 responsive/home E2E를 갱신한다.

```typescript
it('LegacyAndSpriteJobs_RouteByMode', () => {
  expect(jobPath('background', 'old')).toBe('/background/old')
  expect(jobPath('background', 'new', 'twoD')).toBe('/2d/background/new')
})
test('MobileGroups_HaveDistinctAccessibleNames')
test('detail url activates its parent group and restores menu focus')
```

- [ ] `pnpm --filter @nextend/frontend test src/routes/navItems.test.ts`, build 후 `pnpm --filter @nextend/frontend test:e2e tests/e2e/app-shell-responsive.spec.ts tests/e2e/home-active-job.spec.ts`로 실패를 확인한다.
- [ ] desktop은 항상 보이는 3D/2D 제목과 하위 항목을 그린다. 그룹별 접힘 상태를 추가하지 않는다. icon rail의 tooltip/accessible name에 '3D 배경'·'2D 배경'을 포함한다.
- [ ] ≤720px에서는 home/3D/2D/admin 하단 nav를 사용하고 그룹 버튼으로 기존 Radix menu를 연다. 키보드·Escape·focus 복원·준비 중 표시를 유지한다. 3D object도 현재 준비 중 상태를 유지한다.
- [ ] 홈·기존 스튜디오·공유 작업 목록을 사용하는 호출 내역에서 Task11의 error/404/정상 빈 결과를 구분해 표시한다. 홈 카드에 mode/category를 표시하고 모든 jobPath 호출에 mode를 전달한다. 기존 결과에서는 source job/image ID를 가지고 2D 입력으로 이동한다. 외부 URL·Blob key는 넘기지 않는다. route/prefetch를 함께 갱신하고 lazy import를 유지한다.
- [ ] 1440×900·390px, 접힌 nav, 직접 job URL, legacy mode 누락, reduced-motion을 같은 테스트로 확인한다. PRODUCT/DESIGN/xHuman을 동기화하고 `feat(navigation): group studios by 3d and 2d modes`로 커밋한다.

## Task 15: 양쪽 스택 회귀·품질 경계·완료 기록

**Files:** Create `docs/superpowers/plans/2026-10-06-2d-background-sprites-execution.md`의 검증·판단 기록. Modify 본 계획의 checkbox/실행 기록과 기록 링크, `README.md`의 실제 2D 제공 범위, `docs/xHuman/{backend.md,frontend.md,providers-and-prompts.md}`, 필요한 경우 이번 Task의 실패 테스트. 관련 없는 실패 수정·일괄 포맷을 묶지 않는다.

**Interfaces:** 구현·자동 검증·수동 UI·실 AI 품질을 구분해 보고한다. 실 AI 미실행이면 반복 경계·시점 재구성·loop 자연스러움은 미확인으로 기록한다.

- [ ] API 계약을 C#/TS/Fake에서 대조한다. 새 kind/operation의 label/grid/usage, migration 순서, mode를 무시한 mesh/review 경로, Blob 삭제·request receipt 수명을 확인한다.
- [ ] `dotnet build apps/backend/Noxtend.slnx`를 실행한다.
- [ ] `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~Noxtend.Tests.Domain|FullyQualifiedName~Noxtend.Tests.Application|FullyQualifiedName~Noxtend.Tests.Api|FullyQualifiedName~Noxtend.Tests.Architecture'`를 실행한다.
- [ ] Docker 이용 시 `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`를 실행한다. 새 SQL 테스트는 기존 공유 collection으로 직렬 실행한다.
- [ ] `pnpm test`, `pnpm lint`, `pnpm typecheck`, `pnpm build`, `pnpm test:e2e`를 실행한다. Vitest 수집은 현재 vite.config.ts의 `src/**/*.test.ts`이며 DOM 동작은 Playwright에서 확인한다.
- [ ] 최신 build preview에서 1440×900/390px·키보드·focus·reduced-motion·subset 종료·401/429/500 표시·새로고침·PNG/ZIP 다운로드를 수동 확인하고 관찰 결과만 기록한다.
- [ ] PNG/manifest/ZIP fixture의 크기·alpha·anchor·프레임 순서·2px padding·여러 페이지·256MiB 경계를 대조한다.
- [ ] 각 view×kind의 입력·검수 기준·최대 호출 수를 실 AI 품질 검증용으로 기록한다. 유료 생성·smoke는 별도 사용자 요청으로 승인되기 전까지 실행하지 않는다.
- [ ] 이번 diff의 비밀값·링크·`git diff --check`·문서 동기화를 확인한다. 미실행/실패/skip을 포함해 결과를 기록하고 이번 파일만 `test(sprites): verify workflow and legacy regressions`로 커밋한다.

## 설계 요구와 작업 연결

| 설계 절 | 구현·검증 Task |
| --- | --- |
| 1 범위 / 2 기존 구현 | 1, 6, 7, 15 |
| 3 메뉴·주소 | 12, 14 |
| 4 입력·설정 | 1, 3, 6, 7, 12 |
| 5 제작·검수 | 2, 8, 9, 12, 13 |
| 6 서버 경계 | 2, 4, 7, 8, 9 |
| 7 상태·동시성 | 2, 4, 8, 9, 10 |
| 8 저장·호환 | 4, 7, 9 |
| 9 API·Frontend | 10, 11 |
| 10 생성·검증 | 3, 6, 7, 8 |
| 11 내보내기 | 5, 9, 10 |
| 12 회귀·UI 검증 | 10..15 |
| 13 후속 텍스트 입력 | 현재 범위 밖; 기존 이미지 이후 흐름 재사용 |

## 실행 방법과 리뷰

15개 Task를 순서대로 진행한다. 첫 사용 가능 지점은 Task 12의 정적 2D 배경이며 Task 13에서 같은 흐름에 loop를 추가하고 Task 14에서 메뉴·홈 연결을 완성한다. 텍스트 입력·2D 캐릭터/오브젝트·엔진 전용 importer는 후속 작업이다.

**실행: Subagent-driven.** 사용자가 정한 기본 지침과 구현 실행 요청에 따라 Task마다 새 구현 담당과 독립 reviewer를 사용하고 마지막에 전체 변경을 다시 검토한다. 현재 checkout의 `codex/2d-background-sprites` 브랜치에서 진행하며, Task의 검증·리뷰가 끝나면 다음 작업을 이어간다.

자체 검토에서는 spec 1..13·Global Constraints·Review Focus 5개와 Task의 연결, Create/Modify 구분, 의존 순서, 타입 일관성, red/green 명령의 실행 위치를 확인한다. 설계 변경이 필요한 사실을 찾으면 spec/plan을 함께 고치고 근거를 기록한다.
