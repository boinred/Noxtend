# Noxtend 개발 지침

## 상시 원칙

- 이 파일은 프로젝트 전체에 적용하며 하위 `AGENTS.md`가 해당 영역을 보완한다. 사용자 요청을 우선하고 충돌하는 지침의 범위를 확인한다.
- 설명·보고는 한국어로, 코드·명령어·경로·API 이름·에러 메시지는 원문으로 작성한다.
- 관련 문서, 실제 구현, 테스트·실행 설정을 먼저 읽는다. 현재 구현·설계 의도·과거 기록을 구분하고 체크 표시나 README의 과거 수치를 현재 검증으로 사용하지 않는다.
- 다른 작업의 수정·untracked 파일을 보존한다. 관련 없는 정리·의존성 교체·전체 재작성·일괄 포맷을 묶지 않는다.
- 오류를 빈 목록·성공 응답·비용 0·임의 진행률로 숨기지 않는다. 구현·자동 검증·수동 확인·실 API 확인을 구분하고 실행 명령·결과·남은 제약을 보고한다.

## 제품과 아키텍처

- Noxtend는 이미지 분석·파츠 분해·방향 이미지 생성·3D 재구성을 연결하는 AI 에셋 제작 스튜디오다. [PRODUCT.md](PRODUCT.md)가 제품 방향의 정본이며 노드 캔버스 방식은 제품 범위에 포함하지 않는다.
- 작업 상태의 정본은 SQL Server다. Redis Streams는 디스패치 수단이며 UI·큐 메시지가 서버 상태를 대신 결정하지 않는다.
- API와 Worker는 같은 ASP.NET Core 호스트에서 실행된다. 단계·워커 수·동시성은 현재 등록 코드·설정으로 확인한다.
- 현재 클라이언트 계약은 HTTP JSON과 파일 응답이다. `packages/proto/README.md`의 Protobuf·gRPC-Web는 후속 계획이며 실제 호출·생성 코드로 사용 여부를 판단한다.
- 부분 성공·취소·재시도·검수 승인·단계 전이를 구분한다. 근거 없이 Saga나 특정 durable workflow 구현으로 설명하지 않는다.

## 작업별 스킬과 자료

프로젝트 스킬의 정본은 `.agents/skills/`다. `.claude/skills/`는 스킬 정본을 가리키는 링크이고 `CLAUDE.md`는 이 파일을 참조한다. 공통 지침을 복제하지 않으며 선택한 스킬의 전체 `SKILL.md`와 작업에 필요한 참조만 읽는다.

| 작업 | 필수 지침·스킬 |
| --- | --- |
| 코드 작성·수정·디버깅·테스트·리뷰 | [karpathy-guidelines](.agents/skills/karpathy-guidelines/SKILL.md) |
| 기능·버그 수정·리팩터링·테스트 구현 | [tdd-cycle](.agents/skills/tdd-cycle/SKILL.md): 계획 → 설계 → 구현 → 검증 |
| 여러 계층 변경·불명확한 의존 관계 탐색 | [context-map](.agents/skills/context-map/SKILL.md); 알려진 범위의 일반 편집은 생략 |
| HTTP DTO·route·직렬화·상태 코드 변경 영향 리뷰 | [api-breaking-change-detector](.agents/skills/api-breaking-change-detector/SKILL.md) |
| 프롬프트 회귀·AI 출력 품질 비교 | [agentic-eval](.agents/skills/agentic-eval/SKILL.md); 일반 단위 테스트와 실 모델 평가 구분 |
| Backend | [apps/backend/AGENTS.md](apps/backend/AGENTS.md), [noxtend-workflow](.agents/skills/noxtend-workflow/SKILL.md)의 Backend·검증 자료 |
| Frontend·UI·모션 | [apps/frontend/AGENTS.md](apps/frontend/AGENTS.md), [DESIGN.md](DESIGN.md), 위 스킬의 Frontend·검증 자료 |
| 인프라·Azure·Kubernetes·루트 CI | [deploy/AGENTS.md](deploy/AGENTS.md), 위 스킬의 배포·검증 자료 |
| 일반 문서·AGENTS·스타일 설정 | 위 스킬의 검증 자료; 새 TDD 테스트 불필요 |

Plan·Design만 요청하면 애플리케이션 소스를 수정하지 않는다. 실행 요청은 승인된 가역적 범위를 검증까지 진행한다.

## 코드 탐색과 지도 갱신

흐름: 지도 확인 → 관련 영역 선택 → 변경 대상 코드·테스트 재확인 → 구현 → 영향받은 지도만 갱신

1. 지도: 처음에는 [docs/xHuman/CODEMAP.md](docs/xHuman/CODEMAP.md)만 읽고 작업에 해당하는 영역 지도(`docs/xHuman/*.md`)만 추가로 읽는다. 모든 영역 지도를 매번 읽거나 저장소 전체를 재분석하지 않는다.
2. 재확인: 수정 전에 지도가 가리킨 실제 선언·호출 관계·계약·테스트와 현재 `git diff`를 확인한다. 지도에서 찾지 못하면 관련 디렉터리부터 검색 범위를 넓힌다.
3. 충돌: 지도와 코드가 다르면 코드·테스트를 기준으로 판단하고 해당 지도를 고친다. 인증·암호화·DB migration·배포는 요약만 믿지 않고 실제 구현을 확인한다.
4. 갱신: 경로·책임·계약·데이터 흐름이 바뀌면 영향받은 지도와 그 확인 기준만 고친다. 단순 서식 변경에는 지도 갱신이 필요 없다. 세부 기준은 [문서 유지 규칙](docs/xHuman/README.md#문서-유지-규칙)이다.
5. 커밋: 문서 수정은 코드와 같은 커밋에 넣는다. 코드를 먼저 커밋하고 문서를 따로 커밋하지 않는다.

## 설계·계획 문서 작성

- 승인된 설계를 참조하고, 구현 계획에는 파일·계약·검증 방법을 중심으로 필요한 내용만 쓴다. 같은 제약·설명·예제 코드를 작업마다 반복하지 않는다.
- `rg --files`로 기존 경로를 확인한 뒤 필요한 파일만 읽는다. 출력이 잘리면 읽는 범위를 줄이고, 존재하지 않는 경로·심볼을 현재 구현처럼 쓰지 않는다.
- 한국어 초안을 작게 나눠 저장하고 작성 후 `pnpm docs:check`를 실행한다. 자동 검사 범위·에이전트 훅 활성화·분량 경고는 [검증 자료](.agents/skills/noxtend-workflow/references/verification.md#설계계획-문서-훅)를 따른다.
- 작업 중에는 60초 안에 확인한 사실·남은 불확실성·다음 확인을 짧게 알린다. 훅은 작성 시간이나 의미상 정확성을 보장하지 않으므로 긴 무응답·반복 조사로 보완하지 않는다.

## 설계 승인 후 실행 방식

- 기능·버그 수정·리팩터링·테스트 등의 구현은 **작업별 분담·독립 리뷰(Subagent-driven)**으로 진행한다. 설계 승인 후 구현 계획에 실행 방식을 명시하며, 사용자가 지정한 방식은 유지하고 이미 정한 방식을 다시 선택하도록 묻지 않는다.
- 설계 승인과 구현 실행 요청을 구분한다. Plan·Design만 요청한 경우 계획 작성까지 진행하고, 구현 실행이 승인되면 작업 사이에 계속 진행할지 반복해서 묻지 않는다.

| 방식 | 적용·역할 |
| --- | --- |
| **Subagent-driven · 기본** | `superpowers:subagent-driven-development`를 사용한다. 주 에이전트는 승인 설계·계획과 의존 관계를 관리하고, 작업별 새 구현 에이전트와 구현에 참여하지 않은 새 리뷰 에이전트를 배정한다. |
| **Native · 직접 구현** | 사용자가 직접 구현을 명시적으로 선택한 경우에 적용한다. 작업 규모·결합도를 이유로 자동 전환하지 않는다. `superpowers:executing-plans`로 주 에이전트가 구현하고, 마지막에 별도 에이전트가 전체 변경을 리뷰한다. |

- 구현자에게 해당 작업의 범위·담당 파일·입출력 계약·검증 명령을 전달한다. 같은 파일을 여러 구현자가 동시에 수정하지 않으며 의존 작업은 선행 작업의 검증·리뷰 후 진행한다. 같은 형태의 작은 수정은 한 작업으로 묶는다.
- Subagent-driven은 작업별 설계 준수·코드 품질 리뷰 → 지적 수정·수정 범위 재리뷰 → 다음 작업 순서로 진행한다. 작업 완료 후 전체 변경의 독립 리뷰와 영향받는 스택의 회귀 검증을 수행한다. 리뷰 결과는 실제 diff·코드·테스트 근거로 판단하며 리뷰 통과가 실행 검증을 대신하지 않는다.
- 실행 방식·작업 완료·검증 결과·미해결 사항을 구현 계획의 실행 기록에 남긴다. 에이전트 도구·스킬을 사용할 수 없으면 사유를 알리고 가능한 직접 구현·검증을 진행하되, 수행하지 못한 독립 리뷰를 완료로 보고하지 않는다. 유료 호출·배포·Git 등 외부 행동의 승인 범위는 기존 지침을 따른다.

## 코딩 스타일과 주석

- 공백·줄 끝은 루트 `.editorconfig`를 따른다. Frontend 포맷·lint·계층 검사는 커밋 때 `.githooks/pre-commit`이 실행하므로 규칙을 여기서 반복하지 않는다. 대상·명령·우회 제약은 [검증 자료](.agents/skills/noxtend-workflow/references/verification.md)를 따른다.
- C#은 포매터 없이 현재의 명명·중괄호·들여쓰기·표현 스타일을 유지한다. 새 포매터나 스타일 규칙으로 일괄 재포맷하지 않는다.
- 주석은 비자명한 결정·상태 전이 조건·동시성·재처리·호환성의 이유만 설명하고 코드 동작을 반복하지 않는다.
- 일반 주석은 단답형의 짧은 명사구로 쓴다. 제품·도메인은 한국어(예: `// 검수 승인 전 생성 보류`), 빌드·CI·배포는 영어로 쓰고 서술형 종결어미·마침표·여러 문장을 피한다. C#·JS·TS는 `//`, 그 밖의 언어는 문법상 필요한 주석 구문을 쓴다.
- XML 문서화·라이선스·자동 생성 주석·도구 지시문은 기존 형식을 유지한다. 기존 주석을 일괄 수정하지 않는다.

## 빌드·테스트 명령

저장소 루트 기준이다. 필터 의미·Docker 요구·유료 smoke 조건은 [검증 자료](.agents/skills/noxtend-workflow/references/verification.md)를 따른다.

```bash
# Backend 빌드·전체 회귀(유료 smoke 제외, Docker 필요)
dotnet build apps/backend/Noxtend.slnx
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'

# Frontend 전체 회귀
pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e

# 새 clone의 커밋 훅 등록
pnpm hooks:install
```

## 실행·외부 행동·Git

- 런타임·명령은 `package.json`, `pnpm-lock.yaml`, `.csproj`, 실제 스크립트가 정본이다. 현재 요구는 Node.js `>=24`, pnpm `11.9.0`, Backend `.NET 10`이다. 루트의 앱 실행·검증 `pnpm` 스크립트는 Frontend 대상이며 Backend 검증을 대신하지 않는다.
- 기능 코드 변경은 해당 스택 전체 회귀, API 계약 변경은 양쪽 스택을 검증한다. 일반 검증은 Fake를 사용하고 유료 smoke를 제외한다. 미수집·건너뜀·환경 미설정을 통과로 계산하지 않으며 기존 실패 판단에는 변경 전 재현 등 근거가 필요하다.
- 현재 인증이 없다. 네트워크 노출·비밀값·저장 데이터·Data Protection 키 보존은 배포 지침을 따른다. 실제 AI·유료 smoke·Slack 전송·배포는 사용자 요청의 승인 범위에서만 수행하며 설정·환경 변수 존재를 승인으로 간주하지 않는다.
- `azure-pipelines.yml`은 Slack 커밋 알림 정의다. 빌드·테스트·배포 파이프라인으로 설명하지 않고 실제 참조 스크립트를 확인한다.
- Git 명령 전에 `git rev-parse --show-toplevel`로 저장소 여부를 확인한다. `.git` 없는 사본은 변경 전 사본·파일 비교로 검증하며 임의 초기화나 commit·branch·push 완료 주장을 하지 않는다.
- 저장소에서는 이번 작업 파일의 diff·비밀값을 확인한다. 전체 작업트리 일괄 stage는 명시적 요청이 있을 때만 수행한다. 코드 수정·로컬 검증·실제 배포 완료는 각각 증거에 맞춰 보고한다.
