# 검증 절차

명령은 별도 표시가 없으면 저장소 루트 기준이다. 런타임·명령의 정본은 `package.json`·`pnpm-lock.yaml`·`.csproj`·실제 스크립트이며 문서의 과거 테스트 수를 재사용하지 않는다.

## 변경 범위 선택

| 변경 | 검증 |
| --- | --- |
| 기능 코드 | 관련 동작 검증 → 영향받는 스택 전체 회귀 |
| HTTP 계약·상태 값 | Backend와 Frontend 모두 회귀 |
| 일반 문서·AGENTS·스킬 | 링크·참조 경로·명령/실행 위치·지침 충돌·변경 범위·공백; skill-creator의 `quick_validate.py`가 제공되면 사용하고 없으면 frontmatter·이름·참조를 직접 검증 |
| 스타일 도구 설정 | 구성 파싱·대상/ignore·변경 파일 포맷 검사·기존 lint/typecheck/build; 동작 변경이 없으면 새 TDD 테스트 불필요 |
| 배포 설정 | [deployment.md](deployment.md)의 관련 정적 검증과 승인된 환경 검증 |

루트의 앱 실행·검증 `pnpm` 스크립트는 Frontend를 대상으로 한다. Backend 회귀를 대체하지 않는다. 배포·실 공급자 호출은 검증 설정이 존재한다는 이유만으로 실행하지 않는다.

## Backend

변경된 동작 테스트부터 실행한다. .NET 10 SDK가 필요하다.

```bash
dotnet build apps/backend/Noxtend.slnx
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~Noxtend.Tests.Domain|FullyQualifiedName~Noxtend.Tests.Application|FullyQualifiedName~Noxtend.Tests.Api|FullyQualifiedName~Noxtend.Tests.Architecture'
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'
```

- 두 번째 명령은 Docker 없는 Domain/Application/Api/Architecture 회귀, 세 번째는 SQL Server·Redis Testcontainers를 포함하는 전체 회귀다. 일반 회귀에서는 유료 smoke를 제외한다.
- 전체 회귀는 Docker가 필요하다. SQL fixture는 컨테이너를 공유하고 테스트별 DB를 만든다. fixture를 개발·운영 DB/Redis로 대체하지 않는다.
- 명시적으로 요청된 유료 smoke: `TripoSmokeTests`는 `TRIPO_SMOKE_TEST=1`·`TRIPO_API_KEY`, `SimilaritySmokeTests`는 `RUN_SIMILARITY_SMOKE=1`·`SIMILARITY_SMOKE_PROVIDER`·`SIMILARITY_SMOKE_MODEL`·`SIMILARITY_SMOKE_KEY`·`SIMILARITY_SMOKE_FIXTURES`가 필요하다.
- `SimilaritySmokeTests`는 조건 미충족 시 조기 return한다. 통과 표시만으로 실 공급자 검증을 주장하지 않고 실행 여부·호출 수·건너뜀·환경 제약을 보고한다.

## Frontend

먼저 관련 테스트를 선택하고 기능 코드 변경이면 전체 회귀를 실행한다. 개발 서버는 `pnpm dev`, 기본 포트 5173이다.

```bash
pnpm --filter @nextend/frontend test src/infra/api/client.test.ts
pnpm --filter @nextend/frontend test:e2e tests/e2e/review-gate.spec.ts
pnpm test
pnpm lint
pnpm typecheck
pnpm build
pnpm test:e2e
```

- 첫 두 명령은 예시이므로 변경 동작에 맞는 파일로 대체한다.
- `pnpm test`는 현재 Node 환경의 `src/**/*.test.ts`만 수집하고 `.test.tsx`는 수집하지 않는다. DOM 상호작용은 Playwright 또는 명시적인 테스트 설정 변경으로 검증한다.
- `pnpm build`는 TypeScript·Vite·`scripts/check-bundle.mjs` 초기 JS gzip 예산 검사다. 예산을 완화해 번들 증가를 숨기지 않고 import·지연 로드를 확인한다.
- Playwright는 빌드 후 `127.0.0.1:4173` preview를 사용한다. 기존 서버 재사용 시 이전 빌드를 테스트하지 않는지 확인하고 무관한 프로세스를 종료하지 않는다.
- `tests/e2e/fakeApi.ts`의 가짜 API로 요청·응답·오류·단계 전이를 확인한다. 실제 공급자 생성은 비용·승인 범위를 확인한 별도 검증이다.
- 계약·상태 변경은 `src/domain/job/backendParity.test.ts`, API client 테스트·관련 가짜 응답을 함께 검토한다. 테스트 파일 존재와 실제 수집·실행을 구분한다.

## 커밋 훅

```bash
pnpm hooks:install
pnpm hooks:test
```

- `.githooks/pre-commit`은 staged 변경에 `apps/frontend/`, 루트 `package.json`·`pnpm-lock.yaml`·`pnpm-workspace.yaml`·`.editorconfig` 또는 `.githooks/`가 있으면 기존 Frontend `lint`를 실행한다. Backend만 변경한 커밋과 staged 변경이 없는 실행은 건너뛴다. lint 또는 Git diff 실패는 커밋을 차단한다.
- 훅은 검사만 수행하며 자동 포맷·stage·stash를 하지 않는다. 실행 대상은 staged 경로로 결정하지만 lint는 현재 작업 트리 전체 Frontend를 읽는다. 부분 stage의 커밋 내용만 격리해 검증하는 것은 아니다.
- `hooks:install`은 이 checkout의 Git 설정에 `.githooks` 경로를 등록한다. 새 clone에는 한 번 등록해야 한다. Git의 `--no-verify`로 우회할 수 있으므로 훅 통과가 CI·전체 회귀·수동 검증을 대신하지 않는다.
- `hooks:test`는 임시 Git 저장소와 가짜 pnpm으로 실행·건너뜀·실패 전파 및 실제 커밋 차단을 확인한다. 실제 Frontend 검사 명령은 위의 `pnpm lint`를 사용한다.

## 설계·계획 문서 훅

```bash
pnpm docs:check
pnpm docs:check docs/superpowers/specs/2026-10-06-2d-background-sprites-design.md
pnpm hooks:test
```

- 정본 검사는 `.githooks/check-design-docs.mjs`다. 대상은 `docs/superpowers/specs/`와 `docs/superpowers/plans/`의 Markdown이며 다른 문서를 검사했다고 주장하지 않는다.
- 제목·닫히지 않은 코드 블록·설명에 혼입된 일본어 가나·미완성 TODO/TBD/FIXME·로컬 inline Markdown 링크의 대상 존재를 검사한다. 코드 블록·inline code의 일본어는 제외한다. reference-style 링크·anchor·코드 심볼·전체 언어 판별·설계의 타당성은 자동 검사 범위가 아니다.
- 설계는 목적·범위·검증 제목이 필요하다. 계획은 Goal·Spec 링크·Global Constraints·Review Focus와 Task별 Files·체크리스트가 필요하며 한국어 대응 항목도 허용한다. 신규 파일은 계획의 코드 표기로 적고 아직 없는 파일을 현재 근거처럼 링크하지 않는다.
- 500행 초과는 중복과 과도한 상세화를 검토하라는 경고다. 프로젝트의 초기 휴리스틱이며 품질·처리 시간 기준이나 업계 표준이 아니다. 경고는 커밋을 차단하지 않는다.
- Git `pre-commit`은 기존 Frontend lint 전에 변경된 staged 문서와 링크 대상의 index 상태를 검사한다. 작업 트리 내용으로 부분 stage 오류를 숨기지 않는다. 문서만 바뀌면 Frontend lint는 실행하지 않으며 검사 실패·Git 오류는 커밋을 차단한다.
- `.codex/hooks.json`과 `.claude/settings.json`은 같은 검사를 PostToolUse·Stop에 등록한다. PostToolUse는 Write/Edit/apply_patch/Bash의 입력에 대상 문서 경로가 포함될 때 변경·untracked 문서를 검사한다. 상대 경로를 생략한 shell 쓰기와 다른 도구는 Stop에서 보완한다. 완료된 파일 쓰기를 되돌리지 않고 수정 피드백을 전달한다.
- Stop 실패는 수정 작업을 한 번 더 요청한다. `stop_hook_active`인 재진입에서는 검사하지 않아 무한 반복을 피하며 최종 커밋은 Git 검사로 다시 제한한다. 자동 포맷·stage·파일 수정·유료 LLM 호출은 없다.
- Codex 프로젝트 훅은 신뢰된 프로젝트에서만 로드하며 새·변경 훅은 CLI `/hooks`에서 정의를 검토하고 신뢰해야 실행된다. 등록 파일 작성만으로 현재 Desktop 세션에서 자동 실행되었다고 판단하지 않는다. [Codex 공식 안내](https://learn.chatgpt.com/docs/hooks#review-and-trust-hooks), [Claude Code 공식 안내](https://code.claude.com/docs/en/hooks#hook-locations)를 따른다.
- `hooks:test`는 임시 저장소에서 오류·경고·에이전트 JSON 피드백·부분 stage·미stage 링크 대상과 실제 커밋 차단을 확인한다. 네이티브 에이전트 이벤트의 실제 발화는 별도로 확인해야 한다.

## 코딩 스타일 도구

- 기본 공백·인코딩·줄 끝은 루트 `.editorconfig`를 따른다. Frontend 포맷은 `apps/frontend/.prettierrc.json`·`.prettierignore`, 코드 품질·계층 검사는 `apps/frontend/eslint.config.js`·기존 Oxlint를 사용한다. `eslint-config-prettier`로 포맷 규칙 충돌을 막고 `pnpm lint`에 포맷 검사도 포함한다.
- 루트 `pnpm format`·`pnpm format:check`는 Frontend로 전달된다. 전자는 쓰기, 후자는 읽기 전용이며 Backend나 루트 문서를 포맷하지 않는다. Frontend Markdown은 `.prettierignore`의 `**/*.md`로 제외된다.
- Prettier는 기존 Frontend의 공백 2칸·single quote·semicolon 없음 스타일을 유지한다. 쓰기 전 범위를 확인하고 무관한 수정·untracked 파일을 일괄 재포맷하지 않는다. 지원 CLI에 파일 경로를 전달해 이번 변경 파일부터 검사한다.
- C#은 기존 스타일을 유지한다. Prettier 대상에 C#을 추가하거나 `dotnet format`·새 C# 스타일 분석기·일괄 재포맷을 이 작업에 묶지 않는다.

## 완료 근거

- 구현·자동 검증·수동 확인·실 API/배포 확인을 구분한다. 미실행·미수집·건너뜀·환경 미설정을 통과로 계산하지 않는다.
- 도구·Docker·브라우저·자격 증명 부족은 명령·오류를 보고한다. 기존 실패라는 판단에는 변경 전 재현 등 증거가 필요하다.
- Git 저장소에서 이번 파일의 diff·비밀값·공백을 확인한다. `.git` 없는 사본이나 전체 untracked 상태에서는 변경 전 사본과 파일 비교도 사용한다.
