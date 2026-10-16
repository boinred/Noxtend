import { useState } from 'react'
import { useSpriteRoiEditor } from './useSpriteRoiEditor'
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
import { useUpdateSpritePlan, useApproveSpritePlan } from '@/app/queries/useSprites'
import { useModelPrices } from '@/app/queries/useTuning'
import { apiErrorMessage, apiErrorStatus } from '@/app/queries/errors'
import { assetRole, assetOutputSize, cellWidthOf } from '@/domain/sprites/rules'
import { spriteRoleLabel } from '@/domain/sprites/labels'
import type { SpriteGrid } from '@/domain/sprites/types'
import type { SpriteState, SpriteAssetPlan } from '@/domain/sprites/types'
import { spriteStyles as styles } from './spriteStyles'

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
          sourceBounds: {
            x: p.sourceBounds.x,
            y: p.sourceBounds.y,
            w: p.sourceBounds.w,
            h: p.sourceBounds.h,
          },
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
      const { x, y, w, h } = p.sourceBounds
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
        ![x, y, w, h].every(Number.isFinite) ||
        x < 0 ||
        y < 0 ||
        w <= 0 ||
        h <= 0 ||
        x + w > 1 ||
        y + h > 1 ||
        (transparent && !p.requiresTransparency) ||
        (assetRole(sprite.settings, p) === 'backdrop' && p.requiresTransparency)
      )
    })
  const busy = disabled || update.isPending || approve.isPending
  function context() {
    return { jobId, requestId: crypto.randomUUID(), expectedRevision: draft.revision }
  }
  function edit(id: string, patch: Partial<SpriteAssetPlan>) {
    setPlans((current) => current.map((p) => (p.id === id ? { ...p, ...patch } : p)))
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
  const roi = useSpriteRoiEditor({
    plans,
    lockedIds: plans
      .filter((plan) => isActive(plan.id))
      .map((plan) => plan.id)
      .join(','),
    disabled: busy,
    revision: `${draft.revision}:${sprite.reviewRevision}`,
    onCommit: (id, sourceBounds) => edit(id, { sourceBounds }),
  })
  const displayPlans = plans.map((plan) =>
    roi.preview?.partId === plan.id ? { ...plan, sourceBounds: roi.preview.bounds } : plan,
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
      <PartsOverlay
        sourceImageId={sourceImageId}
        frameRef={roi.frameRef}
        selection={{ selectedId: roi.selectedId, onSelect: roi.setSelectedId }}
        editing={{ preview: roi.preview, onPointerHandleDown: roi.onPointerHandleDown }}
        parts={displayPlans.map((plan, index) => ({
          id: plan.id,
          name: plan.name,
          label: `${index + 1}. ${plan.name}${tiles ? ` · ${spriteRoleLabel(assetRole(sprite.settings, plan))}` : ''}`,
          placements: [plan.sourceBounds],
        }))}
        alwaysShowBoxes
      />
      <p className={styles.hint}>
        영역이나 이름을 선택하고 끌어 이동하세요. 손잡이는 크기 조정, Esc는 취소입니다.
      </p>
      <div className={styles.stack}>
        {displayPlans.map((p, index) => {
          const active = isActive(p.id)
          return (
            <fieldset
              key={p.id}
              disabled={busy || active}
              className={`${styles.asset} ${roi.selectedId === p.id ? 'border-primary bg-primary/5' : ''}`}
              data-testid="sprite-plan-card"
            >
              <legend className="sr-only">대상 설정 {index + 1}</legend>
              <button
                type="button"
                aria-label={`대상 선택 ${index + 1}`}
                aria-pressed={roi.selectedId === p.id}
                onClick={() => roi.setSelectedId(p.id)}
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
                      edit(p.id, {
                        role: value as SpriteAssetPlan['role'],
                        requiresTransparency:
                          value === 'object' ||
                          (value === 'tile' && sprite.settings.view === 'isometric'),
                      })
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
              <details className="min-w-0">
                <summary className="cursor-pointer text-sm text-muted-foreground">
                  정밀 조정 {index + 1}
                </summary>
                <fieldset className="mt-2 grid grid-cols-2 gap-2">
                  {(['x', 'y', 'w', 'h'] as const).map((axis) => (
                    <label key={axis} className={styles.field}>
                      {{ x: 'X 위치', y: 'Y 위치', w: '너비', h: '높이' }[axis]}
                      <input
                        className={backgroundStyles.input}
                        aria-label={`ROI ${axis} ${index + 1}`}
                        type="number"
                        min="0"
                        max="1"
                        step="0.01"
                        value={Number.isFinite(p.sourceBounds[axis]) ? p.sourceBounds[axis] : ''}
                        onChange={(e) =>
                          edit(p.id, {
                            sourceBounds: { ...p.sourceBounds, [axis]: e.target.valueAsNumber },
                          })
                        }
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
          대상은 1..12개·최대 64프레임입니다. 이름·중복 없는 정수 순서·원본 내부의 양수 ROI·필수
          투명 배경·4/8프레임·FPS 1..30·설명 500자 이하를 확인해 주세요.
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
