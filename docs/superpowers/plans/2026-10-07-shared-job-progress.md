# 작업 진행 상태 공용 컴포넌트 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 현재 2D 진행 상태 화면을 보존하면서 3D 배경·캐릭터와 이후 작업이 같은 표시 컴포넌트를 재사용한다.

**Architecture:** 공용 컴포넌트는 헤더·막대·단계·요약·아이콘 버튼과 펼침 상태를 담당한다. 기존 작업별 컴포넌트는 단계 판정·집계 입력을 만들고 화면은 조회·취소·화면 이동을 담당한다. 기존 집계 함수와 계산 규칙을 재사용한다.

**Tech Stack:** React 19, TypeScript, Vite, Tailwind, 기존 Button·Icon, Vitest, Playwright Fake API.

**Spec:** [승인 설계](../specs/2026-10-07-shared-job-progress-design.md)

## Global Constraints

- 현재 2D 화면의 모양과 동작 보존. 시각적 개선을 추가로 제안할 경우 구현 전에 목업으로 확인받는다.
- 작업 단계·서버 상태 전이, HTTP 계약, 생성 요청, 검수·재시도 로직, 모델 선택, 결과 화면은 변경 범위에 포함하지 않는다.
- 새 UI 라이브러리나 작업 종류별 등록 시스템도 추가하지 않는다.
- 공용 컴포넌트 안에 2D·3D·캐릭터 분기나 서버 요청을 넣지 않는다.
- 종료 상태만으로 단계 완료를 채우지 않는다. 현재 이미지 슬롯·실제 3D 제작 대상 기준으로 집계한다.
- 기존 접근성, 오류·재시도·시도 정보와 취소 후 이동을 유지한다. 검증은 Fake를 사용하고 유료 호출은 실행하지 않는다.
- 구현 코드와 해당 `docs/xHuman/frontend.md` 수정은 같은 커밋에 포함한다. 재사용 지침은 실제 구현·검증 뒤 추가한다.

## Review Focus

1. 부분 내보내기·검수 재진입에서 체크와 완료율을 강제로 채우지 않음: Task 1의 정적·부분 성공 회귀.
2. 상세를 접거나 모바일·테마를 바꿔도 요약·오류·조작이 유지됨: Task 1 기준 이미지 비교, Task 2 두 3D 화면의 키보드·390px 검증.
3. 생성 대상 미확정과 조회 오류를 성공 `0 / 0`으로 숨기지 않음: Task 2 대상 확인·캐시된 조회 실패 테스트.
4. 3D 미선택과 후속 제작 결과를 구분하고 최신 결과의 실제 분모를 사용함: Task 2 미선택·부분 제작·후속 생성 테스트.
5. 취소 확인 닫기·요청 중·조회 실패에서 중복 취소가 발생하지 않음: Task 1 기존 취소 회귀, Task 2 요청 수·비활성·화면 이동 테스트.

## 실행 방식과 공통 계약

**실행 방식: Subagent-driven.** 저장소의 기존 선택을 유지한다. 주 에이전트는 새 구현 에이전트에 작업의 Files·Interfaces·검증을 전달하고, 구현에 참여하지 않은 새 에이전트가 설계 준수와 코드 품질을 검토한다. 지적 수정과 수정 범위 재리뷰가 끝난 뒤 다음 작업으로 넘어간다. 같은 파일을 동시에 편집하지 않는다.

현재 `codex/shared-job-progress-design` 체크아웃을 이어 사용한다. 작업 트리 변경을 먼저 확인하고 다른 작업을 보존한다. 별도 격리가 실제 필요한 경우에만 기존 작업 트리·worktree를 확인해 선택한다.

계획 경로 `apps/frontend/src/features/screens/JobProgressPanel.tsx`에서 다음 표시 타입과 컴포넌트를 export한다. `JobStatus`는 `domain/job/types`, `IconName`은 `features/shell/Icon`, `ReactElement`는 React의 기존 타입이다.

```ts
type JobProgressActions = {
  onRefresh: () => void
  onCancel?: () => void
  cancelDisabled?: boolean
}
type JobProgressStep = {
  id: string
  label: string
  icon: IconName | null
  state: JobStatus | 'upcoming' | 'incomplete'
  completed: boolean
  statusText: string
  testId?: string
}
type JobProgressSummary = {
  id: string
  label: string
  value: string
  hint: string
  icon: IconName
}
type JobProgressPanelProps = JobProgressActions & {
  status: JobStatus
  steps: readonly JobProgressStep[]
  currentStepId: string
  currentPosition: number
  currentLabel: string
  revision?: number
  summaries: readonly JobProgressSummary[]
  navigationLabel: string
}
function JobProgressPanel(props: JobProgressPanelProps): ReactElement
```

`steps`에는 실제 제작 단계의 비어 있지 않은 목록을 전달한다. 현재 위치는 기존 계산의 값을 전달하고 완료 수와 구분한다. `icon: null`은 대기 상태의 점, `completed`는 완료율 계산, `statusText`는 스크린 리더 설명에 사용한다. 표시 상태와 완료 판정을 분리하고 기존 2D의 활성·완료·이전 미완료·후속 대기 색상 우선순위를 유지한다.

위 계약 외에 registry·범용 factory·별도 상태 머신을 추가하지 않는다. 공통 상태색·제목·버튼은 같은 파일에 둔다.

## Task 1: 2D 화면을 보존하는 공용 표시 추출

**Files:**
- Create: `apps/frontend/src/features/screens/JobProgressPanel.tsx`
- Modify: `apps/frontend/src/features/screens/sprites/SpriteProgress.tsx`
- Modify: `apps/frontend/src/features/screens/sprites/SpriteStudioScreen.tsx`
- Modify: `docs/xHuman/frontend.md`의 실제 표시 소유자 안내
- Test: `apps/frontend/tests/e2e/sprites-static.spec.ts`, `apps/frontend/tests/e2e/sprites-animation.spec.ts`

**Interfaces:**
- Consumes: `spriteProgressCounts(sprite: SpriteState, tasks: readonly JobTask[])`와 기존 단계·phase 보정; 함수의 실제 반환 타입을 그대로 사용한다.
- Produces: 위 공통 계약과 `SpriteProgress(props: { job: Job; sprite: SpriteState } & JobProgressActions): ReactElement`.
- `SpriteStudioScreen`이 현재 취소 확인과 mutation을 감싼 콜백을 전달한다. `children` 안의 버튼 마크업은 공용 표시로 옮긴다.

- [ ] **Step 1: 수정 전에 기존 동작과 기준 화면 확보**
  `sprites-static.spec.ts`의 상태 요약·메타데이터 검수·부분 내보내기·취소 테스트와 `sprites-animation.spec.ts`의 현재 프레임 슬롯 테스트를 실행한다. 이 실행으로 새로 얻은 `/tmp/noxtend-sprite-status-{desktop,mobile,light,animation-mobile}.png`를 별도 작업 아티팩트 위치에 복사한 뒤 변경 후 검증을 진행한다. 성공·실패·취소·접힘 상태도 같은 Fake로 캡처해 이후 비교한다.

  Run: `CI=1 pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-static.spec.ts tests/e2e/sprites-animation.spec.ts`

  Expected: 실제 수집된 테스트 통과와 기준 PNG 확보. 4173 포트가 사용 중이면 소유자를 확인하고 무관한 서버를 종료하지 않는다. `CI=1`은 오래된 preview 재사용을 방지한다.

- [ ] **Step 2: 최소 표시 코드 추출**
  `JobProgressPanel`로 현재 헤더·막대·단계·요약과 버튼의 JSX·클래스를 옮긴다. `SpriteProgress`에 기존 단계·완료 판정·요약 계산을 남겨 공통 입력을 만든다. `state`·아이콘·스크린리더 문구는 현재 완료 여부와 활성/이전/후속 단계에 맞게 연결한다. 활성 부분 성공에서 완료 체크가 있어도 상태색·문구는 경고를 유지한다.

  현재 펼침 초기값 `true`, `useId`·`aria-controls`, 숨겨지는 단계 목록, 제목·버튼 이름·title, `py-2`, 카드 열 규칙, 요약 순서와 revision 표시를 유지한다. 바깥 패널·오류·현재 task 목록은 기존 화면에 둔다. 동작 보존 추출에는 인위적인 실패 테스트를 추가하지 않고 기존 회귀를 먼저 실행한다.

- [ ] **Step 3: 실제 문서 동기화와 회귀 검증**
  `docs/xHuman/frontend.md`에 공용 파일·`SpriteProgress`의 연결 책임을 기록한다. 3D 연결은 아직 현재 사실로 쓰지 않는다. Step 1 명령과 `pnpm typecheck`, `pnpm lint`, `pnpm docs:check`를 실행한다. 같은 조건의 PNG를 기준과 비교해 시각 차이가 없음을 확인한다.

  유지할 기존 단정:
  ```ts
  await expect(panel.getByLabel('원본 분석 성공')).toHaveText('1 / 1')
  await expect(panel.getByLabel('배경 생성 성공')).toHaveText('2 / 2')
  await expect(progress).toHaveAttribute('aria-valuenow', '3')
  await expect(progress).toHaveAttribute('aria-valuemax', '5')
  expect(cancelRequests).toBe(0)
  // 취소 확인 수락 후
  expect(cancelRequests).toBe(1)
  ```

- [ ] **Step 4: 독립 리뷰·수정·커밋**
  리뷰어는 공통 계약, 2D 계산 보존, 화면 비교와 수집·실행 결과를 검토한다. 지적 수정과 재리뷰 뒤 이번 파일만 stage하고 `git diff --cached --check`와 커밋 훅을 통과시켜 `refactor(frontend): share sprite job progress presentation`으로 커밋한다. 기준·변경 후 아티팩트와 검증 결과를 실행 기록에 남긴다.

## Task 2: 3D 배경·캐릭터 연결과 재사용 지침 반영

**Files:**
- Modify: `apps/frontend/src/features/screens/background/BackgroundPipelineStepper.tsx`, `apps/frontend/src/features/screens/character/CharacterPipelineStepper.tsx`
- Modify: `apps/frontend/src/features/screens/background/BackgroundStudioScreen.tsx`, `apps/frontend/src/features/screens/character/CharacterStudioScreen.tsx`
- Modify: `apps/frontend/src/features/screens/background/RunProgress.tsx`, `apps/frontend/src/features/screens/background/LiveActionBar.tsx`
- Rename: `apps/frontend/src/features/screens/character/CharacterPipelineStepper.test.tsx` → `apps/frontend/src/features/screens/character/CharacterPipelineStepper.test.ts`
- Test/Modify: `apps/frontend/src/features/screens/background/BackgroundPipelineStepper.test.ts`
- Create: `apps/frontend/tests/e2e/shared-job-progress.spec.ts`
- Test/Modify: `apps/frontend/tests/e2e/background-studio-actions.spec.ts`, `apps/frontend/tests/e2e/background-studio-e2e.spec.ts`, `apps/frontend/tests/e2e/live-result.spec.ts`의 기존 취소 선택자와 확인 동작
- Modify: `apps/frontend/AGENTS.md`, `docs/xHuman/frontend.md`

**Interfaces:**
- Consumes: Task 1의 `JobProgressPanel`·표시 타입·행동 타입, 기존 `computeStepStates(tasks: JobTask[], jobStatus?: string)` 반환 상태, `generationTally(job: Job)`·`meshTally(job: Job)`.
- Produces: `BackgroundPipelineStepper(props: { job: Job; className?: string } & JobProgressActions): ReactElement`, 캐릭터는 같은 props 구조의 `CharacterPipelineStepper`.
- `computeStepStates` export와 규칙은 유지한다. `RunProgress` 입력은 `{ sourceImageId: string; tasks: JobTask[] }`, `LiveActionBar` 입력은 `{ job: Job }`로 줄이며 취소 버튼만 상단으로 이동한다.

- [ ] **Step 1: 새 동작의 실패 테스트 작성**
  `shared-job-progress.spec.ts`에서 두 작업의 헤더·7단계·성공 요약·펼침/접힘을 검증한다. 기존 Fake API의 실제 작업을 사용하고 테스트 안에서 필요한 응답만 조정한다. 제품 로직용 fixture 시스템을 새로 만들지 않는다.

  배경 종료 fixture: `installFakeApi(page, { seedTerminalJobs: 1 })`, `/background/seed-job-0`에서 단정한다. 현재 Fake는 서술 재작성 task가 없는 실제 7단계 입력을 제공하므로 완료 수는 6이다.
  ```ts
  const panel = page.getByTestId('background-pipeline-stepper')
  await expect(panel.getByLabel('원본 분석 성공')).toHaveText('1 / 1')
  await expect(panel.getByLabel('파츠 이미지 생성 성공')).toHaveText('16 / 16')
  await expect(panel.getByLabel('3D 모델 생성 성공')).toHaveText('4 / 4')
  await expect(panel.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '6')
  await expect(panel.getByRole('progressbar')).toHaveAttribute('aria-valuemax', '7')
  ```

  캐릭터는 기존 업로드·성별 선택·접수 여정으로 같은 집계와 `방향 이미지 생성 성공` 이름을 검증한다. 각 테스트 이름과 추가 단정은 다음 표로 고정한다.

  | 테스트 | 입력과 단정 |
  | --- | --- |
  | `partial results keep actual denominators` | `generation: 'partial'`, `meshOutcome: 'mixed'`: 이미지 `15 / 16`, 모델 `1 / 4`, 완료되지 않은 생성 단계 유지 |
  | `unplanned results stay unknown` | 생성 task·이미지·mesh가 없는 응답: 이미지 `대상 확인 중`, 모델 요약 없음, `0 / 0` 없음 |
  | `mesh results remain visible after later generation` | `models.mesh=null`인 응답에 실제 reconstruct task·mesh 결과 유지: 모델 요약 표시; 둘 다 없는 응답에서는 생략 |
  | `collapsed progress remains usable on mobile` | 두 작업의 Enter/Space 접기·펼치기, 요약 유지, 390px 넘침 없음, light/dark·reduced motion 확인 |
  | `cancel requires acceptance and preserves input` | 두 작업에서 닫기 0회·수락 1회, 취소 중 비활성, 기존 이미지·모델·캐릭터 입력 이어받기 유지 |
  | `cached query failure disables cancellation` | 기존 작업 조회 후 재조회 실패: 오류·기존 화면 유지, 취소 비활성, 새로고침으로 복구 |
  | `retry details remain visible` | 실패 task의 `attemptCount=3`: 실패 이유·시도 횟수·재시도 행동 유지 |

  Run: `CI=1 pnpm --filter @nextend/frontend test:e2e tests/e2e/shared-job-progress.spec.ts`

  Expected: 새 성공 요약과 취소 확인이 아직 없어 해당 동작 단정에서 실패. 실행·fixture 오류는 정상 Red로 기록하지 않는다.

- [ ] **Step 2: 기존 상태 계산과 공용 표시 연결**
  두 Stepper의 단계 순서·현재 위치 계산·`computeStepStates`를 유지하고 기존 표시 마크업을 공용 호출로 대체한다. 단계별 현재 표시 상태와 완료 여부를 분리해 전달한다. 기존 `stepper-step-*`·`data-state`·상위 test ID·탐색 이름을 유지한다. 패널은 2D와 같은 토큰·외관으로 구성한다.

  요약 이름은 `원본 분석 성공`, 배경 `파츠 이미지 생성 성공` / 캐릭터 `방향 이미지 생성 성공`, `3D 모델 생성 성공`으로 고정한다. 값은 기존 집계 함수의 결과를 `성공 / 대상` 형식으로 전달하고 대상 미확정·실제 mesh 유무는 표의 테스트 조건을 따른다. revision을 만들어 넣지 않는다.

- [ ] **Step 3: 취소 행동 통합과 기존 검증 연결**
  각 화면에서 `onRefresh={() => void refetch()}`와 비종료 작업의 취소 콜백을 전달한다. `window.confirm('작업을 취소할까요?')`가 true인 경우에만 기존 `cancelJob.mutate`와 `goToInput`을 실행한다. 요청 중·`jobError`에서 취소를 비활성화한다. 2D 확인 문구는 변경하지 않는다.

  `RunProgress`·`LiveActionBar`의 취소 props·버튼만 제거하고 다른 상태 정보는 유지한다. 기존 취소 테스트의 `run-cancel`·`live-cancel` 선택자를 상단 `작업 취소`의 접근 가능한 이름으로 바꾸고 dialog 수락을 명시한다. 기존 상세에 없는 시도 정보는 기존 상세 영역에 연결한다.

  캐릭터 계산 테스트 확장자는 `.test.ts`로 바꿔 실제 수집되도록 하고, 기존 테스트와 취소·부분 성공·선택하지 않은 단계의 단정을 유지한다.

- [ ] **Step 4: 대상 검증 통과**
  Run: `pnpm --filter @nextend/frontend test src/features/screens/background/BackgroundPipelineStepper.test.ts src/features/screens/character/CharacterPipelineStepper.test.ts src/domain/job/generation.test.ts src/domain/job/mesh.test.ts`

  Run: `CI=1 pnpm --filter @nextend/frontend test:e2e tests/e2e/shared-job-progress.spec.ts tests/e2e/background-studio-actions.spec.ts tests/e2e/background-studio-e2e.spec.ts tests/e2e/character-studio-e2e.spec.ts tests/e2e/live-result.spec.ts tests/e2e/review-gate.spec.ts`

  Expected: 캐릭터 계산 테스트가 실제 수집되고 전부 통과. 오류·검수·결과·재시도 회귀를 포함한다.

- [ ] **Step 5: 전체 회귀와 지침 갱신**
  `pnpm test`, `pnpm lint`, `pnpm typecheck`, `pnpm build`, `CI=1 pnpm test:e2e`를 실행하고 Task 1의 2D 기준 이미지와 다시 비교한다. 결과가 확인되면 `apps/frontend/AGENTS.md`에 신규·수정 작업의 진행 표시 재사용 규칙을 추가한다. `docs/xHuman/frontend.md`에 세 연결부·기존 집계 함수·검증 파일·신규 작업 연결 방법을 기록한다. 새 지침과 구현은 같은 커밋에 포함한다.

  지침 내용: 진행 상태 UI는 `JobProgressPanel`을 우선 재사용하고, 작업별 단계 판단·집계·요청은 연결부와 기존 화면에 둔다. 헤더·막대·카드·요약·행동을 별도로 복제하지 않는다. 2D 외관 변경에는 사전 목업 승인을 적용한다.

- [ ] **Step 6: 독립 리뷰·수정·커밋**
  새 리뷰어가 설계 준수와 코드 품질을 검토한다. 수정 뒤 영향 범위 재검증·재리뷰, `pnpm docs:check`, `git diff --check`를 실행한다. 이번 파일만 stage하고 `git diff --cached --check`와 훅을 통과시켜 `feat(frontend): reuse job progress in 3d studios`로 커밋한다.

## 최종 검증과 실행 기록

Task 2 뒤 두 작업에 참여하지 않은 리뷰어가 전체 변경을 검토한다. 새 수정이 발생하면 해당 회귀와 리뷰를 다시 실행한다. 마지막 코드 이후 전체 회귀가 통과했으면 이유 없이 반복하지 않는다. 실제 AI 호출·Backend 변경·배포·push는 이번 계획의 검증 완료로 보고하지 않는다.

| 단계 | 현재 기록 |
| --- | --- |
| 설계 승인 | 사용자의 `진행해줘`로 서면 설계 승인 |
| 실행 방식 | 기존 지침의 Subagent-driven 유지 |
| 계획 검토 | 서면 검토 대기 |
| Task 1 / Task 2 | 미실행 |
| 독립 리뷰 / Frontend 회귀 | 미실행 |

실행 중 각 작업의 커밋·명령·수집/통과/건너뜀·시각 비교 아티팩트·리뷰 지적과 수정·환경 제약을 이 표 아래에 짧게 기록한다.
