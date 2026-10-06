import { useState } from 'react'
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
  const [draft, setDraft] = useState({ revision: sprite.reviewRevision, plans: saved, saved })
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
  const dirty = JSON.stringify(plans) !== JSON.stringify(draft.saved)
  const stale = draft.revision !== sprite.reviewRevision
  // 미저장 draft 보존, 저장 반영 또는 변경 없는 draft만 동기화
  if (stale && (!dirty || JSON.stringify(plans) === JSON.stringify(saved)))
    setDraft({ revision: sprite.reviewRevision, plans: saved, saved })
  const backmost = Math.min(...plans.map((p) => p.order))
  const invalid =
    plans.length < 1 ||
    plans.length > 12 ||
    count > 64 ||
    new Set(plans.map((p) => p.order)).size !== plans.length ||
    plans.some((p) => {
      const { x, y, w, h } = p.sourceBounds
      const transparent =
        sprite.settings.outputKind === 'layers'
          ? p.order !== backmost
          : sprite.settings.view === 'isometric'
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
        (transparent && !p.requiresTransparency)
      )
    })
  const busy = disabled || update.isPending || approve.isPending
  function context() {
    return { jobId, requestId: crypto.randomUUID(), expectedRevision: draft.revision }
  }
  function edit(id: string, patch: Partial<SpriteAssetPlan>) {
    setPlans((current) => current.map((p) => (p.id === id ? { ...p, ...patch } : p)))
  }
  return (
    <section className={styles.panel} aria-label="제작 계획 검수">
      <h2 className={styles.title}>제작 계획 검수</h2>
      <p className={styles.hint}>
        {plans.length}/12개 · 정적 대상 1프레임 · 총 {count}/64장 · 모델 {model} ·{' '}
        {price == null ? '비용 미확인' : `예상 이미지 비용 $${(price * count).toFixed(2)}`}
      </p>
      <p className={styles.hint}>
        원본 {sprite.sourceCanvas.width}×{sprite.sourceCanvas.height} → 모델 요청{' '}
        {sprite.generationCanvas.width}×{sprite.generationCanvas.height} → 최종{' '}
        {sprite.outputCanvas.width}×{sprite.outputCanvas.height}px · 순서는 뒤에서 앞으로 증가
      </p>
      <p className={styles.hint}>
        이름·순서·FPS 저장은 이미지를 생성하지 않습니다. 생성 입력 변경은 해당 결과와 승인을
        무효화하고 계획 재검수가 필요합니다.
      </p>
      <PartsOverlay
        sourceImageId={sourceImageId}
        parts={plans.map((plan, index) => ({
          id: plan.id,
          name: plan.name,
          label: `${index + 1}. ${plan.name}`,
          placements: [plan.sourceBounds],
        }))}
        alwaysShowBoxes
      />
      <div className={styles.stack}>
        {plans.map((p, index) => {
          const active = sprite.assets
            .find((a) => a.id === p.id)
            ?.frames.some((f) =>
              tasks.some(
                (t) =>
                  t.id === f.currentTaskId && (t.status === 'running' || t.status === 'pending'),
              ),
            )
          return (
            <fieldset key={p.id} disabled={busy || active} className={styles.asset}>
              <legend>
                {index + 1}. {p.name}
              </legend>
              <div className={styles.grid}>
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
              <fieldset className="grid grid-cols-4 gap-2 max-[720px]:grid-cols-2">
                {(['x', 'y', 'w', 'h'] as const).map((axis) => (
                  <label key={axis} className={styles.field}>
                    ROI {axis} {index + 1}
                    <input
                      className={backgroundStyles.input}
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
              <label className={styles.row}>
                <input
                  type="checkbox"
                  checked={p.requiresTransparency}
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
              <div className={styles.grid}>
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
          onClick={() => update.mutate({ ...context(), assets: plans })}
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
          <Button
            variant="outline"
            onClick={() => setDraft({ revision: sprite.reviewRevision, plans: saved, saved })}
          >
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
