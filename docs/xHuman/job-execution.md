# 작업 실행·저장소 지도

작업 접수부터 공정 실행·재시도·저장까지의 공통 백본과 3D 메시 공정을 찾는 지도다. 계층 규칙과 상태·동시성 원칙은 [Backend 개발 지침](../../apps/backend/AGENTS.md)이 정본이며 여기서는 경로와 흐름만 가리킨다. 경로는 `apps/backend/` 기준이다.

## 수정 진입점

| 책임 | 시작점 |
| --- | --- |
| 호스트 조립·Worker·Sweeper 등록 | `Noxtend.Api/Program.cs`, `Workers/TaskWorkerRegistration.cs`(`TaskKind` → handler 표) |
| Infrastructure DI·설정 키 | `Noxtend.Infrastructure/InfrastructureServiceCollectionExtensions.cs`의 `AddNoxtendInfrastructure`(`ConnectionStrings:Db`·`Blob`·`Redis`, `Jobs`·`Generation`·`MeshGeneration` section) |
| 작업·공정 aggregate와 상태 전이 | `Noxtend.Domain/Job/PipelineJob.cs`, `PipelineTask.cs`, `TaskKind.cs`; 실행 가능 판정은 `PipelineJob.IsReadyToRun` |
| 다음 공정 적재 | `Noxtend.Application/Job/JobOrchestrator.cs`(`StartAsync`, `OnTaskCompletedAsync`) |
| 공정 claim·lease·결과 반영·재시도 분류 | `Noxtend.Application/Pipeline/TaskExecution.cs`(`RunAsync`, `TaskExecutionPolicy`, `TaskFailure`, `ITaskHandler`) |
| 유실 공정 회수 | `Noxtend.Api/Workers/TaskSweeper.cs`(주기 `JobOptions.SweepIntervalSeconds`) → `Noxtend.Application/Job/SweepStaleTasksHandler.cs`; Redis pending 회수 기준은 `Workers/ReclaimPlan.cs`의 `For`·`Idle`(TaskKind별 `JobOptions`·`GenerationOptions`·`MeshGenerationOptions`의 lease×2) |
| 큐 | Port `Noxtend.Domain/Ports/ITaskQueue.cs`, 구현 `Noxtend.Infrastructure/Queue/RedisTaskQueue.cs`(Redis Streams, consumer group `workers`) |
| SQL 저장 | `Noxtend.Infrastructure/Persistence/NoxtendDbContext.cs`, `Configurations/`, `Repositories/Ef*Repository.cs` |
| Blob 저장 | `Noxtend.Domain/Ports/IBlobStorage.cs`·`IMeshArtifactStorage.cs`, `Noxtend.Infrastructure/Blob/Azure*Storage.cs`(container `source-images`·`meshes`) |
| 상태 확인 | `Noxtend.Api/Controllers/HealthController.cs`(`/health/live`, `/health`), `Noxtend.Infrastructure/Health/*Probe.cs` |

`Persistence/InMemory/`, `Blob/InMemory*`, `Queue/InMemory*`는 테스트용 구현이며 `AddNoxtendInfrastructure`는 SQL·Redis·Azure Blob 구현을 등록한다.

## 데이터 흐름

1. 접수: `JobsController` → `StartJobHandler`(2D는 `SpriteJobsController` → `StartSpriteJobHandler`) → `IJobRepository.AddAsync`·`SaveChangesAsync` → `JobOrchestrator.StartAsync` → `ITaskQueue.EnqueueAsync`
2. 실행: `TaskWorker`(TaskKind별 `BackgroundService`)가 `ConsumeAsync` → 공정마다 새 DI scope에서 `ITaskHandler.HandleAsync` → `Synthesize` 방어 handler를 제외한 handler는 `TaskExecution.RunAsync`로 SQL에서 공정을 claim하고 lease를 갱신하며 본문 실행
3. 반영: 본문이 돌려준 반영 함수를 최신 aggregate(`ReloadAsync`)에 적용 → 저장 → `JobOrchestrator.OnTaskCompletedAsync`가 준비된 후속 공정 적재 → Worker `AckAsync`
4. 복구: 중복 배달·종료 공정·다른 owner는 `Skipped`로 Ack한다. 프로세스 종료 시 Ack하지 않고 `SweepStaleTasksHandler`가 lease 만료 공정을 재시도 한도 안에서 다시 적재하고 준비된 대기 공정을 재적재한다. rowversion 경합은 `ConcurrencyConflictException`으로 감싸며 Worker는 오류로 기록하지 않는다.
5. 유사도 평가는 별도 경로다: `SimilarityController` → `StartSimilarityRunHandler`·`UploadCandidateRenderHandler`·`RetrySimilarityRunHandler` → `ISimilarityQueue` → `SimilarityWorker` → `EvaluateSimilarityHandler`, 회수는 `SimilaritySweeper` → `SweepSimilarityHandler`.

TaskKind별 handler는 `TaskWorkerRegistration`의 표가 정본이다. 텍스트 공정은 `RunTaskHandler`, 이미지 생성은 `RunGenerationTaskHandler`, 3D는 `RunMeshTaskHandler`, 2D는 `Noxtend.Application/Sprites/Run*TaskHandler.cs`다. 단계별 상세는 [Backend 코드 안내](backend.md)와 [공급자와 프롬프트](providers-and-prompts.md)를 본다.

## 3D 메시 공정

`TaskKind.Reconstruct` → `RunMeshTaskHandler` → `IMeshProviderFactory`(`Noxtend.Infrastructure/Mesh/MeshProviderFactory.cs`) → `TripoMeshProvider`·`MeshyMeshProvider`·`FakeMeshProvider` → 결과 다운로드(`TripoResultDownload`·`MeshyResultDownload`·`MeshResultHttp`) → `IMeshArtifactStorage`, 실행 기록은 `IMeshRunRepository`. 추가·재계획 접수는 `Noxtend.Application/Mesh/AddMeshProductionHandler.cs`·`ReplanPartMeshHandler.cs`, 다운로드 API는 `GeneratedMeshesController`다.

- 다운로드 host 허용목록은 `MeshGenerationOptions.AllowedResultHosts`이고 사설 IP 거부·host 대조는 `MeshResultHttp`가 한다.
- 제출 결과를 알 수 없는 실패는 `MESH_SUBMISSION_UNKNOWN`이다. 재제출 금지·`ProviderTaskId` 재사용 규칙은 [Backend 개발 지침](../../apps/backend/AGENTS.md#상태동시성재처리)을 따른다.

## 유지할 계약

- 상태 정본은 SQL이다. 큐 메시지 존재·Worker 메모리로 완료·재시도를 결정하지 않는다.
- 상태 변경은 `PipelineJob`·`PipelineTask` 메서드로만 한다. 준비 판정은 Orchestrator와 Sweeper가 `IsReadyToRun`을 공유한다.
- `DbContext`는 공정 scope 단위다. Worker 수명이나 병렬 작업에 공유하지 않는다.
- 동시성 토큰은 `Configurations/`의 `IsRowVersion` 설정(작업·MeshRun·SceneLayout·Similarity·Sprite 상태)이다.
- 공급자 키 복호화는 `ProviderCredentialResolver`, 암호화는 `Noxtend.Infrastructure/Security/DataProtectionSecretProtector.cs`이며 key ring 경로는 `Program.cs`의 `DataProtection:KeysPath`다. 실제 보존 조건은 [배포 지침](../../deploy/AGENTS.md)에서 확인한다.
- `Program.cs`는 기동 시 `MigrateAsync`를 실행한다.

## Migration

- 원본 계약: `NoxtendDbContext`, `Persistence/Configurations/`, Tuning 저장은 `Repositories/EfTuningRepositories.cs`
- 생성 코드: `Persistence/Migrations/<timestamp>_<Name>.cs`·`.Designer.cs`·`NoxtendDbContextModelSnapshot.cs`. 설계 시점 context는 `Persistence/DesignTimeDbContextFactory.cs`
- 생성 명령(저장소 루트): `dotnet ef migrations add <Name> --project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Infrastructure`. 루트 `dotnet-tools.json`에는 csharpier만 있고 `dotnet-ef`는 없으므로 별도 설치하고 버전을 EF 패키지와 맞춘다. `--startup-project apps/backend/Noxtend.Api`는 EF Design 패키지가 없어 실패한다.
- 이미 적용된 migration은 고치지 않고 후속 migration을 만든다. 개발 DB에 `database update`를 실행하지 않는다.

## 관련 테스트

| 대상 | 테스트(`apps/backend/Noxtend.Tests/` 기준) |
| --- | --- |
| 상태 전이·준비 판정 | `Domain/PipelineJobTests.cs`, `Domain/PipelineTaskTests.cs`, `Domain/MeshRunTests.cs` |
| 적재·회수·재시도 | `Application/JobOrchestratorTests.cs`, `SweepStaleTasksHandlerTests.cs`, `RetryTaskHandlerTests.cs`, `RetryReenqueueSchedulingTests.cs`, `JobLifecycleTests.cs`, `Api/ReclaimPlanTests.cs` |
| Worker 등록·경합 | `Api/TaskWorkerRegistrationTests.cs`, `Api/TaskWorkerConcurrencyTests.cs` |
| SQL·Redis(Docker) | `Infrastructure/TaskConcurrencyTests.cs`, `EfJobRepositoryConcurrencyTests.cs`, `RedisTaskQueueTests.cs`, `JobDeletionPersistenceTests.cs`, `MigrationRegistrationTests.cs`, `ServiceRegistrationTests.cs` |
| 3D 메시 | `Application/MeshRecoveryTests.cs`, `MeshJobIntakeTests.cs`, `Infrastructure/TripoMeshProviderTests.cs`, `MeshyMeshProviderTests.cs`, `TripoResultDownloadTests.cs`, `MeshPersistenceTests.cs`, `Api/GeneratedMeshesControllerTests.cs` |
| 계층 경계 | `Architecture/LayerBoundaryTests.cs` |

명령과 Docker·유료 smoke 조건은 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md#backend)를 따른다.

## 확인 기준과 미확인

- 마지막 확인: 2026-10-08, revision `d2cad8d`. 위 경로·심볼은 코드에서 존재와 호출 관계를 확인했다. 테스트는 이 지도 작성 중 실행하지 않았다.
- 미확인: Redis 장애·Pod 재시작 중 실제 복구 시간, 실제 SQL Server에 대한 migration 적용 결과, Tripo·Meshy 실 API 응답 형식 변화. 이들은 Fake·Testcontainers 테스트 범위 밖이며 실환경 확인이 필요하다.
