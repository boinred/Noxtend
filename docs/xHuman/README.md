# LLM 에이전트 개발 안내

이 폴더는 Noxtend 서버에서 LLM·이미지 AI 공급자, 프롬프트, 호출 내역을 수정할 때 필요한 코드 경로와 정본 자료를 찾는 색인이다. 공통 작업 규칙은 루트 [AGENTS.md](../../AGENTS.md), Backend 계층·상태·보안 규칙은 [apps/backend/AGENTS.md](../../apps/backend/AGENTS.md)를 따른다. 이 문서는 두 지침을 복제하거나 대체하지 않는다.

## 작업별 시작점

| 작업 | 먼저 볼 문서 | 구현·검증 시작점 |
| --- | --- | --- |
| Backend 구조·작업 흐름·API·Worker·저장소 | [backend.md](backend.md) | `apps/backend/Noxtend.Api`, `Noxtend.Application`, `Noxtend.Domain`, `Noxtend.Infrastructure` |
| 텍스트·이미지 공급자, 프롬프트 버전, 호출 기록·비용 | [providers-and-prompts.md](providers-and-prompts.md) | `Noxtend.Domain/Ports`, `Noxtend.Infrastructure/Llm`, `Noxtend.Infrastructure/Image`, `Noxtend.Tuning.*` |
| 공급자 설정 API와 모델·기능 목록 | 두 문서 | `Noxtend.Api/Controllers`, `Noxtend.Application/Providers`, `Noxtend.Domain/Provider` |
| 유사도 AI 평가 | 두 문서 | `Noxtend.Application/Similarity/EvaluateSimilarityHandler.cs`, 관련 Application 테스트 |
| 프롬프트 변경의 회귀·AI 출력 품질 비교 | [prompt-evaluation.md](prompt-evaluation.md), [$agentic-eval](../../.agents/skills/agentic-eval/SKILL.md) | 기존 골든 샘플·프롬프트 버전·단계 테스트·호출 기록·사람 판정 |

## 에이전트 작업 규칙

- 시작 전에 루트·Backend `AGENTS.md`와 작업에 해당하는 문서만 읽는다. 모든 LLM 문서를 무조건 읽지 않는다.
- 이 폴더의 안내 문서에는 줄 번호를 고정하지 않는다.
- 현재 동작은 코드·테스트·실행 설정으로 확인한다. README, 과거 Plan·Design, migration 이름만으로 구현 여부를 추정하지 않는다. 설계 문서는 결정의 이유를 찾는 참고 자료다.
- 변경으로 파일·심볼·API·상태·저장 형식·공급자 기능·검증 방법이 달라지면 이 색인과 관련 안내 문서를 같은 작업에서 갱신한다. 기능별 canonical Plan·Design·Report가 있으면 그 문서도 함께 확인한다.
- 공급자 실 호출, 유료 smoke, DB 적용, 외부 배포는 별도 요청 범위가 승인한 경우에만 수행한다. 일반 검증은 Fake와 가짜 HTTP 응답을 사용한다.

## 정본 자료

- 지속되는 아키텍처·보안·재시도 규칙: [Backend 개발 지침](../../apps/backend/AGENTS.md)
- 테스트 명령과 실행 제약: [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)
- 공급자 키·Data Protection·네트워크 공개: [배포 지침](../../deploy/AGENTS.md), [인프라 구성도](../infrastructure.md)
- API·프론트 계약 변경: [Frontend 개발 지침](../../apps/frontend/AGENTS.md)과 `apps/frontend/src/infra/api`, `apps/frontend/src/domain`
