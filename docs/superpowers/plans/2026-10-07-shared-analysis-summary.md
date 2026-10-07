# 분석 요약 공용 컴포넌트 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 장면 분석 카드와 모델 배지를 작업 종류와 무관한 공용 표시로 만들고 2D 배경 스튜디오에도 같은 모양으로 붙인다.

**Architecture:** `AnalysisSummaryPanel`은 머리줄·선택적 팔레트·항목 격자만 그린다. 작업별 연결부(`ScenePanel`, `SpriteAnalysisPanel`)가 순수 함수로 표시 입력을 만든다. `ModelSummary`는 공용 위치로 옮긴다.

**Tech Stack:** React 19, TypeScript, Tailwind, Vitest(`src/**/*.test.ts`, 순수 함수), Playwright Fake API.

**Spec:** [승인 설계](../specs/2026-10-07-shared-analysis-summary-design.md)

## Global Constraints

- 3D 배경·캐릭터 화면의 현재 모양과 동작 보존. 테스트 ID `scene-panel`·`scene-summary`·`scene-palette`·`scene-swatch`·`scene-swatch-chip`·`scene-camera`·`scene-light`·`scene-scale`·`scene-style`·`model-summary`·`model-text`·`model-image`를 그대로 유지한다.
- 공용 표시 안에 작업 종류 분기·서버 요청·집계를 넣지 않는다.
- `StudioViewTabs`, 서버 계약, 분석 프롬프트, `SpriteInput`은 바꾸지 않는다. 새 UI 라이브러리를 추가하지 않는다.
- 렌더 결과 Tailwind 클래스는 현재 `backgroundStyles`의 `scene*`·`palette`·`swatch*`·`modelSummary`·`modelBadge*` 값과 같아야 한다.
- 2D 카드 값은 서버에 저장된 `sprite` 상태 기준이며 검수 패널의 미저장 편집을 반영하지 않는다.
- 주석은 비자명한 이유만 짧은 한국어 명사구로 쓴다. 검증은 Fake를 사용하고 유료 호출은 실행하지 않는다.
- 코드 변경과 해당 `docs/xHuman/frontend.md`·`apps/frontend/AGENTS.md` 수정은 같은 커밋에 넣는다.

## Review Focus

1. 팔레트가 빈 3D 장면: 팔레트 줄을 그리지 않고 나머지 항목은 그대로 표시 — Task 1 `sceneAnalysisSummary` 빈 팔레트 테스트와 공용 카드의 `palette.length > 0` 조건.
2. 저장 전 계획 편집: 카드 대상 수·프레임 수가 바뀌지 않고 저장 뒤에만 갱신 — Task 2 E2E.
3. 분석 중·취소된 2D 작업: 분석 중에는 모델 배지만, 취소돼도 분석이 끝났으면 카드 표시 — Task 2 E2E(`analyzing`, `canceled` seed).
4. 아이소메트릭 타일 반복 문구: `x`/`y`가 `격자 X축`/`격자 Y축`, 그 밖의 시점은 `가로`/`세로` — Task 2 라벨 테스트.
5. 390px·dark에서 긴 캔버스 문구가 가로 스크롤을 만들지 않음 — Task 2 E2E.

## 실행 방식과 공통 계약

**실행 방식: Subagent-driven.** 저장소 기본 방식을 유지한다. 작업 2개를 순서대로 진행하며 Task 2는 Task 1의 공용 카드 계약을 사용한다. 각 작업 후 구현에 참여하지 않은 리뷰 에이전트가 설계 준수·코드 품질을 검토한다. 현재 `claude/shared-analysis-summary` 브랜치를 사용한다.

Spec과 다른 점: 저장소의 Vitest는 DOM 없이 `src/**/*.test.ts`만 실행하므로 "공용 카드 단위 테스트"는 표시 입력을 만드는 순수 함수 테스트와 E2E 렌더 검증으로 대신한다. 컴포넌트 테스트 환경은 추가하지 않는다.

공용 계약(`apps/frontend/src/features/screens/AnalysisSummaryPanel.tsx`):

```ts
export type AnalysisPaletteEntry = { name: string; hex: string | null }
export type AnalysisField = { id: string; label: string; value: string; testId?: string }
export type AnalysisSummaryTestIds = {
  root?: string
  summary?: string
  palette?: string
  swatch?: string
  swatchChip?: string
}
export type AnalysisSummaryPanelProps = {
  label: string
  summary: string
  palette?: readonly AnalysisPaletteEntry[]
  fields: readonly AnalysisField[]
  testIds?: AnalysisSummaryTestIds
}
export function AnalysisSummaryPanel(props: AnalysisSummaryPanelProps): ReactElement
```

## Task 1: 공용 분석 카드와 3D 연결

**Files:**

- Create: `apps/frontend/src/features/screens/AnalysisSummaryPanel.tsx`
- Create: `apps/frontend/src/features/screens/ModelSummary.tsx` (기존 파일 이동, `git mv`)
- Delete: `apps/frontend/src/features/screens/background/ModelSummary.tsx`
- Modify: `apps/frontend/src/features/screens/background/ScenePanel.tsx`
- Modify: `apps/frontend/src/features/screens/background/backgroundStyles.ts` — 옮긴 키 삭제(다른 사용처가 없을 때만)
- Modify: `apps/frontend/src/features/screens/background/BackgroundStudioScreen.tsx`, `apps/frontend/src/features/screens/character/CharacterStudioScreen.tsx` — `ModelSummary` import 경로만
- Test: `apps/frontend/src/features/screens/background/ScenePanel.test.ts`

**Interfaces:**

- Produces: 위 공용 계약, `ModelSummary`(`@/features/screens/ModelSummary`, props `{ models, providers }` 불변), `sceneAnalysisSummary(scene: SceneSpec): AnalysisSummaryPanelProps`(`ScenePanel.tsx`에서 export)

- [x] **Step 1: 실패하는 테스트 작성** — `ScenePanel.test.ts`

```ts
import { describe, expect, it } from 'vitest'
import { sceneAnalysisSummary } from './ScenePanel'
import type { SceneSpec } from '@/domain/job/types'

const scene: SceneSpec = {
  palette: [
    { name: '청록색 심연의 물', hex: '#1BB5C4' },
    { name: '이끼 낀 황록색 식생', hex: null },
  ],
  timeOfDay: '맑은 낮',
  mood: '고요한 탐험 공간',
  renderingStyle: '스타일라이즈드 3D',
  materialFeel: '손으로 칠한 듯한 질감',
  camera: { type: 'isometric', eyeLevel: '지면에서 약 12m', horizonY: 0.375 },
  light: { direction: '좌측 전방 35° 방위', temperature: '따뜻한 주광', shadowHardness: '중간' },
  scale: { object: '청색 발광 단말기', realWorldSize: '높이 1.8m', heightMeters: 1.8 },
}

describe('sceneAnalysisSummary', () => {
  it('keeps the current scene wording and test ids', () => {
    const props = sceneAnalysisSummary(scene)
    expect(props.label).toBe('장면')
    expect(props.summary).toBe('맑은 낮 · 고요한 탐험 공간')
    expect(props.palette).toEqual(scene.palette)
    expect(props.fields.map((f) => [f.label, f.testId])).toEqual([
      ['시점', 'scene-camera'],
      ['광원', 'scene-light'],
      ['스케일 기준', 'scene-scale'],
      ['렌더링', 'scene-style'],
    ])
    expect(props.fields[0]!.value).toBe('isometric · 지면에서 약 12m · 수평선 0.38')
    expect(props.fields[1]!.value).toBe('좌측 전방 35° 방위 · 따뜻한 주광 · 중간')
    expect(props.fields[3]!.value).toBe('스타일라이즈드 3D · 손으로 칠한 듯한 질감')
    expect(props.testIds).toEqual({
      root: 'scene-panel',
      summary: 'scene-summary',
      palette: 'scene-palette',
      swatch: 'scene-swatch',
      swatchChip: 'scene-swatch-chip',
    })
  })

  it('passes an empty palette through so the shared card can omit the row', () => {
    expect(sceneAnalysisSummary({ ...scene, palette: [] }).palette).toEqual([])
  })
})
```

스케일 값은 `formatScaleReference(scene.scale)` 결과와 같아야 한다: `expect(props.fields[2]!.value).toBe(formatScaleReference(scene.scale))`를 첫 테스트에 추가한다(`@/domain/job/sceneScale`에서 import).

- [x] **Step 2: 실패 확인**

Run: `pnpm --dir apps/frontend exec vitest run src/features/screens/background/ScenePanel.test.ts`
Expected: FAIL — `sceneAnalysisSummary` export 없음

- [x] **Step 3: 공용 카드 작성** — `AnalysisSummaryPanel.tsx`. 위 공용 계약의 타입을 export하고 현재 `ScenePanel` 마크업을 옮긴다. 클래스 문자열은 `backgroundStyles`의 현재 값을 그대로 복사한다.

```tsx
/**
 * 분석 결과 요약 카드 — 3D 장면·2D 분석 공용 표시
 *
 * 작업별 연결부가 표시 입력을 만든다. 여기서는 작업 종류를 판단하지 않는다.
 */
import type { ReactElement } from 'react'

// (공용 계약 타입 5개 export — 위 블록과 동일)

const styles = {
  panel: 'rounded-xl border border-border bg-card px-[18px] py-4',
  header: 'mb-3 flex items-baseline gap-2.5',
  label: 'text-[0.8125rem] font-semibold text-muted-foreground',
  summary: 'text-[0.9375rem] font-semibold text-foreground',
  palette: 'mb-3.5 flex flex-wrap gap-x-3 gap-y-1.5',
  swatch: 'inline-flex items-center gap-1.5 text-xs text-muted-foreground',
  // 밝은 색도 경계가 보이도록 border 유지
  swatchChip: 'size-3.5 rounded border border-border',
  // 색상 미확정 — 투명 칩은 빈 칩으로 읽힘
  swatchChipUnresolved: 'size-3.5 rounded border border-dashed border-border bg-[var(--sunken-bg)]',
  grid: 'grid grid-cols-[repeat(auto-fit,minmax(260px,1fr))] gap-x-5 gap-y-2.5',
  field: 'min-w-0',
  fieldLabel: 'mb-0.5 text-xs text-muted-foreground',
  fieldValue: 'text-[0.8125rem] leading-normal text-foreground',
}

export function AnalysisSummaryPanel({
  label,
  summary,
  palette = [],
  fields,
  testIds = {},
}: AnalysisSummaryPanelProps): ReactElement {
  return (
    <section className={styles.panel} data-testid={testIds.root}>
      <div className={styles.header}>
        <span className={styles.label}>{label}</span>
        <span className={styles.summary} data-testid={testIds.summary}>
          {summary}
        </span>
      </div>

      {palette.length > 0 ? (
        <div className={styles.palette} data-testid={testIds.palette}>
          {palette.map((entry, index) => (
            <span
              key={`${entry.name}:${entry.hex ?? 'unresolved'}:${index}`}
              className={styles.swatch}
              title={entry.hex ? `${entry.name} ${entry.hex}` : `${entry.name} · 색상 미확정`}
              data-testid={testIds.swatch}
            >
              {/* 이름이 옆에 있으므로 칩은 보조 장식 */}
              <span
                className={entry.hex ? styles.swatchChip : styles.swatchChipUnresolved}
                style={entry.hex ? { backgroundColor: entry.hex } : undefined}
                data-resolved={entry.hex !== null}
                data-testid={testIds.swatchChip}
                aria-hidden="true"
              />
              {entry.name}
            </span>
          ))}
        </div>
      ) : null}

      <dl className={styles.grid}>
        {fields.map((field) => (
          <div key={field.id} className={styles.field}>
            <dt className={styles.fieldLabel}>{field.label}</dt>
            <dd className={styles.fieldValue} data-testid={field.testId}>
              {field.value}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  )
}
```

- [x] **Step 4: `ScenePanel` 축소** — 상단 설계 주석(팔레트·camera/light/scale 이유)은 유지하고 본문을 다음으로 교체한다.

```tsx
import { AnalysisSummaryPanel, type AnalysisSummaryPanelProps } from '../AnalysisSummaryPanel'
import { formatScaleReference } from '@/domain/job/sceneScale'
import type { SceneSpec } from '@/domain/job/types'

export interface ScenePanelProps {
  scene: SceneSpec
}

export function sceneAnalysisSummary(scene: SceneSpec): AnalysisSummaryPanelProps {
  const { camera, light } = scene
  return {
    label: '장면',
    summary: `${scene.timeOfDay} · ${scene.mood}`,
    palette: scene.palette,
    fields: [
      {
        id: 'camera',
        label: '시점',
        value: `${camera.type} · ${camera.eyeLevel} · 수평선 ${camera.horizonY.toFixed(2)}`,
        testId: 'scene-camera',
      },
      {
        id: 'light',
        label: '광원',
        value: `${light.direction} · ${light.temperature} · ${light.shadowHardness}`,
        testId: 'scene-light',
      },
      {
        id: 'scale',
        label: '스케일 기준',
        value: formatScaleReference(scene.scale),
        testId: 'scene-scale',
      },
      {
        id: 'style',
        label: '렌더링',
        value: `${scene.renderingStyle} · ${scene.materialFeel}`,
        testId: 'scene-style',
      },
    ],
    testIds: {
      root: 'scene-panel',
      summary: 'scene-summary',
      palette: 'scene-palette',
      swatch: 'scene-swatch',
      swatchChip: 'scene-swatch-chip',
    },
  }
}

export function ScenePanel({ scene }: ScenePanelProps) {
  return <AnalysisSummaryPanel {...sceneAnalysisSummary(scene)} />
}
```

- [x] **Step 5: `ModelSummary` 이동** — `git mv apps/frontend/src/features/screens/background/ModelSummary.tsx apps/frontend/src/features/screens/ModelSummary.tsx`. 파일 안의 `backgroundStyles` import를 지역 `styles` 상수로 바꾼다(값은 현재 `modelSummary`·`modelBadge`·`modelBadgeLabel`·`modelBadgeProvider`·`modelBadgeModel`과 동일, 기존 주석 유지). 두 3D 화면의 import를 `@/features/screens/ModelSummary`로 바꾼다.

- [x] **Step 6: 스타일 키 정리** — `rg -n "styles\.(scene|sceneHeader|sceneLabel|sceneSummary|palette|swatch|swatchChip|swatchChipUnresolved|sceneGrid|sceneField|sceneFieldLabel|sceneFieldValue|modelSummary|modelBadge\w*)\b" apps/frontend/src`로 다른 사용처가 없음을 확인하고 `backgroundStyles.ts`에서 해당 키와 그 주석만 삭제한다. 사용처가 남은 키는 두고 보고서에 적는다.

- [x] **Step 7: 대상 검증**

Run: `pnpm --dir apps/frontend exec vitest run src/features/screens/background/ScenePanel.test.ts` → PASS
Run: `pnpm typecheck && pnpm lint` (저장소 루트) → 오류 0
Run: `pnpm --dir apps/frontend exec playwright test tests/e2e/scene-palette.spec.ts tests/e2e/background-studio-e2e.spec.ts tests/e2e/background-studio-actions.spec.ts tests/e2e/part-generation-e2e.spec.ts tests/e2e/scene-assembly.spec.ts` → 전부 통과(테스트 수정 없이)

- [x] **Step 8: 커밋** — 이 작업 파일만 stage한다. `docs/xHuman/frontend.md`에 `ModelSummary`·`ScenePanel` 경로 언급이 없으면 문서는 Task 2에서 함께 고친다.

```bash
git commit -m "refactor(frontend): share analysis summary card and model badges"
```

## Task 2: 2D 분석 카드 연결과 지침

**Files:**

- Create: `apps/frontend/src/domain/sprites/labels.ts`
- Create: `apps/frontend/src/domain/sprites/labels.test.ts`
- Create: `apps/frontend/src/features/screens/sprites/SpriteAnalysisPanel.tsx`
- Create: `apps/frontend/src/features/screens/sprites/SpriteAnalysisPanel.test.ts`
- Modify: `apps/frontend/src/features/screens/sprites/SpriteStudioScreen.tsx`
- Modify: `apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx` — 두 번째 안내 문장
- Modify: `apps/frontend/tests/e2e/sprites-static.spec.ts`
- Modify: `apps/frontend/AGENTS.md`, `docs/xHuman/frontend.md`

**Interfaces:**

- Consumes: Task 1의 `AnalysisSummaryPanel`·`AnalysisSummaryPanelProps`(`@/features/screens/AnalysisSummaryPanel`), `ModelSummary`(`@/features/screens/ModelSummary`)
- Produces: `spriteViewLabel(view: SpriteView): string`, `spriteRepeatLabel(repeat: SpriteRepeat, view: SpriteView): string`, `spriteOutputLabel(settings: SpriteSettings): string`, `spriteAnalysisSummary(sprite: SpriteState): AnalysisSummaryPanelProps`, `SpriteAnalysisPanel({ sprite })`

- [x] **Step 1: 실패하는 단위 테스트 작성**

`labels.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { spriteOutputLabel, spriteRepeatLabel, spriteViewLabel } from './labels'

describe('sprite labels', () => {
  it('matches the input screen wording', () => {
    expect(spriteViewLabel('sideView')).toBe('횡스크롤')
    expect(spriteViewLabel('topDown')).toBe('탑다운')
    expect(spriteViewLabel('isometric')).toBe('아이소메트릭')
  })

  it('names repeat axes by view', () => {
    expect(spriteRepeatLabel('x', 'isometric')).toBe('격자 X축')
    expect(spriteRepeatLabel('y', 'isometric')).toBe('격자 Y축')
    expect(spriteRepeatLabel('x', 'topDown')).toBe('가로')
    expect(spriteRepeatLabel('y', 'sideView')).toBe('세로')
    expect(spriteRepeatLabel('both', 'isometric')).toBe('양쪽')
  })

  it('describes the output kind', () => {
    expect(
      spriteOutputLabel({ view: 'sideView', outputKind: 'layers', tileWidth: 128, repeat: 'both' }),
    ).toBe('배경 레이어')
    expect(
      spriteOutputLabel({ view: 'isometric', outputKind: 'tiles', tileWidth: 64, repeat: 'x' }),
    ).toBe('반복 타일 · 64px · 격자 X축')
  })
})
```

`SpriteAnalysisPanel.test.ts` — `SpriteState` 고정값은 `src/domain/sprites/rules.test.ts`의 `spriteFixture()` 모양을 참고해 이 파일 안에 최소로 만든다(자산 2개: 정적 1개, `loop: true`·`frameCount: 4` 1개).

```ts
import { describe, expect, it } from 'vitest'
import { spriteAnalysisSummary } from './SpriteAnalysisPanel'
import type { SpriteAssetPlan, SpriteState } from '@/domain/sprites/types'

function plan(id: string, loop: boolean, frameCount = 8): SpriteAssetPlan {
  return {
    id,
    name: id,
    order: id === 'back' ? 0 : 1,
    sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
    requiresTransparency: id !== 'back',
    loop,
    frameCount,
    fps: 8,
    motionNotes: '',
  }
}

function sprite(overrides: Partial<SpriteState> = {}): SpriteState {
  return {
    settings: { view: 'sideView', outputKind: 'layers', tileWidth: 128, repeat: 'both' },
    sourceCanvas: { width: 1920, height: 1080 },
    generationCanvas: { width: 1536, height: 1024 },
    outputCanvas: { width: 1920, height: 1080 },
    transform: { scale: 1, offsetX: 0, offsetY: 0 },
    phase: 'planReview',
    reviewRevision: 1,
    completedExportId: null,
    assets: [plan('back', false), plan('water', true, 4)].map((p) => ({
      id: p.id,
      plan: p,
      planRevision: 1,
      anchor: { x: 0, y: 0 },
      approvedBaseImageId: null,
      approval: null,
      frames: [{ index: 0, currentTaskId: null, currentImageId: null }],
    })),
    images: [],
    exports: [],
    ...overrides,
  }
}

describe('spriteAnalysisSummary', () => {
  it('summarizes layers from the saved plan', () => {
    const props = spriteAnalysisSummary(sprite())
    expect(props.label).toBe('분석')
    expect(props.summary).toBe('횡스크롤 · 레이어 2개')
    expect(props.palette).toBeUndefined()
    expect(props.fields.map((f) => [f.label, f.value, f.testId])).toEqual([
      ['시점', '횡스크롤', 'sprite-analysis-view'],
      ['출력', '배경 레이어', 'sprite-analysis-output'],
      ['캔버스', '원본 1920×1080 → 요청 1536×1024 → 최종 1920×1080', 'sprite-analysis-canvas'],
      ['대상', '2개 · 총 5프레임', 'sprite-analysis-assets'],
    ])
    expect(props.testIds).toEqual({ root: 'sprite-analysis-panel', summary: 'sprite-analysis-summary' })
  })

  it('names tiles and keeps an empty plan visible', () => {
    const props = spriteAnalysisSummary(
      sprite({
        settings: { view: 'isometric', outputKind: 'tiles', tileWidth: 128, repeat: 'y' },
        assets: [],
      }),
    )
    expect(props.summary).toBe('아이소메트릭 · 타일 0개')
    expect(props.fields[1]!.value).toBe('반복 타일 · 128px · 격자 Y축')
    expect(props.fields[3]!.value).toBe('0개 · 총 0프레임')
  })
})
```

- [x] **Step 2: 실패 확인**

Run: `pnpm --dir apps/frontend exec vitest run src/domain/sprites/labels.test.ts src/features/screens/sprites/SpriteAnalysisPanel.test.ts`
Expected: FAIL — 모듈 없음

- [x] **Step 3: 라벨 함수 작성** — `labels.ts`

```ts
import type { SpriteRepeat, SpriteSettings, SpriteView } from './types'

const VIEW_LABELS: Record<SpriteView, string> = {
  sideView: '횡스크롤',
  topDown: '탑다운',
  isometric: '아이소메트릭',
}

export function spriteViewLabel(view: SpriteView): string {
  return VIEW_LABELS[view]
}

// 아이소메트릭 반복은 화면 축이 아니라 다이아몬드 격자 축
export function spriteRepeatLabel(repeat: SpriteRepeat, view: SpriteView): string {
  if (repeat === 'both') return '양쪽'
  if (view === 'isometric') return repeat === 'x' ? '격자 X축' : '격자 Y축'
  return repeat === 'x' ? '가로' : '세로'
}

export function spriteOutputLabel(settings: SpriteSettings): string {
  if (settings.outputKind === 'layers') return '배경 레이어'
  return `반복 타일 · ${settings.tileWidth}px · ${spriteRepeatLabel(settings.repeat, settings.view)}`
}
```

- [x] **Step 4: 2D 연결부 작성** — `SpriteAnalysisPanel.tsx`

```tsx
import { AnalysisSummaryPanel, type AnalysisSummaryPanelProps } from '../AnalysisSummaryPanel'
import { spriteOutputLabel, spriteViewLabel } from '@/domain/sprites/labels'
import type { SpriteCanvas, SpriteState } from '@/domain/sprites/types'

const size = (canvas: SpriteCanvas) => `${canvas.width}×${canvas.height}`

// 서버 저장 계획 기준 — 미저장 편집은 검수 패널에만 반영
export function spriteAnalysisSummary(sprite: SpriteState): AnalysisSummaryPanelProps {
  const { settings } = sprite
  const plans = sprite.assets.map((asset) => asset.plan)
  const frames = plans.reduce((sum, plan) => sum + (plan.loop ? plan.frameCount : 1), 0)
  const view = spriteViewLabel(settings.view)
  const unit = settings.outputKind === 'layers' ? '레이어' : '타일'
  return {
    label: '분석',
    summary: `${view} · ${unit} ${plans.length}개`,
    fields: [
      { id: 'view', label: '시점', value: view, testId: 'sprite-analysis-view' },
      {
        id: 'output',
        label: '출력',
        value: spriteOutputLabel(settings),
        testId: 'sprite-analysis-output',
      },
      {
        id: 'canvas',
        label: '캔버스',
        value: `원본 ${size(sprite.sourceCanvas)} → 요청 ${size(sprite.generationCanvas)} → 최종 ${size(sprite.outputCanvas)}`,
        testId: 'sprite-analysis-canvas',
      },
      {
        id: 'assets',
        label: '대상',
        value: `${plans.length}개 · 총 ${frames}프레임`,
        testId: 'sprite-analysis-assets',
      },
    ],
    testIds: { root: 'sprite-analysis-panel', summary: 'sprite-analysis-summary' },
  }
}

export function SpriteAnalysisPanel({ sprite }: { sprite: SpriteState }) {
  return <AnalysisSummaryPanel {...spriteAnalysisSummary(sprite)} />
}
```

- [x] **Step 5: 단위 테스트 통과 확인**

Run: `pnpm --dir apps/frontend exec vitest run src/domain/sprites/labels.test.ts src/features/screens/sprites/SpriteAnalysisPanel.test.ts` → PASS

- [x] **Step 6: 실패하는 E2E 작성** — `sprites-static.spec.ts` 끝에 추가. 이미 있는 `start`·`installFakeApi`·`SPRITE_IDS`를 쓴다. Fake 기본 계획은 정적 대상 2개(`후경 지면`, `전경 나무`), 원본 320×180, 요청 1024×1024, 레이어 최종 320×180, 타일 128px·`both`다.

```ts
test('sprite analysis card shows models and follows the saved plan', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'analyzing' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByTestId('model-text')).toContainText('analysis')
  await expect(page.getByTestId('model-image')).toContainText('sprite-image')
  await expect(page.getByTestId('sprite-analysis-panel')).toHaveCount(0)
})

test('sprite analysis card reflects saved plan edits only', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await start(page)
  const card = page.getByTestId('sprite-analysis-panel')
  await expect(card.getByTestId('sprite-analysis-summary')).toHaveText('횡스크롤 · 레이어 2개')
  await expect(card.getByTestId('sprite-analysis-output')).toHaveText('배경 레이어')
  await expect(card.getByTestId('sprite-analysis-canvas')).toHaveText(
    '원본 320×180 → 요청 1024×1024 → 최종 320×180',
  )
  await expect(card.getByTestId('sprite-analysis-assets')).toHaveText('2개 · 총 2프레임')
  await expect(page.getByText('모델 요청', { exact: false })).toHaveCount(0)

  await page.getByLabel('루프 애니메이션 1', { exact: true }).check()
  await expect(card.getByTestId('sprite-analysis-assets')).toHaveText('2개 · 총 2프레임')
  await page.getByRole('button', { name: '계획 저장', exact: true }).click()
  await expect(card.getByTestId('sprite-analysis-assets')).toHaveText('2개 · 총 9프레임')

  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(card).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
})

test('sprite analysis card names isometric tiles', async ({ page }) => {
  await installFakeApi(page, { sprites: {} })
  await start(page, 'isometric', 'tiles')
  const card = page.getByTestId('sprite-analysis-panel')
  await expect(card.getByTestId('sprite-analysis-summary')).toHaveText('아이소메트릭 · 타일 2개')
  await expect(card.getByTestId('sprite-analysis-output')).toHaveText('반복 타일 · 128px · 양쪽')
})

test('sprite analysis card stays on a canceled job after analysis', async ({ page }) => {
  // canceled seed — 기준 이미지 생성 뒤 취소된 작업
  await installFakeApi(page, { sprites: { seed: 'canceled' } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  await expect(page.getByTestId('sprite-analysis-panel')).toBeVisible()
  await expect(page.getByTestId('model-summary')).toBeVisible()
})
```

- [x] **Step 7: E2E 실패 확인**

Run: `pnpm --dir apps/frontend exec playwright test tests/e2e/sprites-static.spec.ts -g "sprite analysis card"`
Expected: FAIL — `sprite-analysis-panel`·`model-text` 없음

- [x] **Step 8: 화면 연결** — `SpriteStudioScreen.tsx`

- import 추가: `import { useProviders } from '@/app/queries/useProviders'`, `import { ModelSummary } from '../ModelSummary'`, `import { SpriteAnalysisPanel } from './SpriteAnalysisPanel'`
- 컴포넌트 첫 부분 훅 목록에 `const { providers } = useProviders()` 추가
- 진행 패널 `</section>` 바로 뒤, `SpritePlanReview` 앞에 추가:

```tsx
          {/* 모델은 접수 시점 고정 — 분석 중에도 표시 */}
          <ModelSummary models={job.models} providers={providers} />
          {sprite.phase !== 'analyzing' ? <SpriteAnalysisPanel sprite={sprite} /> : null}
```

`SpritePlanReview.tsx`의 두 번째 `<p className={styles.hint}>`(원본 → 모델 요청 → 최종 문장)를 다음으로 교체한다.

```tsx
      <p className={styles.hint}>순서는 뒤에서 앞으로 증가</p>
```

`sprite.sourceCanvas` 등 더 이상 쓰지 않는 값이 생기면 lint 경고 없이 정리한다.

- [x] **Step 9: 지침·문서 갱신**

`apps/frontend/AGENTS.md`의 `## 작업 진행 표시` 절 끝에 추가:

```markdown
- 분석 결과 요약은 [AnalysisSummaryPanel](src/features/screens/AnalysisSummaryPanel.tsx), 모델 표시는 [ModelSummary](src/features/screens/ModelSummary.tsx)를 우선 재사용한다. 작업별 값은 연결부의 순수 함수(`sceneAnalysisSummary`, `spriteAnalysisSummary`)가 만들며 공용 표시 안에 작업 종류 분기를 넣지 않는다.
```

`docs/xHuman/frontend.md`의 `JobProgressPanel` 항목 뒤에 현재 사실로 추가:

```markdown
- `src/features/screens/AnalysisSummaryPanel.tsx`는 분석 카드의 머리줄·선택적 팔레트·항목 격자를 그린다. 연결자는 `background/ScenePanel.tsx`(`sceneAnalysisSummary`, 3D 배경·캐릭터 `RunResult`)와 `sprites/SpriteAnalysisPanel.tsx`(`spriteAnalysisSummary`, 서버 저장 계획 기준)다. `src/features/screens/ModelSummary.tsx`는 3D 두 화면과 2D 화면의 진행 패널 아래에서 텍스트·이미지 모델 배지를 표시한다. 2D 카드는 분석이 끝난 뒤부터 표시하고, 시점·출력·반복 문구는 `src/domain/sprites/labels.ts`를 쓴다.
```

- [x] **Step 10: 대상·전체 검증**

Run: `pnpm --dir apps/frontend exec playwright test tests/e2e/sprites-static.spec.ts tests/e2e/sprites-animation.spec.ts` → 전부 통과
Run(저장소 루트): `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e` → 전부 통과
Run: `pnpm docs:check` → 통과

- [x] **Step 11: 커밋** — 이 작업 파일만 stage한다.

```bash
git commit -m "feat(frontend): show shared analysis summary in 2d studio"
```

## 실행 기록

- 실행 방식: Subagent-driven. 작업별 구현 에이전트 1개와 구현에 참여하지 않은 리뷰 에이전트 1개, 마지막에 브랜치 전체 리뷰 1회와 수정 범위 재리뷰 1회.
- Task 1 완료: `4f61915` — `AnalysisSummaryPanel`·`sceneAnalysisSummary`, `ModelSummary` 공용 위치 이동, `backgroundStyles`의 옮긴 키 삭제. 리뷰 승인, 기존 3D E2E 5개 수정 없이 통과.
- Task 2 완료: `327d5fb` — `labels.ts`·`SpriteAnalysisPanel`, 2D 화면 배치, `SpritePlanReview` 캔버스 문장 이동, E2E 4개, 지침·도메인 문서. 리뷰 승인.
- 전체 리뷰 후 수정: `4617b7c` — 2D 세로 스택에서 `ModelSummary` 아래 여백 중복 제거(`className` 병합, 3D 클래스 불변), 공용 카드 머리 주석 명사구화. 재리뷰 통과.
- 검증(최종 HEAD): `pnpm test` 437 통과, `pnpm lint`·`pnpm typecheck`·`pnpm build` 통과, `pnpm test:e2e` 321 통과, `pnpm docs:check` 통과.
- 계획과 다른 점: 공용 카드 단위 테스트는 순수 함수 테스트와 E2E로 대체. 빈 팔레트면 3D도 팔레트 줄을 그리지 않음(설계 명시).
- 보류: 카드 `section`의 접근성 이름(3D 접근성 트리 변경 우려), `SpriteInput` 선택지 문구와 `labels.ts` 중복(이번 범위에서 `SpriteInput` 불변), 대상 추가·삭제 후 카드 갱신 E2E.
