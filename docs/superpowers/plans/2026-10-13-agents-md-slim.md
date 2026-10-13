# 루트 AGENTS.md 축소·일괄 stage 차단 훅 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 매 세션 로드되는 루트 `AGENTS.md`를 102줄에서 60줄 이내로 줄이고, 문장으로만 있던 "전체 작업트리 일괄 stage 금지"를 Claude Code·Codex 공용 PreToolUse 훅으로 강제한다.

**Spec:** 별도 설계 문서 없음. 기준은 [Claude Code memory 문서](https://code.claude.com/docs/en/memory)의 "파일당 200줄 미만, 일부 영역·여러 단계 절차는 스킬로, 강제 규칙은 PreToolUse 훅으로"와 사용자가 승인한 절별 이동안이다. 60줄은 실무 경험칙이며 공식 한도가 아니다.

## Global Constraints

- 지침 의미를 바꾸지 않는다. 옮기는 문장은 원문을 유지하고, 줄이는 문장은 이미 다른 정본(`docs/xHuman/README.md`, `verification.md`)에 있는 내용만 링크로 대체한다.
- `.claude/rules/`·`@` import는 쓰지 않는다(Codex 미지원, import는 컨텍스트 비용을 줄이지 않음).
- 훅은 Claude Code와 Codex가 같은 스크립트를 쓴다. 입력은 `tool_input.command`, 차단 출력은 `{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":...}}`다. 해당 없음·입력 오류는 출력 없이 exit 0으로 일반 권한 흐름에 맡긴다.
- 작업 트리의 다른 작업 변경(`deploy/*`, `docs/infrastructure.md`, `.agents/skills/noxtend-workflow/references/deployment.md`, `deploy/k8s/README.md`)은 수정·stage하지 않는다.

## Review Focus

1. 훅의 오탐·누락: 경로 지정 stage(`git add .agents/x`, `git add ./src/a.ts`), 따옴표 안 문자열, `git commit -m`의 메시지 값은 허용하고 `-A`·`--all`·`-u`·`--update`·`.`·`:/`·`*`·`commit -a`·`-am`·`git -C <dir> add -A`·복합 명령 안의 일괄 stage는 차단하는가 → Task 1 테스트.
2. 옮긴 지침의 누락: 기존 102줄의 각 규칙이 새 `AGENTS.md`, `execution.md`, `karpathy-guidelines`, 기존 정본 중 한 곳에 남는가 → Task 2 대조표.

## 실행 방식

**Subagent-driven.** Task 1(훅) → Task 2(문서) 순서다. Task 2의 `AGENTS.md` 문장이 Task 1의 훅을 가리키기 때문이다.

## Task 1: 일괄 stage 차단 훅

**Files:**

- Create: `.githooks/guard-git-stage.mjs`
- Create: `.githooks/guard-git-stage.test.mjs`
- Modify: `.claude/settings.json` — `PreToolUse` matcher `Bash`, `node "$CLAUDE_PROJECT_DIR/.githooks/guard-git-stage.mjs"`, timeout 10
- Modify: `.codex/hooks.json` — `PreToolUse` matcher `^Bash$`, `node "$(git rev-parse --show-toplevel)/.githooks/guard-git-stage.mjs"`, timeout 10
- Modify: `.agents/skills/noxtend-workflow/references/verification.md` — `## 커밋 훅` 절에 이 훅의 대상·차단 범위·우회(사용자가 직접 실행)·Codex의 `/hooks` 신뢰 필요를 한 항목으로 추가

- [x] **Step 1: 실패 테스트** — `node:test`로 스크립트에 JSON을 stdin으로 넣어 결과를 단정한다(`check-design-docs.test.mjs`와 같은 spawn 방식). 차단: `git add -A`, `git add --all`, `git add .`, `git add -u`, `git add :/`, `git stage .`, `git commit -a -m x`, `git commit -am "x"`, `cd apps && git add .`, `git -C /repo add -A`, `git status; git add -A`. 허용(출력 없음·exit 0): `git add AGENTS.md docs/a.md`, `git add .agents/skills/x.md`, `git add ./src/a.ts`, `git commit -m "git add -A later"`, `git commit --amend --no-edit`, `echo "git add -A"`, `tool_name`이 `Edit`인 입력, 빈·잘못된 JSON. 스크립트 없이 실행해 실패를 확인한다.
- [x] **Step 2: 구현** — 명령을 `&&`·`||`·`;`·`|`·줄바꿈으로 나누고 따옴표를 고려해 토큰화한다. `git` 뒤 전역 옵션(`-C <arg>`, `-c <arg>`, `--git-dir=…` 등 `--` 옵션)을 건너뛰고 하위 명령이 `add`/`stage`면 위 차단 인자 또는 `A`·`u`를 포함한 짧은 옵션 묶음을, `commit`이면 `-a`·`--all` 또는 `a`를 포함한 짧은 옵션 묶음을 찾는다. `-m`·`-F`·`--message`·`--file` 다음 토큰은 값으로 건너뛴다. 차단 사유는 한국어 한 문장으로 "경로를 지정해 stage" 안내를 포함한다.
- [x] **Step 3: 등록·문서** — 두 설정 파일에 등록하고 기존 `PostToolUse`·`Stop` 항목은 유지한다. `verification.md` 항목을 추가한다.
- [x] **Step 4: 검증** — `pnpm hooks:test` 전체 통과, `echo '{"tool_name":"Bash","tool_input":{"command":"git add -A"}}' | node .githooks/guard-git-stage.mjs`가 deny JSON 출력, `pnpm docs:check`, `git diff --check`.
- [x] **Step 5: Commit** — `feat(hooks): block bulk git staging from agents`

## Task 2: 루트 AGENTS.md 축소

**Files:**

- Modify: `AGENTS.md` — 승인된 목표 본문(59줄)으로 교체
- Create: `.agents/skills/noxtend-workflow/references/execution.md` — 기존 `## 설계·계획 문서 작성`(60초 알림 문장 제외)과 `## 설계 승인 후 실행 방식`(표 포함, 경계 검증·PR 병합 두 항목 제외)을 원문 그대로 옮김
- Modify: `.agents/skills/noxtend-workflow/SKILL.md` — 표에 `설계·계획 문서 작성·구현 실행 방식 | references/execution.md` 행 추가
- Modify: `.agents/skills/karpathy-guidelines/SKILL.md` — `## Comments` 절에 기존 주석 규칙 3항목을 원문 그대로 추가
- Modify: `docs/xHuman/CODEMAP.md` — `../../AGENTS.md#설계-승인-후-실행-방식` 링크를 `execution.md`로 변경

- [x] **Step 1: 이동** — 새 파일·절을 먼저 만들고 원문 문장을 옮긴다.
- [x] **Step 2: 교체** — `AGENTS.md`를 목표 본문으로 바꾼다.
- [x] **Step 3: 대조** — 기존 `AGENTS.md`(`git show HEAD:AGENTS.md`)의 항목마다 새 위치를 표로 보고서에 남긴다. 삭제 항목(`git rev-parse` 확인, `.git` 없는 사본, 지도 갱신 세부, `pnpm docs:check` 실행 지시)은 대체 정본 경로를 적는다.
- [x] **Step 4: 검증** — `pnpm docs:check`, `git diff --check`, 바뀐 Markdown의 상대 링크·heading anchor 존재 확인, `wc -l AGENTS.md`가 60 이하.
- [x] **Step 5: Commit** — `docs: slim root agent instructions`

## 실행 기록

- 실행 방식: Subagent-driven, 브랜치 `claude/agents-md-slim`, 시작 `d52ceb3`(같은 브랜치의 선행 커밋은 이전 작업의 미커밋 문서를 사용자 요청으로 먼저 커밋한 것).
- Task 1: `59d7f19`. RED(스크립트 없음, 2건 실패) → GREEN. `pnpm hooks:test` 4/4 통과, `git add -A` 수동 입력에 deny JSON 출력, 커밋 훅의 Frontend lint 통과. 독립 리뷰 Approved; 리뷰어가 브리프 사례와 추가 탐침(`-Av`, `-c … add .`, `GIT_DIR=x`, `-m x -a`, `--amend -a`, `-p`, `-C HEAD` 등)을 직접 실행해 확인.
- Task 1 남은 제약: `sudo`·`env`·절대 경로 `git`·`xargs`·서브셸·`eval`로 감싼 명령, `git add ./`·`..`, 주석(`#`) 뒤 문자열은 판별하지 않는다. `git add -- -A`는 오탐이다. 훅은 보조 장치이며 지침의 경로 지정 stage 원칙이 정본이다.
- Task 2: `7496ac9`, 수정 `1eebf23`. 루트 `AGENTS.md` 102줄(12,103B) → 59줄. 독립 리뷰에서 저장소 아닌 사본의 `git init` 금지·완료 주장 금지 규칙 누락(Important)을 찾아 `verification.md` 완료 근거 항목에 복원했고, "전체 `SKILL.md`" 표현도 되살렸다. `pnpm docs:check`, `git diff --check`, 상대 링크·anchor 검사 통과.
- 옮긴 위치: 계획 작성·구현 실행 방식 → `.agents/skills/noxtend-workflow/references/execution.md`, 주석 규칙 → `karpathy-guidelines`의 `## Comments`, 지도 갱신 세부 → 기존 `docs/xHuman/README.md`, 실패 판단·`.git` 없는 사본 → 기존 `verification.md`, 루트 CI 참조 스크립트 확인 → 기존 `deployment.md`.
- 미반영 Minor: "공통 지침을 복제하지 않는다", 지도에서 못 찾으면 검색 범위를 넓힌다는 문장, `noxtend-workflow` description의 실행 자료 언급은 넣지 않았다.
