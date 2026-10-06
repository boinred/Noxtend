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
