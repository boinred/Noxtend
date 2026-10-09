# Noxtend 코드 지도

AI 에이전트가 작업에 필요한 코드와 테스트를 찾는 첫 안내서다. 사람용 소개는 루트 `README.md`에 있다. 탐색 출발점일 뿐 코드 분석을 대신하지 않는다. 수정 전에는 실제 선언·호출·테스트를 확인하고, 작업 규칙은 [AGENTS.md](../../AGENTS.md)를 따른다.

마지막 확인: 2026-10-08, revision `d2cad8d`

## 저장소 구성

| 경로 | 역할 |
| --- | --- |
| `apps/backend/` | .NET 10 ASP.NET Core. API와 Worker가 한 호스트에서 실행된다. 솔루션 `Noxtend.slnx` |
| `apps/frontend/` | React 19·Vite 스튜디오·관리자 화면. 패키지 `@nextend/frontend` |
| `deploy/` | Docker Desktop Kubernetes 로컬 기동(`local-up.sh`, `k8s/`), Azure 내부 테스트 정의(`azure/`) |
| `packages/proto/` | Protobuf 계약 계획 문서뿐이다. 생성 코드·호출 없음 |
| `docs/xHuman/` | 영역별 코드 지도와 [문서 유지 규칙](README.md#문서-유지-규칙) |
| `docs/superpowers/` | 과거 설계·계획·실행 기록. 결정 이유 참고용이며 현재 구현 근거가 아니다 |
| `.agents/skills/`, `.githooks/` | 프로젝트 스킬 정본, 커밋·설계 문서 검사 훅 |

## 영역별 지도

필요한 행의 지도만 읽는다.

| 작업 | 지도 |
| --- | --- |
| Backend 계층·작업 흐름·2D sprite·HTTP 계약 | [backend.md](backend.md) |
| 작업 접수·큐·Worker·재시도·SQL·Blob·migration·3D 메시 | [job-execution.md](job-execution.md) |
| 텍스트·이미지 공급자, 프롬프트 버전, 호출 기록·비용, 공급자 설정 API | [providers-and-prompts.md](providers-and-prompts.md) |
| 프롬프트 회귀·AI 출력 품질 비교 | [prompt-evaluation.md](prompt-evaluation.md), [agentic-eval](../../.agents/skills/agentic-eval/SKILL.md) |
| Frontend 화면·쿼리·API 연동 | [frontend.md](frontend.md) |
| HTTP 계약 변경 | backend.md와 frontend.md 양쪽 |
| 로컬 실행·인프라·배포 | [인프라 구성도](../infrastructure.md), [로컬 기동 절차](../../deploy/k8s/README.md), [배포 지침](../../deploy/AGENTS.md) |
| 설계·계획 문서·실행 방식·검사 훅 | [설계 승인 후 실행 방식](../../AGENTS.md#설계-승인-후-실행-방식), [검증 자료](../../.agents/skills/noxtend-workflow/references/verification.md#설계계획-문서-훅) |

## 공통 진입점과 경계

- HTTP 요청: 화면 → `apps/frontend/src/app/queries/use*.ts` → `src/infra/api/*Api.ts` → `client.ts` → `Noxtend.Api/Controllers/*Controller.cs` → `Noxtend.Application/**/*Handler.cs` → `Noxtend.Domain` aggregate·`Ports/` → `Noxtend.Infrastructure` adapter
- 비동기 공정: Handler → `JobOrchestrator` → Redis Streams → `TaskWorker` → `TaskExecution` → SQL. 상태 정본은 SQL Server다.
- 조립 루트: `Noxtend.Api/Program.cs`, `InfrastructureServiceCollectionExtensions.AddNoxtendInfrastructure`, `Workers/TaskWorkerRegistration.cs`
- HTTP 계약: Backend `Noxtend.Api/Contracts/`·`ApiResults`, Frontend `src/infra/api/`·`src/domain/`. 바꾸면 양쪽 스택을 검증한다.
- 계층 경계 검사: Backend `Noxtend.Tests/Architecture/LayerBoundaryTests.cs`, Frontend `eslint.config.js`·`src/routes/layerRules.test.ts`
- 보안: 인증·인가 없음. 공급자 키는 Data Protection(`DataProtectionSecretProtector`)으로 저장하고 `ProviderCredentialResolver`에서만 복호화하며 응답은 `ProviderResponse`에서 마스킹한다. 3D 결과 다운로드는 `MeshResultHttp`의 host 허용목록을 거친다. 이 항목은 요약이므로 변경 시 [Backend 지침](../../apps/backend/AGENTS.md#계약보안비용)과 실제 구현을 확인한다.
- 자동 생성 코드: EF migration뿐이다. 원본 계약·생성 명령은 [job-execution.md](job-execution.md#migration)에 있다.

## 코드와 테스트 찾기

- Backend 테스트는 `apps/backend/Noxtend.Tests/`의 `Domain`·`Application`·`Api`·`Infrastructure`·`Architecture`에 대상 계층별로 있고 이름은 대체로 `<대상>Tests.cs`다. `Infrastructure`의 SQL·Redis 테스트는 Docker가 필요하고, `TripoSmokeTests`·`Smoke/SimilaritySmokeTests`는 유료 smoke다.
- Frontend 단위 테스트는 소스 옆 `*.test.ts`이며 `.test.tsx`는 수집되지 않는다. E2E는 `apps/frontend/tests/e2e/*.spec.ts`와 가짜 API `fakeApi.ts`·`spriteFakeApi.ts`다.
- 심볼에서 테스트로: `rg -l "StartJobHandler" apps/backend/Noxtend.Tests`
- route에서 Controller로: `rg -n '\[Route|\[Http' apps/backend/Noxtend.Api/Controllers`
- Frontend 호출부에서 route로: `rg -n "/api/jobs" apps/frontend/src/infra/api`
- 지도에 없으면 해당 영역 디렉터리부터 검색 범위를 넓히고 찾은 경로를 지도에 보탠다.
- 실행 명령·Docker·유료 smoke 조건은 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다.

## 지도 갱신 기준

경로·책임·계약·데이터 흐름이 바뀌면 영향받은 지도와 그 지도의 확인 기준만 갱신한다. 영역이 추가·삭제되면 이 파일의 표도 고친다. 단순 서식 변경에는 갱신하지 않는다. 세부 기준은 [문서 유지 규칙](README.md#문서-유지-규칙)을 따른다.
