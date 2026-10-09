# Backend 작업 절차

파일 경로는 `apps/backend/` 기준이고 `apps/`로 시작하는 경로는 저장소 루트 기준이다. 명령은 저장소 루트에서 실행한다. 상시 계층·상태·보안 규칙은 `apps/backend/AGENTS.md`를 따른다.

LLM·AI 공급자·프롬프트·호출 기록 작업은 [공급자와 프롬프트 지도](../../../../docs/xHuman/providers-and-prompts.md)부터 읽는다.

## 변경 전 확인

- 진입점은 `Noxtend.Api/Program.cs`, DI는 `Noxtend.Infrastructure/InfrastructureServiceCollectionExtensions.cs`다. 계약은 `Noxtend.Api/Controllers`·`Contracts`, 상태는 `Noxtend.Domain/Job`, 저장 형식은 `Noxtend.Infrastructure/Persistence`에서 확인한다.
- `Noxtend.slnx`, 관련 `.csproj`와 테스트를 읽는다. README의 초기 모듈·엔드포인트·테스트 수를 현재 구현으로 사용하지 않는다.
- 계층 변경은 `.csproj`와 `Noxtend.Tests/Architecture/LayerBoundaryTests.cs`를 함께 확인한다.

## 상태·Worker 변경

- 리스·갱신·협조적 취소·재시도는 `Noxtend.Application/Pipeline/TaskExecution.cs`, 단계별 실행은 `RunTaskHandler`·`RunGenerationTaskHandler`·`RunMeshTaskHandler`에서 확인한다. 검수 승인 전 보류·방향별 재시도·부분 성공·3D 입력 조건을 함께 검증한다.
- Worker·동시성은 `TaskWorkerRegistration`·`ReclaimPlan`·`TaskSweeper`·`SimilarityWorker`·`SimilaritySweeper`를 확인한다. 같은 `TaskWorker`의 다중 등록을 `AddHostedService<TaskWorker>`로 바꾸면 중복 제거로 등록이 누락될 수 있다.
- 이미지·3D 동시성/리스는 `GenerationOptions`·`MeshGenerationOptions`, 일반 공정은 `JobOptions`가 기준이다. `Synthesize`는 동기 완료 경로이며 방어용 등록을 일반 외부 호출 공정으로 해석하지 않는다.

## 계약·공급자 변경

- 계약 변경은 DTO 필드·enum 전송 표기·null 의미·오류 코드와 `apps/frontend/src/infra/api`·`apps/frontend/src/domain`의 타입/파서를 함께 검토하고 양쪽 스택을 검증한다.
- Fake 검증 전 `Llm:UseFake`와 Factory 등록을 실제 확인한다. 서버 기동만으로 공급자 호출이 Fake라고 판단하지 않는다.
- 공급자 규약에 맞는 재시도 한도·backoff를 정하고 새 유료 제출과 기존 작업 조회를 구분한다. 3D 재개는 `Noxtend.Application/Mesh/RunMeshTaskHandler.cs`·저장된 `MeshRun`이 기준이다.
- 단가 계산은 `Noxtend.Tuning.Domain/Call/ModelPriceBook.cs`를 확인한다.

## 영속성 변경

- EF 모델 변경 시 SqlServer migration·snapshot·관련 persistence 테스트를 함께 검토한다.
- 프롬프트·단가 시드는 `SeedPrompts`·`SeedModelPrices`와 기존 DB 갱신 migration을 함께 확인한다.
- 저장 JSON은 `Noxtend.Infrastructure/Persistence/Serialization`의 구형 데이터 읽기·새 형식 쓰기를 검증한다.
- migration 생성은 `DesignTimeDbContextFactory`와 `--project apps/backend/Noxtend.Infrastructure --startup-project apps/backend/Noxtend.Api`를 사용한다. 실제 DB 적용은 별도 승인 범위다.

검증 명령·Docker 요구·유료 smoke 조건은 [verification.md](verification.md)를 읽는다. 컨테이너·실제 DB에 영향을 주는 작업은 [deployment.md](deployment.md)도 확인한다.
