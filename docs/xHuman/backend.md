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

`SpriteCommandsHandler`는 `SpriteCommandContext(JobId, RequestId, ExpectedRevision)`로 계획 편집·계획 승인·기준 승인·프레임 재생성·대상 승인·내보내기를 접수한다. operation·job/asset/index·expectedRevision과 고정 property 순서의 JSON, 정렬한 대상 ID로 SHA-256 fingerprint를 만들고 전역 요청 조회를 revision 검사보다 먼저 수행한다. 최초 조회와 작업 조회 사이에 동일 요청이 확정되는 경쟁은 작업 조회 후 mutation 검사 전 receipt 재조회로 판정하므로 이후 취소된 작업에서도 기존 접수 응답을 반환한다. 같은 요청은 기존 receipt·TaskIds를 반환하고 다른 본문·경로·operation은 `SPRITE_REQUEST_CONFLICT`, 새 요청의 낡은 검수는 `SPRITE_REVISION_CONFLICT`로 거부한다. 상태·팬아웃·receipt를 같은 SaveChanges로 저장한 다음 기존 오케스트레이터에 디스패치하며 SQL unique/rowversion 경쟁은 ReloadAsync 후 승자 receipt 또는 충돌로 반환한다. 생성 승인 때 이미지 모델의 투명 지원과 고정 생성 캔버스 capability를 확인한다.

분석 대기·실행 중 `ReplaceSpritePlan`을 거부해 늦은 분석이 편집을 덮어쓰지 않게 한다. `RetryTaskHandler`·`RetryOutputTask`는 현재 AnalyzeSprites·GenerateSprite·PackSprites 실패를 같은 공정과 고정 입력으로 다시 실행한다. 분석은 새 계획이 없는 현재 분석, 생성은 현재 빈 슬롯, pack은 현재 승인 snapshot의 최신 내보내기만 재시도한다. 명시적 프레임 재생성은 새 공정이며 무관한 승인은 유지한다. 종료된 2D 작업의 재생성·retry·export 접수는 Pending과 CompletedAt=null로 재개방하고 Worker Claim에서 Running으로 전환한다. 취소된 작업은 재개방하지 않으며 계획 편집만 하는 작업은 PendingReview다. `PackSprites=9`는 LLM operation·프롬프트를 갖지 않고 단일 일반 공정 Worker·ReclaimPlan에 등록한다.

`SpriteInputs.cs`의 `SpriteFrameInput.Plan`은 접수 시점의 계획이며 비동기 결과는 `IsCurrentTask`와 `TryAttachSpriteImage`가 대상·슬롯·입력 revision·기준 이미지를 확인해 반영한다. `SpriteManifest.cs`는 파일 좌표 계약이다. 내보내기는 승인된 이미지 snapshot을 고정하며 포함 대상 변경만 패키지 자격을 무효화한다. 대상 추가·삭제는 포함·제외 목록도 변경하므로 모든 기존 패키지 자격을 무효화한다. 정적 대상의 기준 승인만으로 성공하지 않으며 패키지 완료 후 전체 대상이면 성공, 명시적 일부 대상이면 부분 성공이다. 개별 승인 후에도 다른 현재 공정이 실행 중이면 Running과 해당 생성 단계를 유지한다. 패키지 완료 자격은 표시용 Phase 대신 가장 최근 접수된 패키지 공정의 current 자격과 승인 snapshot으로 판정하므로 제외 대상의 편집·승인이 완료를 막지 않는다. `SpritePipelineState.CompletedExportId`는 작업을 종료한 마지막 패키지 ID이며, 명시적으로 다시 연 작업을 같은 이전 패키지가 재종료시키지 않도록 유지한다. 새 ExportId 완료는 정상 반영하고 이전 패키지의 current·다운로드 이력 자격은 보존한다. 생성 일부 실패에 쓸 이미지가 있으면 검수 대기, 없으면 실패이며 취소는 재개방하지 않는다.

`SpriteRules.SheetPages`는 회전·trim 없이 행 우선으로 최대 4096×4096 페이지에 배치한다. 각 셀은 2px extrusion을 포함하며 manifest rect는 원본 프레임만 가리킨다. `IImageTranscoder.WriteSpriteSheetAsync`와 `SkiaImageTranscoder.Sprites.cs`는 한 페이지 canvas와 한 입력 decode를 유지하고 출력 스트림에 PNG를 인코딩한다.

`Noxtend.Application/Sprites/SpritePackageWriter.cs`는 승인 snapshot의 실제 PNG 크기를 고정 캔버스와 비교하고 서버가 만든 상대 경로만 `ZipArchive`에 순차 기록한다. `manifest.json`, `layers|tiles/asset-{assetId}.png`, `frames/asset-{assetId}/frame-000.png`, `sheets/asset-{assetId}-000.png`가 포함된다. ZIP 중앙 디렉터리까지 256 MiB 상한을 검사하며 초과·취소·이미지 오류에서 manifest 성공을 반환하지 않는다. 출력은 호출자가 소유하는 서버 임시 FileStream이며 ZIP 전체 메모리 버퍼를 만들지 않는다. manifest는 schemaVersion 1, TwoD, topLeft·pixels, 포함·제외 대상 ID를 명시하고 Blob 키·절대 경로를 담지 않는다. `SpriteSheetLayoutTests`와 `SpritePackageTests`가 배치·extrusion 픽셀·정적 프레임 수·ZIP 파일과 좌표·저장 JSON 호환·상한·취소를 검증한다. `RunSpritePackTaskHandler`는 기존 `TaskExecution`과 일반 공정의 리스 정책으로 실행하며 서버 임시 FileStream을 finally에서 삭제한다. 고정 입력에 포함된 작업 소유의 불변 PNG만 읽고 Blob 저장 후 최신 공정·리스·취소·승인 snapshot을 확인해 패키지를 공개한다. 취소·오래된 snapshot은 새 패키지를 공개하지 않고 자신의 미공개 ZIP Blob만 정리하며 PNG와 이미 공개된 오래된 패키지는 다운로드 이력으로 보존한다. SQL 저장 응답 유실 때는 최신 export 행을 조회해 공개된 Blob을 보존한다. 패키지 확정 실패에 쓸 PNG가 있으면 PartiallySucceeded이며 생성 실패만으로 부분 성공 처리하지 않는다.

`StartSpriteJobHandler`는 uploadId 또는 sourceJobId·sourceGeneratedImageId 쌍을 받아 기존 업로드와 두 제작 모드의 소유 결과를 확인한다. 기존 결과는 `GeneratedImages`와 `Sprites.Images`에서 해당 작업 소속을 조회한다. 분석 모델·활성 프롬프트와 이미지 모델의 확인된 투명·생성 크기 지원을 검증하고 원본을 12 MiB·16,777,216픽셀 안에서 디코딩한다. 업로드는 저장된 MIME이 실제 codec MIME과 대소문자 무시 비교로 일치해야 하며 불일치는 UploadUnsupportedType으로 거부한다. 검증된 업로드의 StoredImage·Blob을 재사용하고, 기존 작업 결과만 실제 MIME으로 새 StoredImage·독립 Blob에 복사한다. EXIF 방향 크기와 합법적 GenerationCanvas·Transform을 접수 시 고정한다. 공유 업로드는 기존 삭제 경로를 따라 마지막 SourceImageId 참조 작업이 삭제될 때 row·Blob을 제거한다.

`SpriteRequests`를 원본 조회·복사 전에 전역 조회하므로 원본 삭제 후 같은 정규화 요청은 기존 receipt를 반환한다. 다른 fingerprint는 충돌이며 접수 경쟁은 SQL 유니크 제약으로 판정한다. job·AnalyzeSprites task·receipt와 결과 복사 시 새 StoredImage는 같은 SaveChanges에 저장한다. 경쟁에서 진 요청이 만든 독립 Blob만 제거하며 재사용 업로드는 삭제하지 않는다. 최초 접수 저장 전 실패는 자신의 복사본만 정리한다. SaveChanges 호출 후 비경합 오류·취소는 요청 취소와 분리한 전역 receipt 조회로 own job의 커밋 여부를 확인한다. 커밋됐거나 조회에 실패해 불명확하면 복사본을 보존하며, 조회·정리 실패는 경고로 남기고 최초 저장 예외를 유지한다. 커밋 이후 orchestration 오류에는 이 정리를 적용하지 않는다. 공급자 모델명은 Trim 후 SHA-256 fingerprint에 포함한다.

`RunSpriteAnalysisTaskHandler`는 기존 TaskExecution·JobOptions 리스·rate limit·LLM 호출 기록 경로를 사용한다. `SpritePlanParser`는 선택 시점·유형, 정확한 응답 필드, 1~12개 대상, ROI·순서·투명·루프 제약을 검사하고 대상 ID를 서버에서 부여한다. 분석 성공은 PlanReview·PendingReview에서 멈추며 이미지 생성 공정을 만들지 않는다. TaskKind.AnalyzeSprites=7은 분석 worker로 등록한다.

`PipelineJob.PlanSpriteFrames`는 승인으로 반환된 고정 입력을 `TaskKind.GenerateSprite=8` 공정과 슬롯에 연결하고 RequestId를 기록한다. `RunSpriteGenerationTaskHandler`는 기준 프레임에 Original만, 이후 프레임에 Original과 고정 SpriteBase를 전달한다. 기준 생성 완료는 BaseReview에서 멈추며 검수 승인 이후에만 후속 프레임을 계획한다. 위상은 index/frameCount이고 마지막 프레임을 별도로 복제하지 않는다. 실제 PNG·MIME·GenerationCanvas 크기·32 MiB·픽셀·알파 검증과 고정 PNG 정규화를 거친 뒤 Blob 저장, SQL 공개 순으로 처리한다. 공개되지 않은 이번 Blob만 정리하며 저장 후 응답 유실이나 조정 실패에서도 실제 SQL 이미지 참조를 다시 확인한다. 조회가 실패해 공개 여부를 모르면 Blob을 보존하고 경고를 남긴다.

`TaskExecution`은 리스 갱신 때도 `ReloadAsync`로 실제 상태를 읽고, 마지막에는 갱신 루프를 중지·await한 뒤 다시 조회한다. 루프의 Delay만 취소하며 이미 시작한 renewal SQL은 기존 command timeout 내 완료를 기다리므로 정상 body 종료가 진행 중 split query를 취소하지 않는다. `PipelineTask.IsOwnedBy`의 Running·claimed AttemptCount·미만료 lease·마지막으로 자신이 저장한 Tasks.RowVersion과 `IsCurrentTask`를 함께 확인해 취소·회수·교체된 시도의 성공과 실패 반영을 차단한다. Tasks.RowVersion은 claim 저장 직후 복사하고 자신이 저장한 renewal 성공 뒤에만 교체하므로 수동 재시도의 AttemptCount 재사용도 이전 실행과 구분한다. rowversion 충돌은 공급자 호출을 반복하지 않고 결과 반영과 저장만 제한적으로 재시도한다. 결과 공개 이후 충돌은 상태 조정만 재시도한다. `GenerateSprite`는 기존 GenerationOptions의 lease·workers와 JobOptions.MaxAttempts를 사용하고 Redis reclaim idle은 생성 lease의 두 배다. 3D와 2D 이미지 worker는 각각 설정 수만큼 등록되므로 합계가 늘지만 공급자 ID별 RateLimitGate 상태를 공유한다.

`SpriteGenerationTests.cs`의 Fake·픽셀 검증과 같은 파일의 SQL 컬렉션 테스트는 기준 승인, 고정 위상·입력, stale 슬롯, 마지막 갱신/최종 조회 뒤 취소, 실제 회수 시도, 병렬 결과 rowversion 재시도, 호출 기록 겹침, 저장 후 응답 유실을 검증한다.

관련 검증은 `Noxtend.Tests/Application/SpriteAnalysisTests.cs`, `Infrastructure/SpritePersistenceTests.cs`, `Domain/SpriteRulesTests.cs`, `SpriteLifecycleTests.cs`다. HTTP 연결은 후속 범위다.

`SpritePipelineConfiguration`은 Jobs의 nullable owned state와 `SpriteAssets`·`SpriteImages`·`SpriteExports`·`SpriteRequests`를 매핑한다. `SpriteFrames`는 asset 소유이며 `(AssetId, Index)`가 PK다. `SpriteAsset.Id`는 생성 시 `Plan.Id`로 고정하고, 계획의 프레임 수가 바뀌면 기존 슬롯 객체를 초기화하고 초과 슬롯만 제거한다. 프레임 조회는 `Index`순이다. 이미지·export의 과거 ID는 교차 FK 없이 보존하며 대상 삭제가 이력을 삭제하지 않는다. 작업 삭제는 소유 트리 전체를 cascade로 지운다. PNG·ZIP 키는 기존 `DeletedJobBlobs.Images`에 중복 없이 수집해 `IBlobStorage` 정리 경로로 전달한다.

`SpriteJsonSerializer`는 sprite 값과 공정 고정 입력을 `{ schemaVersion: 1, value: ... }`로 저장하며 미지원 버전·잘못된 JSON·null payload·잘못된 snapshot ID/형식·상대경로가 아닌 Blob 참조를 명시적으로 거부한다. 기존 `SceneJsonSerializer`·`MeshInputSetJsonSerializer`의 3D 호환 규칙은 유지한다. `ApprovedBaseImageId`와 `CompletedExportId`도 SQL에 저장해 재조회 후 검수·완료 소비 상태를 보존한다. Jobs와 Tasks의 rowversion을 유지하고 같은 Jobs 행을 쓰는 sprite state도 rowversion을 공유한다.

`IJobRepository.GetSpriteRequestAsync`는 Jobs에서 owned requests를 projection하고 추적 없이 전역 RequestId를 조회한다. RequestId는 단일 PK이며 상태·공정·receipt는 같은 SaveChanges 트랜잭션으로 저장한다. Count/List와 `ListJobsHandler`의 마지막 선택 인수 `productionMode`는 동일 predicate를 적용하고 `PendingReview`도 active에 포함한다. Repository에서는 현재 enum 값 검증을 추가하지 않으며 HTTP 입력 검증은 API 책임이다.

저장 검증은 격리 `SqlServerFixture`의 `SpritePersistenceTests`가 round-trip·이전 migration 갱신·중복 키·rowversion·삭제·JSON 오류를 확인한다. `AddSpriteProduction`은 기존 작업에 `ThreeD` 기본값을 추가하고 sprite state 열은 nullable로 둔다. 이 migration의 생성·SQL 확인은 기존 `DesignTimeDbContextFactory`가 있는 Infrastructure를 `--project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure`로 사용한다. 생성 SQL 확인과 실제 DB 적용은 별개이며 개발 DB에 `database update`를 실행하지 않는다.

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
