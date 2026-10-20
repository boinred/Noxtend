# Noxtend 개발 지침

## 상시 원칙

- 이 파일은 프로젝트 전체에 적용하며 하위 `AGENTS.md`가 해당 영역을 보완한다. 사용자 요청을 우선하고 충돌하는 지침의 범위를 확인한다.
- 설명·보고는 한국어로, 코드·명령어·경로·API 이름·에러 메시지는 원문으로 작성한다.
- 관련 문서, 실제 구현, 테스트·실행 설정을 먼저 읽는다. 현재 구현·설계 의도·과거 기록을 구분하고 체크 표시나 README의 과거 수치를 현재 검증으로 사용하지 않는다.
- 다른 작업의 수정·untracked 파일을 보존한다. 관련 없는 정리·의존성 교체·전체 재작성·일괄 포맷을 묶지 않는다.
- 오류를 빈 목록·성공 응답·비용 0·임의 진행률로 숨기지 않는다. 구현·자동 검증·수동 확인·실 API 확인을 구분하고 실행 명령·결과·남은 제약을 보고한다.
- 작업 중에는 60초 안에 확인한 사실·남은 불확실성·다음 확인을 짧게 알린다.

## 제품과 아키텍처

- Noxtend는 이미지 분석·파츠 분해·방향 이미지 생성·3D 재구성을 연결하는 AI 에셋 제작 스튜디오다. [PRODUCT.md](PRODUCT.md)가 제품 방향의 정본이며 노드 캔버스 방식은 제품 범위에 포함하지 않는다.
- 작업 상태의 정본은 SQL Server다. Redis Streams는 디스패치 수단이며 UI·큐 메시지가 서버 상태를 대신 결정하지 않는다.
- API와 Worker는 같은 ASP.NET Core 호스트에서 실행된다. 단계·워커 수·동시성은 현재 등록 코드·설정으로 확인한다.
- 현재 클라이언트 계약은 HTTP JSON과 파일 응답이다. `packages/proto/README.md`의 Protobuf·gRPC-Web는 후속 계획이며 실제 호출·생성 코드로 사용 여부를 판단한다.
- 부분 성공·취소·재시도·검수 승인·단계 전이를 구분한다. 근거 없이 Saga나 특정 durable workflow 구현으로 설명하지 않는다.

## 작업별 지침과 스킬

프로젝트 스킬의 정본은 `.agents/skills/`이고 `.claude/skills/`는 그 링크다. 선택한 스킬의 전체 `SKILL.md`와 작업에 필요한 참조만 읽는다.

- 코드 작성·수정·리뷰는 [karpathy-guidelines](.agents/skills/karpathy-guidelines/SKILL.md)(주석 규칙 포함), 기능·버그 수정·리팩터링·테스트 구현은 [tdd-cycle](.agents/skills/tdd-cycle/SKILL.md)을 따른다. 문서·지침만 바꾸는 작업에는 새 TDD 테스트가 필요 없다.
- 공백·줄 끝은 루트 `.editorconfig`를 따르고, Frontend 포맷·lint·계층 검사는 커밋 훅이 실행한다. C#은 포매터 없이 현재 스타일을 유지하며 일괄 재포맷하지 않는다.
- 영역 지침: [Backend](apps/backend/AGENTS.md), [Frontend](apps/frontend/AGENTS.md)와 [DESIGN.md](DESIGN.md), [인프라·배포·루트 CI](deploy/AGENTS.md). 영역별 절차와 검증 자료는 [noxtend-workflow](.agents/skills/noxtend-workflow/SKILL.md)에서 고른다.
- 여러 계층·불명확한 의존 관계는 [context-map](.agents/skills/context-map/SKILL.md), HTTP 계약 영향 리뷰는 [api-breaking-change-detector](.agents/skills/api-breaking-change-detector/SKILL.md), 프롬프트·AI 출력 비교는 [agentic-eval](.agents/skills/agentic-eval/SKILL.md)을 쓴다.
- Plan·Design만 요청하면 애플리케이션 소스를 수정하지 않는다. 실행 요청은 승인된 가역적 범위를 검증까지 진행한다.
- 계획 문서 작성과 구현 실행은 [실행 자료](.agents/skills/noxtend-workflow/references/execution.md)를 따른다. 구현은 작업별 구현·독립 리뷰(Subagent-driven)가 기본이며 사용자가 정한 방식은 다시 묻지 않는다.

## 코드 탐색과 지도

- 처음에는 [docs/xHuman/CODEMAP.md](docs/xHuman/CODEMAP.md)와 해당 영역 지도만 읽는다. 수정 전 지도가 가리킨 실제 코드·테스트와 현재 `git diff`를 확인하고, 지도와 코드가 다르면 코드·테스트를 기준으로 판단한다.
- 인증·암호화·DB migration·배포는 요약만 믿지 않는다. 지도 갱신 대상·확인 기준·같은 커밋 원칙은 [문서 유지 규칙](docs/xHuman/README.md#문서-유지-규칙)을 따른다.

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
- 기능 코드 변경은 해당 스택 전체 회귀, API 계약 변경은 양쪽 스택을 검증한다. HTTP·영속성 경계 변경은 [연결·경계 검증](.agents/skills/noxtend-workflow/references/verification.md#연결경계-검증)도 한다. 일반 검증은 Fake로 하고 유료 smoke를 제외하며, 미수집·건너뜀·환경 미설정을 통과로 계산하지 않는다.
- 현재 인증이 없다. 네트워크 노출·비밀값·저장 데이터·Data Protection 키 보존은 배포 지침을 따른다. 실제 AI·유료 smoke·Slack 전송·배포는 사용자 요청의 승인 범위에서만 수행하며 설정·환경 변수 존재를 승인으로 간주하지 않는다.
- GitHub flow를 사용한다. 최신 `main`에서 짧은 작업 브랜치를 만들고 PR로 병합한다. `main`은 PR·`Repository checks`/`Frontend`/`Backend` 검사·최신 기준 반영·미해결 대화 해소를 요구한다. GitHub 승인 인원은 0명이며 독립 에이전트 리뷰는 별도로 유지한다.
- CI 정본은 `.github/workflows/ci.yml`, 원격 보호 설정은 `.github/main-protection.json`이다. Azure 배포·Azure Pipelines는 사용하지 않는다. 로컬 Backend 배포는 실제 이미지 출처와 성공 이력을 기록하며 Frontend Vite 실행 버전과 구분한다.
- 이번 작업 파일의 diff·비밀값을 확인하고 경로를 지정해 stage한다. 전체 일괄 stage는 에이전트 훅이 막으며 필요하면 사용자가 직접 실행한다. 코드 수정·로컬 검증·실제 배포 완료는 각각 증거에 맞춰 보고한다.
- PR 병합·worktree 정리 후에는 결과 문서에 최종 상태와 유지되는 근거 위치를 덧붙이고 측정 당시 기록은 보존한다.
