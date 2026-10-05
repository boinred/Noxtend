---
name: noxtend-workflow
description: Select Noxtend-specific implementation, UI skill routing, infrastructure procedures, and verification commands for backend, frontend, deployment, or repository instruction changes. Read only references for the affected area; this skill does not authorize deployment or paid provider calls.
---

# Noxtend Workflow

루트와 작업 영역의 `AGENTS.md`를 먼저 읽는다. 상시 아키텍처·보안·보존 규칙은 해당 지침이 정본이며, 이 스킬은 작업에 필요한 절차만 선택한다. 저장소 밖에서는 이 절차를 일반 규칙으로 적용하지 않는다.

| 작업 | 읽을 자료 |
| --- | --- |
| Backend 구현·리뷰·계약·Worker·영속성 | [references/backend.md](references/backend.md) |
| LLM·AI 공급자·프롬프트·호출 기록 | [LLM 개발 안내](../../../docs/llm/README.md)와 해당 작업의 Backend 자료 |
| Frontend 구현·UI·모션 스킬 선택·API 연동 | [references/frontend.md](references/frontend.md) |
| 로컬 인프라·Azure·Kubernetes·루트 CI | [references/deployment.md](references/deployment.md) |
| 테스트·스타일 설정·문서 검증·완료 보고 | [references/verification.md](references/verification.md) |

변경하는 영역의 자료와 검증 자료만 읽는다. API 계약 변경은 Backend와 Frontend 양쪽 자료를 읽고, 실제 DB·배포 변경은 배포 자료도 확인한다.

- 코드 작성·수정·리뷰에는 기존 `$karpathy-guidelines`를 적용한다. 동작 구현은 `$tdd-cycle`의 계획 → 설계 → 구현 → 검증 흐름을 사용하며, 이 스킬에서 그 절차를 복제하지 않는다.
- PDCA 문서나 단계 변경은 기존 `$pdca-cycle`을 사용한다. 일반 문서·스타일 도구 설정을 위해 새 PDCA 기능을 등록하지 않는다.
- 읽기 전용 UI 감사, 계획·설계 요청을 구현 요청으로 확대하지 않는다. 스킬 이름이나 파일 존재는 관련 도구·서비스가 현재 사용 가능하다는 증거가 아니다.
