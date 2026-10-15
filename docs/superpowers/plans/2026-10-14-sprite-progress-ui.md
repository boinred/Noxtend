# 2D 진행 표시·원본 후보·공정 요약 개선 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 2D 작업 화면에서 (1) 공정 실행·대기 중 진행 막대가 멈춘 것처럼 보이는 문제를 buffer 줄무늬와 현재 단계 활동 점으로 해결하고, (2) 원본 후보를 후보 수에 맞는 크기(1장 전체 폭, 2장 2열, 3장 이상 최대 3열)로 보여주며 선택한 후보를 크게 펼치고, (3) 같은 종류 공정 행 나열을 종류별 한 줄 집계로 바꾼다.

**Spec:** 사용자 승인 목업(2026-10-14 대화, 목업 HTML은 세션 scratchpad)과 [DESIGN.md](../../../DESIGN.md)의 진행 상태·모션 원칙. 별도 설계 문서는 없다.

**Tech Stack:** React 19, Tailwind, Vitest(node 환경, 순수 함수), Playwright(Fake API).

## Global Constraints

- 진행률은 서버가 확정한 단계 수만 쓴다. 단계 내부 퍼센트·경과 시간 추정을 만들지 않는다.
- 반복 모션은 실제 실행·대기 중일 때만, `prefers-reduced-motion: reduce`에서는 정지한다. 기존 `stripe-drift` keyframe과 `src/index.css`의 `[data-status-activity]` 규칙을 재사용한다.
- 상태는 색만으로 전달하지 않는다. 텍스트·ARIA를 유지한다.
- 실패 공정은 개별 행으로 실패 이유·`시도 n`·[실패 공정 재시도]를 그대로 유지한다.
- 계층 경계(`apps/frontend/AGENTS.md`): 집계 같은 순수 규칙은 `src/domain/`, 공용 표시 컴포넌트 안에 작업 종류 분기를 넣지 않는다.
- 1440×900·390px에서 가로 넘침이 없어야 한다.
- 주석은 `karpathy-guidelines`의 `## Comments`. 각 Task의 `DESIGN.md`·`docs/xHuman/frontend.md` 수정은 같은 커밋.
- 검증은 Fake API만 사용한다. 실제 AI 호출 금지.

## Review Focus

1. 실행 중 buffer: `job.status`가 `running`·`pending`일 때만 buffer와 활동 점이 보이고 `pendingReview`·종료 상태에서는 없다 → Task 1 e2e.
2. 후보 1장은 전체 폭, 선택 후보는 큰 보기와 펼친 프롬프트, 넓은 화면 최대 3열 → Task 2 e2e.
3. 공정 요약 수: 현재 frame·최신 원본 공정만 집계하고 완료된 과거 슬롯 공정을 세지 않는다 → Task 3 단위 테스트.

## 실행 방식

**Subagent-driven.** Task 1 → 2 → 3 순서(서로 다른 파일이지만 같은 e2e 파일을 수정하므로 순차). 작업마다 새 구현 에이전트와 별도 리뷰 에이전트, 마지막에 전체 변경 리뷰와 Frontend 전체 회귀.

```bash
pnpm --filter @nextend/frontend exec vitest run <파일>
pnpm --filter @nextend/frontend exec playwright test tests/e2e/sprites-static.spec.ts
pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e
```

## Task 1: 공용 진행 막대 buffer와 현재 단계 활동 점

**Files:** `apps/frontend/src/features/screens/JobProgressPanel.tsx`, `apps/frontend/tests/e2e/sprites-static.spec.ts`, `docs/xHuman/frontend.md`

- [x] `status`가 `running` 또는 `pending`이면 `role="progressbar"` 안에 완료 막대 뒤로 buffer를 그린다. 폭은 `max(완료율, currentPosition / steps.length)`. 스타일은 `src/features/screens/background/backgroundStyles.ts`의 `progressBuffer`(줄무늬 `stripe-drift`, `motion-reduce` 정지)를 재사용하고 `data-testid="progress-buffer"`를 붙인다. 그 외 상태에서는 buffer를 렌더하지 않는다.
- [x] 같은 조건에서 현재 단계 카드(`aria-current="step"`)에 `aria-hidden` 활동 점을 추가한다. `data-status-activity` 속성과 `animate-pulse motion-reduce:animate-none`을 쓴다. 점이 없어도 기존 `statusText`·헤더 상태 텍스트로 상태가 전달돼야 한다.
- [x] 공용 컴포넌트이므로 3D 배경·캐릭터 stepper에도 같이 적용된다(DESIGN.md "현재 실행 단계는 buffer로 구분"과 일치). 작업 종류 분기를 넣지 않는다.
- [x] e2e: 원본 공정 실행 중에는 `progress-buffer`가 보이고, 공정이 끝나 `pendingReview`가 되면 사라짐을 기존 원본 준비 테스트에 단언으로 추가한다.
- [x] `docs/xHuman/frontend.md`의 `JobProgressPanel` 설명에 buffer·활동 점 조건을 한 줄로 추가한다.

## Task 2: 원본 후보 크기와 선택 후보 큰 보기

**Files:** `apps/frontend/src/features/screens/sprites/SpriteSourceDraftPanel.tsx`, `apps/frontend/src/features/screens/sprites/spriteStyles.ts`, `apps/frontend/tests/e2e/sprites-static.spec.ts`, `DESIGN.md`, `docs/xHuman/frontend.md`

- [x] 후보 격자 열 수: 1장 1열, 2장 2열, 3장 이상 3열. 720px 이하에서는 1장 1열, 2장 이상 2열. Tailwind는 정적 클래스 이름이어야 하므로 열 수별 클래스를 명시적으로 고른다.
- [x] 후보 이미지는 고정 높이(`h-48`) 대신 칸 폭을 채우고 원본 비율을 유지한다(`h-auto w-full object-contain`, 세로로 긴 이미지 대비 `max-h-[70vh]`). `SpriteFrameReview.tsx`가 쓰는 `styles.image`는 바꾸지 않고 후보 전용 스타일을 추가한다.
- [x] 후보가 1장이면 "사용한 프롬프트"를 처음부터 펼친다(`details open`).
- [x] 후보가 2장 이상이고 하나를 선택하면 격자 위에 선택 후보 큰 보기(`data-testid="sprite-source-selected"`)를 둔다. 패널 전체 폭 이미지(대체 텍스트 `선택한 원본 후보 n`) 아래에 "사용한 프롬프트" 라벨과 프롬프트 전문을 펼쳐 보여준다(`whitespace-pre-wrap wrap-anywhere`, 본문 `text-sm leading-relaxed text-foreground`). 격자의 해당 후보는 기존 `aria-pressed` 링으로 선택을 표시한다. 선택을 해제하면 큰 보기가 사라진다.
- [x] e2e: 기존 열 수 단언(1440px 3열·390px 2열, 후보 3장)을 유지하고, 후보 1장일 때 1열·프롬프트 펼침, 후보 선택 시 큰 보기와 프롬프트 표시·해제 시 사라짐을 추가한다. 긴 프롬프트에서 가로 넘침이 없는지 기존 `bounded` 검사를 큰 보기 상태에서도 실행한다.
- [x] `DESIGN.md` 버튼과 입력의 원본 준비 문단과 `docs/xHuman/frontend.md`의 `SpriteSourceDraftPanel` 설명을 새 배치로 갱신한다.

## Task 3: 공정 목록 종류별 한 줄 집계

**Files:** `apps/frontend/src/domain/job/types.ts`, `apps/frontend/src/domain/job/types.test.ts`, `apps/frontend/src/features/screens/sprites/SpriteStudioScreen.tsx`, `apps/frontend/tests/e2e/sprites-static.spec.ts`, `DESIGN.md`, `docs/xHuman/frontend.md`

- [x] 순수 함수 `taskStatusGroups(tasks)`를 `domain/job/types.ts`에 추가한다. 입력 순서대로 첫 등장 kind 순서를 유지해 `{ kind, total, running, pending, succeeded, failed, canceled, retrying }`를 돌려준다. `retrying`은 `running`·`pending`이면서 `attemptCount > 1`인 수다.
- [x] `SpriteStudioScreen`은 현재 표시 대상 공정(생성 공정은 현재 `frame.currentTaskId`, 원본 공정은 최신 Ordinal 하나 — 기존 필터에서 `status !== 'succeeded'` 조건만 뺀 집합)을 집계한다. 성공이 아닌 공정이 하나라도 있는 종류만 한 줄로 표시한다: `라벨 · 전체 n · 실행 중 n · 대기 n · 성공 n · 실패 n · 취소 n · 재시도 중 n`, 0인 항목은 숨긴다. 상태 라벨은 `jobStatusLabel`을 쓴다. 각 줄에 `data-testid="sprite-task-summary"`.
- [x] 실패 공정은 집계 줄 아래 기존과 같은 개별 행(`라벨 · 실패 · 시도 n`, 실패 이유, [실패 공정 재시도])으로 유지한다.
- [x] 단위 테스트: 종류별 수·순서·`retrying`·빈 입력. e2e: 기존 `원본 후보 생성 · 실행 중` 같은 문구 단언을 새 형식으로 갱신하고, 배경 생성 공정 여러 개가 한 줄로 표시됨을 확인한다.
- [x] `DESIGN.md` 진행 상태에 "2D 공정 목록은 종류별 한 줄 집계, 실패만 개별 행" 한 줄, `docs/xHuman/frontend.md`의 `SpriteStudioScreen` 설명을 갱신한다.

## 실행 기록

- 방식: Subagent-driven. Task 1 `f4ef16b`, Task 2 `62e1b6a`, Task 3 `3341de5`. 작업별 리뷰는 모두 Approved
- 최종 전체 리뷰: Important 1건. `재시도 중`이 Backend 시도 수 의미와 어긋남(`Claim`이 `AttemptCount++`, `ReleaseForRetry`는 유지) → `(running && attemptCount > 1) || (pending && attemptCount > 0)`로 수정하고, DESIGN.md 활동 점·대기 중 반복 모션 문구를 정리(`43c33e2`). 재리뷰에서 모두 해소
- 결정: buffer는 공용 `JobProgressPanel`에 넣어 3D 배경·캐릭터에도 적용. 2D 집계 줄은 `전체 n`을 항상 표시. job `pending`에서도 buffer가 흐름
- 검증(2026-10-14, Fake API): `pnpm test` 503 통과, `pnpm lint`·`pnpm typecheck`·`pnpm build` 통과, Playwright 363 통과. 4173 포트를 무관한 `vite preview`가 점유하고 있어 4183 임시 config로 실행
- 후속 후보: 3D 실행 중 상단 stepper와 `StageProgress`의 buffer 두 개가 함께 흐르는지 시각 확인. 2D 집계에서 `packSprites`·`analyzeSprites`의 과거 성공 공정이 합산되는 점
- 병합(2026-10-14): PR #13을 merge commit `d415365`로 `main`에 병합하고 원격·로컬 브랜치를 삭제했다. 측정 기록은 위 검증 항목 그대로이며 근거는 이 문서와 PR 본문에 남는다
