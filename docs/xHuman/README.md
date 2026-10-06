# 에이전트 개발 문서 색인

이 폴더는 AI 에이전트(Codex, Claude Code)가 Noxtend를 수정할 때 필요한 코드 경로와 정본 자료를 찾는 색인이다. 코드를 탐색하기 전에 이 문서에서 작업에 맞는 도메인 문서를 고르고, 그 문서가 가리키는 파일부터 읽는다. 공통 작업 규칙은 루트 [AGENTS.md](../../AGENTS.md), 영역별 규칙은 각 영역의 `AGENTS.md`를 따른다. 이 폴더는 그 지침을 복제하거나 대체하지 않는다.

## 작업별 시작점

| 작업 | 먼저 볼 문서 | 구현·검증 시작점 |
| --- | --- | --- |
| Backend 구조·작업 흐름·API·Worker·저장소 | [backend.md](backend.md) | `apps/backend/Noxtend.Api`, `Noxtend.Application`, `Noxtend.Domain`, `Noxtend.Infrastructure` |
| 텍스트·이미지 공급자, 프롬프트 버전, 호출 기록·비용 | [providers-and-prompts.md](providers-and-prompts.md) | `Noxtend.Domain/Ports`, `Noxtend.Infrastructure/Llm`, `Noxtend.Infrastructure/Image`, `Noxtend.Tuning.*` |
| 공급자 설정 API와 모델·기능 목록 | 위 두 문서 | `Noxtend.Api/Controllers`, `Noxtend.Application/Providers`, `Noxtend.Domain/Provider` |
| 유사도 AI 평가 | 위 두 문서 | `Noxtend.Application/Similarity/EvaluateSimilarityHandler.cs`, 관련 Application 테스트 |
| 프롬프트 변경의 회귀·AI 출력 품질 비교 | [prompt-evaluation.md](prompt-evaluation.md), [$agentic-eval](../../.agents/skills/agentic-eval/SKILL.md) | 기존 골든 샘플·프롬프트 버전·단계 테스트·호출 기록·사람 판정 |
| Frontend 화면·쿼리·API 연동 | [frontend.md](frontend.md) | `apps/frontend/src/features`, `src/app/queries`, `src/infra/api`, `src/domain` |
| HTTP 계약 변경 | [backend.md](backend.md), [frontend.md](frontend.md) | `Noxtend.Api/Controllers`·`Contracts`, `apps/frontend/src/infra/api`, `apps/frontend/src/domain` |
| 로컬 실행·인프라·배포 | [인프라 구성도](../infrastructure.md), [로컬 기동 절차](../../deploy/k8s/README.md), [배포 지침](../../deploy/AGENTS.md) | `deploy/local-up.sh`, `deploy/k8s/`, `deploy/azure/` |

## 에이전트 작업 규칙

- 루트·영역 `AGENTS.md`와 작업에 해당하는 문서만 읽는다. 이 폴더의 모든 문서를 무조건 읽지 않는다.
- 현재 동작은 코드·테스트·실행 설정으로 확인한다. README, 과거 Plan·Design, migration 이름만으로 구현 여부를 추정하지 않는다. 설계 문서는 결정의 이유를 찾는 참고 자료다.
- 문서와 코드가 다르면 코드·테스트를 기준으로 판단하고 아래 규칙에 따라 문서를 고친다.
- 공급자 실 호출, 유료 smoke, DB 적용, 외부 배포는 별도 요청 범위가 승인한 경우에만 수행한다. 일반 검증은 Fake와 가짜 HTTP 응답을 사용한다.

## 문서 유지 규칙

커밋 전에 이번 변경이 이 색인과 도메인 문서의 내용과 맞는지 확인한다.

- 고칠 때: 파일·심볼·API(route·DTO·상태 코드)·테이블·컬럼·저장 형식·상태 전이·설정 키·스크립트 옵션·규칙을 추가·이동·삭제·변경했고, 그 대상이 문서에 언급되었거나 위 표의 작업 흐름을 바꿀 때. 문서에 영향이 없는 변경은 문서를 건드리지 않는다.
- 고칠 곳: 위 표에서 해당 작업 행의 "먼저 볼 문서". 맞는 행이 없으면 행을 추가하고, 기존 문서로 담을 수 없을 때만 이 폴더에 새 도메인 문서를 만든다.
- 쓰는 방식: 현재 사실만 쓴다. 경로와 심볼 이름으로 가리키고 줄 번호를 고정하지 않는다. 변경 경위·이력·PR 번호는 커밋 메시지에 둔다. 사라진 경로·심볼은 문서에서 지운다. 같은 사실을 여러 문서에 복제하지 않고 정본 문서로 링크한다.
- 커밋: 문서 수정은 코드 변경과 같은 커밋에 넣는다. 코드를 먼저 커밋하고 문서를 따로 커밋하지 않는다.
- 확인: 문서에 쓴 경로가 존재하고 상대 링크가 깨지지 않았는지 확인한다.

## 정본 자료

- 지속되는 아키텍처·보안·재시도 규칙: [Backend 개발 지침](../../apps/backend/AGENTS.md)
- 테스트 명령과 실행 제약: [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)
- 공급자 키·Data Protection·네트워크 공개: [배포 지침](../../deploy/AGENTS.md), [인프라 구성도](../infrastructure.md)
- API·프론트 계약 변경: [Frontend 개발 지침](../../apps/frontend/AGENTS.md)과 `apps/frontend/src/infra/api`, `apps/frontend/src/domain`
