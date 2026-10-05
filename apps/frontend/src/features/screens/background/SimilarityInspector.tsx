/**
 * 유사도 inspector (background-similarity-tuning §14).
 *
 * **모든 유료 호출은 명시적 실행 뒤에만** — 시작 전에 모델·반복 횟수·최대 호출 수·
 * 예상 최대 비용을 먼저 보여주고, 단가 미등록이면 사용자가 확인해야 실행된다 (§11.2).
 * 점수는 상대 개선 신호다 — 절대 품질 보증으로 말하지 않는다 (§5.3).
 *
 * 후보 흐름 (§9.2): 보정 선택 → 서버가 후보 revision 생성 → 같은 offscreen 경로로
 * 후보를 캡처해 업로드 → 서버가 재평가·채택/거부. 업로드 뒤에는 화면을 떠나도 끝난다.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useProviders, useProviderModels } from '@/app/queries/useProviders'
import {
  useSimilarityEstimate,
  useSimilarityStatus,
  useStartSimilarityRun,
} from '@/app/queries/useSimilarity'
import { queryKeys } from '@/app/queries/keys'
import { apiErrorMessage } from '@/app/queries/errors'
import {
  cancelSimilarityRun,
  completeSimilarityRun,
  createSimilarityCandidate,
  getSimilarityRun,
  listSceneRevisions,
  restoreSceneRevision,
  retrySimilarityRun,
  uploadCandidateRender,
} from '@/app/queries/similarityActions'
import { sourceImageUrl } from '@/app/queries/media'
import {
  DIMENSION_LABELS,
  REVISION_ORIGIN_LABELS,
  adjustmentSummary,
  baselineScore,
  defaultSelectedAdjustments,
  isRunActive,
} from '@/domain/similarity/types'
import type { SceneRevision, SimilarityRun, SimilarityScore } from '@/domain/similarity/types'
import type { SceneLayoutData } from '@/domain/job/types'
import type { SceneCaptureHandle } from './SceneAssemblyView'
import { captureFrameFor } from './sceneCapture'
import type { CaptureFrame } from './sceneCapture'
import { backgroundStyles as styles } from './backgroundStyles'

export interface SimilarityInspectorProps {
  jobId: string
  /** 비교 슬라이더의 왼쪽 — 사용자의 원본 사진 (§14.1). */
  sourceImageId: string
  layout: SceneLayoutData | null
  /** 결정적 캡처 손잡이 — 준비 전(null)에는 실행 버튼이 죽어 있다 (§8.2). */
  capture: SceneCaptureHandle | null
}

export function SimilarityInspector({
  jobId,
  sourceImageId,
  layout,
  capture,
}: SimilarityInspectorProps) {
  const { status } = useSimilarityStatus(jobId, true)
  const { providers } = useProviders()
  const start = useStartSimilarityRun(jobId)
  const client = useQueryClient()

  // 평가 가능한 공급자만 — 두 이미지 + structured output (§7.2)
  const evaluators = useMemo(
    () =>
      providers.filter(
        (provider) => provider.isEnabled && provider.capabilities.includes('similarityEvaluation'),
      ),
    [providers],
  )

  const [providerId, setProviderId] = useState<string | null>(null)
  const selectedProviderId = providerId ?? evaluators[0]?.id ?? null
  const { models } = useProviderModels(selectedProviderId)
  const [model, setModel] = useState<string | null>(null)
  const selectedModel = model ?? models[0]?.id ?? null
  const [maxIterations, setMaxIterations] = useState(1)
  // 단가 미등록 확인 (§11.2) — 확인 전에는 실행할 수 없다
  const [unknownPriceConfirmed, setUnknownPriceConfirmed] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  // 비교 슬라이더의 오른쪽 — 시작 시 캡처한 렌더를 들고 있는다 (§14.1).
  // 화면 재진입(폴링만 있는 경우)에는 없다 — 그때는 슬라이더를 그리지 않는다
  const [renderPreview, setRenderPreview] = useState<string | null>(null)

  // 캡처 프레임 = 원본 비율, 긴 변 1024 (사용자 결정) — 정사각 고정은 세로형 원본에
  // 레터박스를 만들고 평가가 서로 다른 프레임을 보게 했다. 원본 크기를 한 번 읽는다
  const [frame, setFrame] = useState<CaptureFrame | null>(null)
  useEffect(() => {
    const image = new Image()
    image.onload = () => setFrame(captureFrameFor(image.naturalWidth, image.naturalHeight))
    image.src = sourceImageUrl(sourceImageId)
  }, [sourceImageId])
  useEffect(
    () => () => {
      if (renderPreview) URL.revokeObjectURL(renderPreview)
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  )

  const estimate = useSimilarityEstimate(
    jobId,
    selectedModel,
    maxIterations,
    status?.eligible === true,
  )

  /**
   * Idempotency-Key (§10.1) — 실행 세션마다 하나. 실패한 POST 의 재시도는 같은 키를
   * 재사용해 유료 호출이 늘지 않고, run 이 하나 끝나면 다음 실행을 위해 회전한다.
   */
  const idempotencyKey = useRef<string>(crypto.randomUUID())
  const hadOpenRun = useRef(false)
  const [settledRun, setSettledRun] = useState<SimilarityRun | null>(null)
  const lastOpenRun = useRef<SimilarityRun | null>(null)

  useEffect(() => {
    const open = isRunActive(status?.openRun ?? null)
    if (open) lastOpenRun.current = status!.openRun

    // run 종료 — 채택이 활성 revision 을 바꿨을 수 있다: 장면 명세를 다시 받고,
    // 마지막 폴링 이전 스냅숏에는 최종 점수가 없으므로 상세를 한 번 더 받는다
    if (hadOpenRun.current && !open) {
      idempotencyKey.current = crypto.randomUUID()
      const finishedId = lastOpenRun.current?.id
      setSettledRun(lastOpenRun.current)
      if (finishedId) {
        void getSimilarityRun(jobId, finishedId)
          .then(setSettledRun)
          .catch(() => {})
      }
      void client.invalidateQueries({ queryKey: ['scene-layout', jobId] })
    }

    hadOpenRun.current = open
  }, [status, client, jobId])

  const run = status?.openRun ?? null
  const canStart =
    status?.eligible === true &&
    run === null &&
    layout !== null &&
    capture !== null &&
    selectedProviderId !== null &&
    selectedModel !== null &&
    (estimate?.priceKnown === true || unknownPriceConfirmed) &&
    !start.isPending

  const onStart = async () => {
    if (!capture || !layout || !selectedProviderId || !selectedModel) return
    setActionError(null)
    setSettledRun(null)

    let render: Blob
    try {
      render = await capture(undefined, undefined, frame ?? undefined)
    } catch (cause) {
      setActionError(cause instanceof Error ? cause.message : '캡처에 실패했습니다')
      return
    }

    setRenderPreview((prev) => {
      if (prev) URL.revokeObjectURL(prev)
      return URL.createObjectURL(render)
    })

    start.mutate({
      jobId,
      providerConfigId: selectedProviderId,
      model: selectedModel,
      maxIterations,
      layoutId: layout.id,
      idempotencyKey: idempotencyKey.current,
      render,
    })
  }

  /** 후보 적용 (§9.2 1~4단계) — 생성 → 오버라이드 캡처 → 업로드. 이후는 서버 몫이다. */
  const [applying, setApplying] = useState(false)
  const onApply = async (runId: string, adjustmentIds: string[]) => {
    if (!capture) return
    setActionError(null)
    setApplying(true)

    try {
      const created = await createSimilarityCandidate(jobId, runId, adjustmentIds)

      // 후보 revision 을 같은 offscreen 경로로 — 화면의 현재 장면은 바뀌지 않는다 (§8.2)
      const overrides = {
        instances: Object.fromEntries(
          created.candidateLayout.instances.map((instance) => [
            `${instance.partId}-${instance.ordinal}`,
            {
              position: instance.position,
              rotationY: instance.rotationY,
              scale: instance.scaleVector,
            },
          ]),
        ),
        light: created.candidateLayout.light,
      }
      const render = await capture(overrides, created.candidateLayout.camera, frame ?? undefined)

      await uploadCandidateRender(jobId, runId, created.evaluation.id, render)
      await client.invalidateQueries({ queryKey: queryKeys.similarityStatus(jobId) })
    } catch (cause) {
      setActionError(apiErrorMessage(cause, '보정 적용에 실패했습니다'))
    } finally {
      setApplying(false)
    }
  }

  const refreshStatus = () =>
    client.invalidateQueries({ queryKey: queryKeys.similarityStatus(jobId) })

  const onComplete = async (runId: string) => {
    setActionError(null)
    try {
      await completeSimilarityRun(jobId, runId)
      await refreshStatus()
    } catch (cause) {
      setActionError(apiErrorMessage(cause, '종료하지 못했습니다'))
    }
  }

  const onCancel = async (runId: string) => {
    setActionError(null)
    try {
      await cancelSimilarityRun(jobId, runId)
      await refreshStatus()
    } catch (cause) {
      setActionError(apiErrorMessage(cause, '취소하지 못했습니다'))
    }
  }

  const onRetry = async (runId: string) => {
    setActionError(null)
    try {
      await retrySimilarityRun(jobId, runId)
      setSettledRun(null)
      await refreshStatus()
    } catch (cause) {
      setActionError(apiErrorMessage(cause, '재시도하지 못했습니다'))
    }
  }

  return (
    <aside className={styles.similarityPanel} data-testid="similarity-inspector">
      <h3 className={styles.similarityTitle}>원본 유사도</h3>

      {status === null ? (
        <p className={styles.similarityHint}>불러오는 중…</p>
      ) : run !== null ? (
        <RunPanel
          run={run}
          applying={applying}
          onApply={onApply}
          onComplete={onComplete}
          onCancel={onCancel}
          actionError={actionError}
          referenceUrl={sourceImageUrl(sourceImageId)}
          renderPreview={renderPreview}
          frame={frame}
        />
      ) : settledRun !== null ? (
        <SettledPanel
          run={settledRun}
          adopted={
            status.activeLayoutId !== null &&
            settledRun.evaluations.some(
              (e) => e.kind === 'candidate' && e.layoutId === status.activeLayoutId,
            )
          }
          onReset={() => setSettledRun(null)}
          onRetry={onRetry}
        />
      ) : status.eligible ? (
        <div className={styles.similarityForm}>
          <div className={styles.similarityFormRow}>
            <div className={styles.similarityField}>
              공급자
              <Select
                value={selectedProviderId ?? ''}
                onValueChange={(value) => {
                  setProviderId(value)
                  setModel(null)
                }}
              >
                <SelectTrigger data-testid="similarity-provider">
                  <SelectValue placeholder="공급자 선택" />
                </SelectTrigger>
                <SelectContent>
                  {evaluators.map((provider) => (
                    <SelectItem key={provider.id} value={provider.id}>
                      {provider.displayName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className={styles.similarityField}>
              모델
              <Select value={selectedModel ?? ''} onValueChange={setModel}>
                <SelectTrigger data-testid="similarity-model">
                  <SelectValue placeholder="모델 선택" />
                </SelectTrigger>
                <SelectContent>
                  {models.map((entry) => (
                    <SelectItem key={entry.id} value={entry.id}>
                      {entry.displayName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className={styles.similarityField}>
              보정 반복
              <Select
                value={String(maxIterations)}
                onValueChange={(value) => setMaxIterations(Number(value))}
              >
                <SelectTrigger data-testid="similarity-iterations">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {[1, 2, 3].map((count) => (
                    <SelectItem key={count} value={String(count)}>
                      {count}회
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          {/* 예상 최대 비용과 실제 비용은 다른 숫자다 (§11.2) — 추정은 보증이 아니다 */}
          <p className={styles.similarityEstimate} data-testid="similarity-estimate">
            {estimate === null
              ? '비용 계산 중…'
              : estimate.priceKnown
                ? `최대 평가 ${estimate.maximumCalls}회 · 예상 최대 $${estimate.estimatedMaximumCostUsd?.toFixed(2)}`
                : `최대 평가 ${estimate.maximumCalls}회 · 단가 미등록`}
          </p>

          {estimate !== null && !estimate.priceKnown ? (
            <label className={styles.similarityConfirm}>
              <input
                type="checkbox"
                data-testid="similarity-price-confirm"
                checked={unknownPriceConfirmed}
                onChange={(event) => setUnknownPriceConfirmed(event.target.checked)}
              />
              단가 미등록을 확인했습니다
            </label>
          ) : null}

          <Button
            variant="default"
            data-testid="similarity-start"
            disabled={!canStart}
            onClick={() => void onStart()}
          >
            원본과 비교
          </Button>
          <p className={styles.similarityHint}>유료 평가 1회가 시작됩니다.</p>

          {capture === null ? (
            <p className={styles.similarityHint} data-testid="similarity-capture-pending">
              3D 장면이 준비되면 실행할 수 있습니다.
            </p>
          ) : null}
          {actionError !== null ? (
            <p className={styles.similarityError} data-testid="similarity-capture-error">
              {actionError}
            </p>
          ) : null}
          {start.isError ? (
            <p className={styles.similarityError} data-testid="similarity-start-error">
              {apiErrorMessage(start.error, '비교를 시작하지 못했습니다')}
            </p>
          ) : null}
        </div>
      ) : (
        // 자격 미충족 — 서버 사유를 그대로 (§11.1)
        <ul className={styles.similarityBlocking} data-testid="similarity-blocking">
          {status.blockingReasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      )}

      {status !== null ? (
        <RevisionHistory
          jobId={jobId}
          onRestored={() => {
            // 복원은 활성 revision 을 바꾼다 — 장면 명세를 다시 받는다 (§4.3)
            void client.invalidateQueries({ queryKey: ['scene-layout', jobId] })
            void refreshStatus()
          }}
        />
      ) : null}
    </aside>
  )
}

/**
 * revision 이력·복원 (§14.1) — 펼칠 때만 불러온다: 이력은 보조 정보라 폴링에 얹지 않는다.
 * 복원은 과거 행을 되살리지 않고 값을 새 활성 revision 으로 복사한다 (§4.3).
 */
function RevisionHistory({ jobId, onRestored }: { jobId: string; onRestored: () => void }) {
  const [revisions, setRevisions] = useState<SceneRevision[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = async () => {
    try {
      setRevisions(await listSceneRevisions(jobId))
    } catch (cause) {
      setError(apiErrorMessage(cause, '이력을 불러오지 못했습니다'))
    }
  }

  const restore = async (layoutId: string) => {
    setError(null)
    try {
      await restoreSceneRevision(jobId, layoutId)
      await load()
      onRestored()
    } catch (cause) {
      setError(apiErrorMessage(cause, '복원하지 못했습니다'))
    }
  }

  return (
    <details
      className={styles.similarityHistory}
      data-testid="similarity-history"
      onToggle={(event) => {
        if ((event.target as HTMLDetailsElement).open && revisions === null) void load()
      }}
    >
      <summary>배치 이력</summary>
      {revisions === null ? (
        <p className={styles.similarityHint}>불러오는 중…</p>
      ) : (
        <ul className={styles.similarityHistoryList}>
          {revisions.map((revision) => (
            <li
              key={revision.id}
              className={styles.similarityHistoryItem}
              data-testid="similarity-revision"
              data-state={revision.state}
            >
              <span className={styles.similarityHistoryLabel}>
                r{revision.revision} · {REVISION_ORIGIN_LABELS[revision.origin]}
                {revision.state === 'active' ? ' · 사용 중' : ''}
              </span>
              {revision.state !== 'active' && revision.state !== 'candidate' ? (
                <button
                  type="button"
                  className={styles.similarityHistoryRestore}
                  data-testid="similarity-restore"
                  onClick={() => void restore(revision.id)}
                >
                  복원
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      {error !== null ? <p className={styles.similarityError}>{error}</p> : null}
    </details>
  )
}

/** 진행·결과 — run 하나의 화면 (§14.2). */
function RunPanel({
  run,
  applying,
  onApply,
  onComplete,
  onCancel,
  actionError,
  referenceUrl,
  renderPreview,
  frame,
}: {
  run: SimilarityRun
  applying: boolean
  onApply: (runId: string, adjustmentIds: string[]) => void
  onComplete: (runId: string) => void
  onCancel: (runId: string) => void
  actionError: string | null
  referenceUrl: string
  renderPreview: string | null
  frame: CaptureFrame | null
}) {
  const latest = run.evaluations.filter((e) => e.status === 'succeeded').at(-1) ?? null
  const score = latest?.score ?? baselineScore(run)

  // confidence 0.75 이상만 기본 선택 (§6)
  const [selected, setSelected] = useState<string[]>([])
  const seededFor = useRef<string | null>(null)
  useEffect(() => {
    if (latest !== null && seededFor.current !== latest.id) {
      seededFor.current = latest.id
      setSelected(defaultSelectedAdjustments(latest.adjustments))
    }
  }, [latest])

  const canApply =
    run.status === 'readyForAdjustment' &&
    run.currentIteration < run.maxIterations &&
    latest !== null &&
    selected.length > 0 &&
    !applying

  return (
    <div className={styles.similarityRun}>
      {/* 상태 갱신은 조용히 읽어 준다 (§14.3) */}
      <p className={styles.similarityStatus} aria-live="polite" data-testid="similarity-status">
        {statusLine(run, applying)}
      </p>

      {score !== null ? <ScorePanel score={score} /> : null}

      {/* 원본/렌더 비교 (§14.1) — clip-path + 접근 가능한 range. 렌더는 이 세션의
          캡처가 있을 때만 그린다: 재진입에는 blob 이 없다 */}
      {renderPreview !== null ? (
        <ComparisonSlider referenceUrl={referenceUrl} renderUrl={renderPreview} frame={frame} />
      ) : null}

      {latest !== null && run.status === 'readyForAdjustment' ? (
        <div className={styles.similarityAdjustments}>
          {latest.adjustments.map((adjustment) => (
            <label
              key={adjustment.id}
              className={styles.similarityAdjustment}
              data-testid="similarity-adjustment"
            >
              <input
                type="checkbox"
                checked={selected.includes(adjustment.id)}
                onChange={(event) =>
                  setSelected((prev) =>
                    event.target.checked
                      ? [...prev, adjustment.id]
                      : prev.filter((id) => id !== adjustment.id),
                  )
                }
              />
              <span className={styles.similarityAdjustmentBody}>
                <span className={styles.similarityAdjustmentSummary}>
                  {adjustmentSummary(adjustment.command)}
                </span>
                <span className={styles.similarityAdjustmentReason}>
                  {adjustment.reason} · 확신 {Math.round(adjustment.confidence * 100)}%
                </span>
              </span>
            </label>
          ))}

          {latest.adjustments.length > 0 ? (
            <Button
              variant="default"
              data-testid="similarity-apply"
              disabled={!canApply}
              onClick={() => void onApply(run.id, selected)}
            >
              선택한 보정 적용 · 평가 1회
            </Button>
          ) : null}

          {/* mesh/texture 문제는 자동 적용 불가 — 분리해 보여준다 (§14.1) */}
          {latest.regenerationNotes.length > 0 ? (
            <div className={styles.similarityNotes} data-testid="similarity-notes">
              <p className={styles.similarityNotesTitle}>자동 적용 불가</p>
              {latest.regenerationNotes.map((note) => (
                <p key={note} className={styles.similarityNote}>
                  {note}
                </p>
              ))}
            </div>
          ) : null}
        </div>
      ) : null}

      {/* 상태별 보조 동작 (§14.2 · §9.3) */}
      <div className={styles.similarityActions}>
        {run.status === 'readyForAdjustment' || run.status === 'awaitingRender' ? (
          <Button
            variant="outline"
            data-testid="similarity-complete"
            onClick={() => onComplete(run.id)}
          >
            이 결과로 마치기
          </Button>
        ) : null}
        {!['completed', 'failed', 'canceled'].includes(run.status) ? (
          <Button
            variant="outline"
            data-testid="similarity-cancel"
            onClick={() => onCancel(run.id)}
          >
            취소
          </Button>
        ) : null}
      </div>

      {actionError !== null ? (
        <p className={styles.similarityError} data-testid="similarity-apply-error">
          {actionError}
        </p>
      ) : null}
    </div>
  )
}

/**
 * 원본/렌더 비교 슬라이더 (§14.1) — clip-path 로 렌더를 가르고, native range 가
 * 그대로 키보드 동작을 제공한다 (§14.3: spring·관성 없음).
 */
function ComparisonSlider({
  referenceUrl,
  renderUrl,
  frame,
}: {
  referenceUrl: string
  renderUrl: string
  frame: CaptureFrame | null
}) {
  const [position, setPosition] = useState(50)

  return (
    <div className={styles.similarityCompare} data-testid="similarity-compare">
      <div
        className={styles.similarityCompareStage}
        // 스테이지 비율 = 캡처 프레임 = 원본 비율 — 레터박스 없이 두 장이 겹친다
        style={frame ? { aspectRatio: `${frame.width} / ${frame.height}` } : undefined}
      >
        <img src={referenceUrl} alt="원본 사진" className={styles.similarityCompareImage} />
        <img
          src={renderUrl}
          alt="3D 렌더"
          className={styles.similarityCompareImage}
          style={{ clipPath: `inset(0 0 0 ${position}%)` }}
        />
        <span className={styles.similarityCompareBar} style={{ left: `${position}%` }} />
      </div>
      <input
        type="range"
        min={0}
        max={100}
        value={position}
        aria-label="원본과 렌더 비교 위치"
        className={styles.similarityCompareRange}
        onChange={(event) => setPosition(Number(event.target.value))}
      />
    </div>
  )
}

/** run 종료 화면 (§14.2) — 채택·거부·실패를 활성 revision 으로 판별한다. */
function SettledPanel({
  run,
  adopted,
  onReset,
  onRetry,
}: {
  run: SimilarityRun
  adopted: boolean
  onReset: () => void
  onRetry: (runId: string) => void
}) {
  const last = run.evaluations.filter((e) => e.status === 'succeeded').at(-1) ?? null

  return (
    <div className={styles.similarityRun}>
      <p className={styles.similarityStatus} aria-live="polite" data-testid="similarity-settled">
        {run.status === 'failed'
          ? '평가에 실패했습니다'
          : adopted
            ? '개선이 채택되어 장면이 갱신되었습니다'
            : '이전 장면 유지됨'}
      </p>
      {last?.score ? <ScorePanel score={last.score} /> : null}
      {run.status === 'failed' ? (
        <>
          {/* 저장된 렌더 재사용 — 새 캡처 없는 유료 재호출임을 밝힌다 (§14.2) */}
          <Button variant="default" data-testid="similarity-retry" onClick={() => onRetry(run.id)}>
            저장된 이미지로 다시 시도
          </Button>
          <p className={styles.similarityHint}>새 캡처 없이 유료 평가 1회를 다시 보냅니다.</p>
        </>
      ) : null}
      <Button variant="outline" data-testid="similarity-reset" onClick={onReset}>
        다시 비교
      </Button>
    </div>
  )
}

function ScorePanel({ score }: { score: SimilarityScore }) {
  return (
    <>
      <p className={styles.similarityOverall} data-testid="similarity-overall">
        {score.overall}
        <span className={styles.similarityOverallUnit}>/100</span>
      </p>

      <ul className={styles.similarityDims}>
        {score.dimensions.map((dimension) => (
          <li
            key={dimension.kind}
            className={styles.similarityDim}
            data-testid="similarity-dimension"
          >
            {/* 숫자·bar·텍스트를 함께 — 색만으로 상태를 말하지 않는다 (§14.1) */}
            <span className={styles.similarityDimLabel}>{DIMENSION_LABELS[dimension.kind]}</span>
            <span className={styles.similarityDimBar}>
              <span className={styles.similarityDimFill} style={{ width: `${dimension.score}%` }} />
            </span>
            <span className={styles.similarityDimScore}>{dimension.score}</span>
          </li>
        ))}
      </ul>
    </>
  )
}

function statusLine(run: SimilarityRun, applying: boolean): string {
  if (applying) return '후보 렌더를 준비하는 중…'

  switch (run.status) {
    case 'evaluating':
      return '유사도 평가 중…'
    case 'readyForAdjustment':
      return '평가 완료 — 보정을 선택할 수 있습니다'
    case 'awaitingRender':
      return '후보 렌더를 기다리는 중…'
    case 'completed':
      return '비교가 완료되었습니다'
    case 'failed':
      return '평가에 실패했습니다'
    case 'canceled':
      return '비교가 취소되었습니다'
  }
}
