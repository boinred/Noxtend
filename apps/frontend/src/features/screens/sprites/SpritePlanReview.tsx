import { useState } from 'react'
import { useSpriteRoiEditor } from './useSpriteRoiEditor'
import { useTileFill } from './useTileFill'
import { SpriteCompareDivider } from './SpriteCompareDivider'
import { SpriteReconstructionLayer } from './SpriteReconstructionLayer'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { JobTask } from '@/domain/job/types'
import { Button } from '@/components/ui/button'
import { backgroundStyles } from '../background/backgroundStyles'
import { PartsOverlay } from '../background/PartsOverlay'
import type { OverlayPart } from '../background/PartsOverlay'
import { useUpdateSpritePlan, useApproveSpritePlan } from '@/app/queries/useSprites'
import { useModelPrices } from '@/app/queries/useTuning'
import { apiErrorMessage, apiErrorStatus } from '@/app/queries/errors'
import { assetRole, assetOutputSize, cellWidthOf } from '@/domain/sprites/rules'
import { spriteRoleLabel } from '@/domain/sprites/labels'
import {
  addPlacement,
  MAX_PLACEMENTS,
  moveGenerationBox,
  placementsOf,
  reconstructionCell,
  reconstructionVisible,
} from '@/domain/sprites/reconstruction'
import type { Bounds } from '@/domain/job/types'
import type { SpriteGrid } from '@/domain/sprites/types'
import type { SpriteState, SpriteAssetPlan } from '@/domain/sprites/types'
import { spriteStyles as styles } from './spriteStyles'

// 오브젝트 자리 상자 id 접미사, 생성 기준 상자는 plan.id
const PLACEMENTS = ':placements'
const PRESSED =
  'aria-pressed:border-primary aria-pressed:bg-primary/12 aria-pressed:text-foreground'
const canonical = ({ x, y, w, h }: Bounds) => ({ x, y, w, h })
const inside = ({ x, y, w, h }: Bounds) =>
  [x, y, w, h].every(Number.isFinite) &&
  x >= 0 &&
  y >= 0 &&
  w > 0 &&
  h > 0 &&
  x + w <= 1 &&
  y + h <= 1

export function SpritePlanReview({
  jobId,
  sourceImageId,
  sprite,
  model,
  disabled,
  tasks,
}: {
  jobId: string
  sourceImageId: string
  sprite: SpriteState
  model: string
  disabled: boolean
  tasks: JobTask[]
}) {
  const planning = sprite.phase === 'planReview'
  const saved = sprite.assets.map((asset) => asset.plan)
  const tiles = sprite.settings.outputKind === 'tiles'
  function projection(plans: SpriteAssetPlan[], grid: SpriteGrid | null) {
    return JSON.stringify({
      plans: [...plans]
        .sort((a, b) => a.id.localeCompare(b.id))
        .map((p) => ({
          id: p.id,
          role: assetRole(sprite.settings, p),
          name: p.name,
          order: p.order,
          sourceBounds: canonical(p.sourceBounds),
          placements: p.placements === null ? null : p.placements.map(canonical),
          requiresTransparency: p.requiresTransparency,
          loop: p.loop,
          frameCount: p.frameCount,
          fps: p.fps,
          motionNotes: p.motionNotes,
        })),
      grid: grid === null ? null : { cellWidth: grid.cellWidth },
    })
  }
  function serverDraft() {
    return {
      revision: sprite.reviewRevision,
      plans: saved,
      grid: sprite.grid,
      saved: projection(saved, sprite.grid),
    }
  }
  const [draft, setDraft] = useState(serverDraft)
  const plans = draft.plans
  function setPlans(next: SpriteAssetPlan[] | ((current: SpriteAssetPlan[]) => SpriteAssetPlan[])) {
    setDraft((current) => ({
      ...current,
      plans: typeof next === 'function' ? next(current.plans) : next,
    }))
  }
  const update = useUpdateSpritePlan()
  const approve = useApproveSpritePlan()
  const { prices } = useModelPrices()
  const price = prices
    .filter((p) => p.model === model && p.effectiveFrom <= new Date().toISOString())
    .sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0]?.perImage
  const count = plans.reduce((sum, plan) => sum + (plan.loop ? plan.frameCount : 1), 0)
  const current = projection(plans, draft.grid)
  const dirty = current !== draft.saved
  const stale = draft.revision !== sprite.reviewRevision
  // 미저장 draft 보존, 저장 반영 또는 변경 없는 draft만 동기화
  if (stale && (!dirty || current === projection(saved, sprite.grid))) setDraft(serverDraft())
  const backmost = Math.min(...plans.map((p) => p.order))
  const cell = cellWidthOf(draft.grid, plans, sprite.settings)
  const outputs = plans.map((plan) =>
    assetOutputSize(sprite.settings, sprite.sourceCanvas, cell, plan),
  )
  const roleErrors = plans.map((plan, index) => {
    const role = assetRole(sprite.settings, plan)
    const output = outputs[index]!
    if ('error' in output) return output.error
    if (
      role === 'backdrop' &&
      (plans.filter((p) => assetRole(sprite.settings, p) === 'backdrop').length > 1 ||
        plan.order !== backmost)
    )
      return '원경 배경은 최대 하나이며 가장 뒤에 있어야 합니다'
    return null
  })
  const gridInvalid =
    draft.grid !== null &&
    (!Number.isFinite(draft.grid.cellWidth) ||
      draft.grid.cellWidth <= 0 ||
      draft.grid.cellWidth > 1)
  const invalid =
    roleErrors.some(Boolean) ||
    gridInvalid ||
    plans.length < 1 ||
    plans.length > 12 ||
    count > 64 ||
    new Set(plans.map((p) => p.order)).size !== plans.length ||
    plans.some((p) => {
      const transparent =
        assetRole(sprite.settings, p) === 'object' ||
        (assetRole(sprite.settings, p) === 'layer' && p.order !== backmost) ||
        (assetRole(sprite.settings, p) === 'tile' && sprite.settings.view === 'isometric')
      return (
        !p.name.trim() ||
        !Number.isInteger(p.fps) ||
        p.fps < 1 ||
        p.fps > 30 ||
        (p.loop && ![4, 8].includes(p.frameCount)) ||
        p.motionNotes.length > 500 ||
        !Number.isSafeInteger(p.order) ||
        !inside(p.sourceBounds) ||
        (p.placements !== null &&
          (p.placements.length < 1 ||
            p.placements.length > MAX_PLACEMENTS ||
            !p.placements.every(inside))) ||
        (transparent && !p.requiresTransparency) ||
        (assetRole(sprite.settings, p) === 'backdrop' && p.requiresTransparency)
      )
    })
  const busy = disabled || update.isPending || approve.isPending
  function context() {
    return { jobId, requestId: crypto.randomUUID(), expectedRevision: draft.revision }
  }
  function edit(
    id: string,
    patch: Partial<SpriteAssetPlan> | ((plan: SpriteAssetPlan) => SpriteAssetPlan),
  ) {
    setPlans((current) =>
      current.map((p) =>
        p.id !== id ? p : typeof patch === 'function' ? patch(p) : { ...p, ...patch },
      ),
    )
  }
  // 생성 기준 상자는 재생성 입력, 자리 상자는 배치만
  function withBox(plan: SpriteAssetPlan, partId: string, ordinal: number, bounds: Bounds) {
    if (partId === plan.id) return moveGenerationBox(plan, bounds, sprite.settings)
    if (partId !== plan.id + PLACEMENTS) return plan
    return { ...plan, placements: placementsOf(plan).map((b, i) => (i === ordinal ? bounds : b)) }
  }
  function overlayParts(source: SpriteAssetPlan[]): OverlayPart[] {
    return source.flatMap((plan, index) => {
      const role = assetRole(sprite.settings, plan)
      const generation: OverlayPart = {
        id: plan.id,
        name: plan.name,
        label: `${index + 1}. ${plan.name}${tiles ? ` · ${spriteRoleLabel(role)}` : ''}`,
        placements: [plan.sourceBounds],
        boxKind: 'source',
      }
      if (role !== 'object') return [generation]
      return [
        generation,
        {
          id: plan.id + PLACEMENTS,
          name: plan.name,
          label: `${index + 1}. ${plan.name} · 자리`,
          placements: placementsOf(plan),
          boxKind: 'placement',
        },
      ]
    })
  }
  function isActive(id: string) {
    return (
      sprite.assets
        .find((asset) => asset.id === id)
        ?.frames.some((frame) =>
          tasks.some(
            (task) =>
              task.id === frame.currentTaskId &&
              (task.status === 'running' || task.status === 'pending'),
          ),
        ) ?? false
    )
  }
  const reconstructing = reconstructionVisible(sprite)
  const tileCell = reconstructionCell(sprite.settings, sprite.sourceCanvas, cell)
  const tileFill = useTileFill({
    jobId,
    sourceImageId,
    sprite,
    plans,
    cell: tileCell,
    enabled: reconstructing,
  })
  // 모드·경계는 화면 상태, 재계산 중에도 마지막 선택 유지
  const [mode, setMode] = useState<'compare' | 'edit'>('compare')
  const [divider, setDivider] = useState(50)
  const ready = reconstructing && tileFill.status === 'ready'
  const comparing = ready && mode === 'compare'
  const roi = useSpriteRoiEditor({
    plans,
    parts: overlayParts(plans),
    lockedIds: plans
      .filter((plan) => isActive(plan.id))
      .flatMap((plan) => [plan.id, plan.id + PLACEMENTS])
      .join(','),
    // 비교로 바뀌면 진행 중인 끌기 취소
    disabled: busy || comparing,
    revision: `${draft.revision}:${sprite.reviewRevision}`,
    onCommit: (partId, ordinal, bounds) =>
      setPlans((current) => current.map((plan) => withBox(plan, partId, ordinal, bounds))),
  })
  // 카드에서 고른 상자는 이미지에서 보여야 함
  function selectBox(partId: string, ordinal: number) {
    setMode('edit')
    roi.select(partId, ordinal)
  }
  const preview = roi.preview
  const displayPlans = preview
    ? plans.map((plan) => withBox(plan, preview.partId, preview.ordinal, preview.bounds))
    : plans
  const [failedImages, setFailedImages] = useState<string[]>([])
  const imagesFailed = sprite.assets.some((asset) =>
    asset.frames.some(
      (frame) => frame.index === 0 && failedImages.includes(frame.currentImageId ?? ''),
    ),
  )
  return (
    <section className={styles.panel} aria-label="제작 계획 검수">
      <h2 className={styles.title}>제작 계획 검수</h2>
      <p className={styles.hint}>
        {plans.length}/12개 · 정적 대상 1프레임 · 총 {count}/64장 · 모델 {model} ·{' '}
        {price == null ? '비용 미확인' : `예상 이미지 비용 $${(price * count).toFixed(2)}`}
      </p>
      <p className={styles.hint}>순서는 뒤에서 앞으로 증가</p>
      <p className={styles.hint}>
        이름·순서·FPS 저장은 이미지를 생성하지 않습니다. 생성 입력 변경은 해당 결과와 승인을
        무효화하고 계획 재검수가 필요합니다.
      </p>
      {tiles ? (
        <label className={styles.field}>
          격자 칸 너비 (원본 px)
          <input
            className={backgroundStyles.input}
            type="number"
            min="0"
            max={sprite.sourceCanvas.width}
            value={
              draft.grid === null
                ? ''
                : Number.isFinite(draft.grid.cellWidth)
                  ? draft.grid.cellWidth * sprite.sourceCanvas.width
                  : ''
            }
            placeholder={
              cell === null
                ? '격자 칸 너비 필요'
                : `자동 ${Math.round(cell * sprite.sourceCanvas.width)}px`
            }
            disabled={busy}
            onChange={(e) =>
              setDraft((current) => ({
                ...current,
                grid:
                  e.target.value === ''
                    ? null
                    : { cellWidth: e.target.valueAsNumber / sprite.sourceCanvas.width },
              }))
            }
          />
        </label>
      ) : null}
      {gridInvalid ? (
        <p role="alert" className={styles.error}>
          격자 칸 너비는 0보다 크고 원본 너비 {sprite.sourceCanvas.width}px 이하여야 합니다.
        </p>
      ) : null}
      {reconstructing ? (
        <>
          {ready ? (
            <div role="group" aria-label="재구성 보기" className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                size="sm"
                className={PRESSED}
                aria-pressed={mode === 'compare'}
                onClick={() => setMode('compare')}
              >
                비교
              </Button>
              <Button
                variant="outline"
                size="sm"
                className={PRESSED}
                aria-pressed={mode === 'edit'}
                onClick={() => setMode('edit')}
              >
                편집
              </Button>
            </div>
          ) : null}
          {tileFill.status === 'loading' ? (
            <p role="status" className={styles.hint}>
              재구성 계산 중…
            </p>
          ) : null}
          {tileFill.error ? (
            <p role="alert" className={styles.error}>
              {tileFill.error}
            </p>
          ) : null}
          {imagesFailed ? (
            <p role="alert" className={styles.error}>
              재구성 이미지 일부를 불러오지 못했습니다
            </p>
          ) : null}
          {imagesFailed || tileFill.retryable ? (
            <Button
              variant="outline"
              onClick={() => {
                setFailedImages([])
                tileFill.retry()
              }}
            >
              재구성 다시 계산
            </Button>
          ) : null}
        </>
      ) : null}
      <PartsOverlay
        sourceImageId={sourceImageId}
        underlay={
          ready ? (
            <div
              data-testid="reconstruction-clip"
              className="h-full w-full"
              style={{ clipPath: `inset(0 0 0 ${divider}%)` }}
            >
              <SpriteReconstructionLayer
                jobId={jobId}
                sprite={sprite}
                plans={displayPlans}
                fill={tileFill.fill}
                cell={tileCell}
                failedImageIds={failedImages}
                onImageError={(id) =>
                  setFailedImages((current) => (current.includes(id) ? current : [...current, id]))
                }
              />
            </div>
          ) : null
        }
        frameRef={roi.frameRef}
        selection={{
          selectedId: roi.selected?.partId ?? null,
          selectedOrdinal: roi.selected?.ordinal,
          // 칩은 같은 상자 묶음의 선택한 자리 유지
          onSelect: (id) => roi.select(id, roi.selected?.partId === id ? roi.selected.ordinal : 0),
          onSelectPlacement: roi.select,
        }}
        editing={{ preview: roi.preview, onPointerHandleDown: roi.onPointerHandleDown }}
        parts={overlayParts(displayPlans)}
        alwaysShowBoxes
        hideBoxes={comparing}
      >
        {ready ? (
          <SpriteCompareDivider position={divider} onChange={setDivider} interactive={comparing} />
        ) : null}
      </PartsOverlay>
      <p className={styles.hint}>
        {comparing
          ? '이미지를 좌우로 끌어 원본과 재구성을 비교하세요. 영역을 옮기려면 편집을 누르세요.'
          : '영역이나 이름을 선택하고 끌어 이동하세요. 손잡이는 크기 조정, Esc는 취소입니다.'}
      </p>
      <div className={styles.stack}>
        {displayPlans.map((p, index) => {
          const active = isActive(p.id)
          const object = assetRole(sprite.settings, p) === 'object'
          const placementsId = p.id + PLACEMENTS
          const placements = placementsOf(p)
          const selectedHere =
            roi.selected?.partId === p.id || roi.selected?.partId === placementsId
          // 선택한 자리 번호, null 은 생성 기준
          const ordinal = roi.selected?.partId === placementsId ? roi.selected.ordinal : null
          const bounds = ordinal === null ? p.sourceBounds : placements[ordinal]!
          return (
            <fieldset
              key={p.id}
              disabled={busy || active}
              className={`${styles.asset} ${selectedHere ? 'border-primary bg-primary/5' : ''}`}
              data-testid="sprite-plan-card"
            >
              <legend className="sr-only">대상 설정 {index + 1}</legend>
              <button
                type="button"
                aria-label={`대상 선택 ${index + 1}`}
                aria-pressed={selectedHere}
                onClick={() => selectBox(p.id, 0)}
                className="min-w-0 rounded text-left focus-visible:outline-2 focus-visible:outline-primary"
              >
                <span className="block break-words font-semibold">
                  {index + 1}. {p.name}
                </span>
                <span className={styles.hint}>
                  {spriteRoleLabel(assetRole(sprite.settings, p))}
                  {'canvas' in outputs[index]!
                    ? ` · 출력 ${outputs[index]!.canvas.width}×${outputs[index]!.canvas.height}px`
                    : ''}
                </span>
              </button>
              {tiles ? (
                <div className={styles.field}>
                  <label htmlFor={`sprite-role-${p.id}`}>에셋 역할 {index + 1}</label>
                  <Select
                    value={assetRole(sprite.settings, p)}
                    disabled={busy || active}
                    onValueChange={(value) =>
                      edit(p.id, (plan) => ({
                        ...plan,
                        role: value as SpriteAssetPlan['role'],
                        requiresTransparency:
                          value === 'object' ||
                          (value === 'tile' && sprite.settings.view === 'isometric'),
                        // 서버는 오브젝트 외 자리 거부
                        placements: value === 'object' ? plan.placements : null,
                      }))
                    }
                  >
                    <SelectTrigger id={`sprite-role-${p.id}`}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(['tile', 'object', 'backdrop'] as const).map((role) => (
                        <SelectItem key={role} value={role}>
                          {spriteRoleLabel(role)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              ) : null}
              {roleErrors[index] ? (
                <p role="alert" className={styles.error}>
                  {roleErrors[index]}
                </p>
              ) : null}
              <div className="grid min-w-0 grid-cols-2 gap-3 max-[360px]:grid-cols-1">
                <label className={styles.field}>
                  대상 이름 {index + 1}
                  <input
                    className={backgroundStyles.input}
                    value={p.name}
                    onChange={(e) => edit(p.id, { name: e.target.value })}
                  />
                </label>
                <label className={styles.field}>
                  깊이 순서 {index + 1}
                  <input
                    className={backgroundStyles.input}
                    type="number"
                    step="1"
                    value={p.order}
                    onChange={(e) => edit(p.id, { order: e.target.valueAsNumber })}
                  />
                </label>
              </div>
              {object ? (
                <div className="flex min-w-0 flex-col gap-2">
                  <p className={styles.hint}>
                    생성 기준(실선)을 바꾸면 다시 생성합니다. 자리(점선)는 재생성 없이 배치만
                    바꿉니다.
                  </p>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      className={PRESSED}
                      aria-label={`생성 기준 선택 ${index + 1}`}
                      aria-pressed={roi.selected?.partId === p.id}
                      onClick={() => selectBox(p.id, 0)}
                    >
                      생성 기준
                    </Button>
                    {placements.map((_, k) => (
                      <Button
                        key={k}
                        variant="outline"
                        size="sm"
                        className={PRESSED}
                        aria-label={`자리 ${k + 1} 선택 ${index + 1}`}
                        aria-pressed={ordinal === k}
                        onClick={() => selectBox(placementsId, k)}
                      >
                        자리 {k + 1}
                      </Button>
                    ))}
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={placements.length >= MAX_PLACEMENTS}
                      onClick={() => {
                        edit(p.id, (plan) => addPlacement(plan, ordinal))
                        roi.select(placementsId, placements.length)
                      }}
                    >
                      자리 추가 {index + 1}
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      disabled={ordinal === null || placements.length <= 1}
                      onClick={() => {
                        if (ordinal === null) return
                        edit(p.id, (plan) => ({
                          ...plan,
                          placements: placementsOf(plan).filter((_, i) => i !== ordinal),
                        }))
                        // 같은 번호의 다음 자리, 없으면 마지막 자리
                        roi.select(placementsId, Math.min(ordinal, placements.length - 2))
                      }}
                    >
                      선택한 자리 삭제 {index + 1}
                    </Button>
                  </div>
                </div>
              ) : null}
              <details className="min-w-0">
                <summary className="cursor-pointer text-sm text-muted-foreground">
                  정밀 조정 {index + 1}
                </summary>
                {object ? (
                  <p className={`mt-2 ${styles.hint}`}>
                    {ordinal === null ? '생성 기준' : `자리 ${ordinal + 1}`} 좌표
                  </p>
                ) : null}
                <fieldset className="mt-2 grid grid-cols-2 gap-2">
                  {(['x', 'y', 'w', 'h'] as const).map((axis) => (
                    <label key={axis} className={styles.field}>
                      {{ x: 'X 위치', y: 'Y 위치', w: '너비', h: '높이' }[axis]}
                      <input
                        className={backgroundStyles.input}
                        aria-label={`${ordinal === null ? 'ROI' : `자리 ${ordinal + 1} ROI`} ${axis} ${index + 1}`}
                        type="number"
                        min="0"
                        max="1"
                        step="0.01"
                        value={Number.isFinite(bounds[axis]) ? bounds[axis] : ''}
                        onChange={(e) => {
                          const value = e.target.valueAsNumber
                          edit(p.id, (plan) =>
                            withBox(plan, ordinal === null ? p.id : placementsId, ordinal ?? 0, {
                              ...bounds,
                              [axis]: value,
                            }),
                          )
                        }}
                      />
                    </label>
                  ))}
                </fieldset>
              </details>
              <div className="flex flex-wrap gap-x-5 gap-y-2">
                <label className={styles.row}>
                  <input
                    type="checkbox"
                    checked={p.requiresTransparency}
                    disabled={tiles}
                    onChange={(e) => edit(p.id, { requiresTransparency: e.target.checked })}
                  />
                  투명 배경 {index + 1}
                </label>
                <label className={styles.row}>
                  <input
                    type="checkbox"
                    checked={p.loop}
                    onChange={(e) =>
                      edit(p.id, {
                        loop: e.target.checked,
                        frameCount: [4, 8].includes(p.frameCount) ? p.frameCount : 8,
                      })
                    }
                  />
                  루프 애니메이션 {index + 1}
                </label>
              </div>
              <div className="grid min-w-0 grid-cols-2 gap-3 max-[360px]:grid-cols-1">
                <div className={styles.field}>
                  <label htmlFor={`sprite-frames-${p.id}`}>프레임 수 {index + 1}</label>
                  <Select
                    disabled={busy || active || !p.loop}
                    value={String([4, 8].includes(p.frameCount) ? p.frameCount : 8)}
                    onValueChange={(value) => edit(p.id, { frameCount: Number(value) })}
                  >
                    <SelectTrigger id={`sprite-frames-${p.id}`}>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="4">4프레임</SelectItem>
                      <SelectItem value="8">8프레임</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <label className={styles.field}>
                  FPS {index + 1}
                  <input
                    className={backgroundStyles.input}
                    type="number"
                    min="1"
                    max="30"
                    step="1"
                    value={Number.isFinite(p.fps) ? p.fps : ''}
                    onChange={(e) => edit(p.id, { fps: e.target.valueAsNumber })}
                  />
                </label>
              </div>
              <label className={styles.field}>
                동작 설명 {index + 1}
                <textarea
                  className={backgroundStyles.input}
                  maxLength={500}
                  value={p.motionNotes}
                  onChange={(e) => edit(p.id, { motionNotes: e.target.value })}
                />
              </label>
              <p className={styles.hint}>
                {p.loop ? p.frameCount : 1}프레임 · 기준 승인 후 루프 생성 · 설명{' '}
                {p.motionNotes.length}/500자
              </p>
              {planning ? (
                <Button
                  variant="destructive"
                  disabled={plans.length === 1}
                  onClick={() => setPlans(plans.filter((plan) => plan.id !== p.id))}
                >
                  대상 {index + 1} 삭제
                </Button>
              ) : null}
            </fieldset>
          )
        })}
      </div>
      <div className={styles.row}>
        {planning ? (
          <Button
            variant="outline"
            disabled={busy || plans.length >= 12}
            onClick={() =>
              setPlans([
                ...plans,
                {
                  id: crypto.randomUUID(),
                  name: `대상 ${plans.length + 1}`,
                  order: Math.max(...plans.map((p) => p.order)) + 1,
                  sourceBounds: { x: 0, y: 0, w: 1, h: 1 },
                  placements: null,
                  requiresTransparency: true,
                  loop: false,
                  frameCount: 8,
                  fps: 8,
                  motionNotes: '',
                  role: sprite.settings.outputKind === 'layers' ? 'layer' : 'tile',
                },
              ])
            }
          >
            대상 추가
          </Button>
        ) : null}
        <Button
          variant="outline"
          disabled={busy || !dirty || invalid || stale}
          onClick={() => update.mutate({ ...context(), assets: plans, grid: draft.grid })}
        >
          계획 저장
        </Button>
        {planning ? (
          <Button
            disabled={busy || dirty || invalid || stale}
            onClick={() => approve.mutate(context())}
          >
            계획 승인 · 기준 이미지 생성
          </Button>
        ) : null}
      </div>
      {stale ? (
        <div role="alert">
          <p className={styles.error}>
            서버 계획이 변경되었습니다. 미저장 편집을 보존했습니다. 최신 계획을 명시적으로 불러온 뒤
            다시 편집해 주세요.
          </p>
          <Button variant="outline" onClick={() => setDraft(serverDraft())}>
            서버 계획으로 다시 불러오기
          </Button>
        </div>
      ) : null}
      {dirty ? <p className={styles.hint}>변경한 계획을 저장한 뒤 승인해 주세요.</p> : null}
      {invalid ? (
        <p role="alert" className={styles.error}>
          대상은 1..12개·최대 64프레임입니다. 이름·중복 없는 정수 순서·원본 내부의 양수 ROI·오브젝트
          자리 1..20개·필수 투명 배경·4/8프레임·FPS 1..30·설명 500자 이하를 확인해 주세요.
        </p>
      ) : null}
      {[update, approve].map((mutation, index) =>
        mutation.error ? (
          <div key={index} role="alert">
            <p className={styles.error}>
              {apiErrorStatus(mutation.error) === 409
                ? '계획 충돌: 서버 상태를 다시 조회했습니다. 변경 내용을 확인하고 새 동작으로 저장·승인해 주세요. '
                : ''}
              {apiErrorMessage(mutation.error, '요청 응답을 받지 못했습니다')}
            </p>
          </div>
        ) : null,
      )}
      {update.error &&
      update.variables &&
      (apiErrorStatus(update.error) === null ||
        apiErrorStatus(update.error) === 0 ||
        (apiErrorStatus(update.error) ?? 0) >= 500) ? (
        <Button variant="outline" disabled={busy} onClick={() => update.mutate(update.variables!)}>
          같은 계획 요청 재전송
        </Button>
      ) : null}
      {approve.error &&
      approve.variables &&
      (apiErrorStatus(approve.error) === null ||
        apiErrorStatus(approve.error) === 0 ||
        (apiErrorStatus(approve.error) ?? 0) >= 500) ? (
        <Button
          variant="outline"
          disabled={busy}
          onClick={() => approve.mutate(approve.variables!)}
        >
          같은 승인 요청 재전송
        </Button>
      ) : null}
    </section>
  )
}
