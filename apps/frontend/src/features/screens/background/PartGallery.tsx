/**
 * 파츠 제작 파이프라인 리스트 — 파츠에서 4방향 이미지와 3D 에셋으로 이어지는 자리.
 *
 * Design Ref: §5.2 · §5.4 · Plan FR-15
 *
 * 파츠 명세 표를 대체한다. 이전에는 이름·분류·서술이 전부 글자였는데, 이제 그 자리에
 * 그림이 온다 — 이것이 사이클 #7 의 목적이다.
 *
 * **실패한 행이 성공한 행과 나란히 있다** (Plan D-3). 비싼 성공분을 버리지 않으려면
 * 부분 실패가 결과 화면에 그대로 보여야 하고, 실패한 것만 다시 돌릴 수 있어야 한다.
 */
import { useEffect, useRef, useState } from 'react'
import { TopLayerBoundary } from '@/lib/top-layer-boundary'
import { Button } from '@/components/ui/button'
import { generatedImageUrl } from '@/app/queries/media'
import { generationTally, jobStatusLabel, meshState, partCards } from '@/domain/job/types'
import { useProviderMeshModels, useProviders } from '@/app/queries/useProviders'
import { apiErrorMessage } from '@/app/queries/errors'
import { backgroundStyles as styles } from './backgroundStyles'
import { MeshTile } from './MeshTile'
import { ModelSelect } from './ModelSelect'
import { ProviderSelect } from './ProviderSelect'
import { formatFailureReason, meshFailureMessage } from './failureMessages'
import type { Job, MeshState, PartCard, PartViewCard, ViewDirection } from '@/domain/job/types'
import type { BackPlan, LeftRightPlan } from '@/app/queries/useJob'

/** 파츠의 정면 이미지가 완료되어 비정면 뷰 생성을 추가 요청할 수 있는지 여부 */
export function canRequestSelectedView(card: PartCard, viewDirection: ViewDirection): boolean {
  if (viewDirection === 'front') return false
  const frontView = card.views.find((v) => v.viewDirection === 'front')
  const currentView = card.views.find((v) => v.viewDirection === viewDirection)
  return frontView?.imageId !== null && currentView?.status === 'unplanned'
}

/** 4면 미만(2~3면) 입력으로 인해 3D 텍스처 퀄리티 하락 경고 뱃지를 띄워야 하는지 여부 */
export function hasPartialViewsWarning(card: PartCard): boolean {
  const readyCount = card.views.filter((v) => v.imageId !== null).length
  return readyCount >= 2 && readyCount < 4
}

/**
 * 파츠별 "3D 전송 뷰 자유 선택 + 대칭" 체크박스 상태 (spec 20260917).
 *
 * DB에 저장하지 않는다 — "3D 생성" 클릭 시점 값만 요청에 실어 보낸다.
 */
export interface PartMeshAxisSelection {
  /** 정면도 이번 스펙부터 껐다 켤 수 있다 — 꺼지면 이 파츠는 이번 3D 생성에서 통째로 빠진다 */
  frontUsed: boolean
  leftUsed: boolean
  rightUsed: boolean
  backUsed: boolean
  /** Left/Right 카드 중 어느 쪽 "좌우대칭"이 켜져 있는지 — 상호 배타적 */
  mirrorLeftRight: 'none' | 'left' | 'right'
  mirrorBack: boolean
}

/** 이미지가 있으면 기본 켜짐, 대칭은 꺼진 초기 상태 (사용자 확정: 실수 비용보다 편의 우선). */
export function defaultAxisSelection(card: PartCard): PartMeshAxisSelection {
  const has = (direction: ViewDirection) =>
    card.views.find((v) => v.viewDirection === direction)?.imageId !== null

  return {
    frontUsed: has('front'),
    leftUsed: has('left'),
    rightUsed: has('right'),
    backUsed: has('back'),
    mirrorLeftRight: 'none',
    mirrorBack: false,
  }
}

/**
 * 정면이 꺼진 채 다른 축이 켜져 있으면 유효성 오류 (spec 20260917 프론트 UI 설계).
 *
 * **이 조합의 유일한 방어가 여기다(merge-gate 2차 리뷰 B2).** `ReplanPartMeshRequest`
 * 에는 "정면 사용 여부" 필드가 없다 — 서버는 정면 이미지가 DB에 있는지만 본다. 프론트가
 * 이 파츠를 배치 대상에서 통째로 빼지 않으면, 서버는 이 조합을 인지할 방법이 없다
 * (정면 이미지가 있으면 다른 값만 와도 정면을 포함해 정상 접수된다).
 */
export function axisSelectionError(selection: PartMeshAxisSelection): string | null {
  if (selection.frontUsed) return null

  const othersOn =
    selection.leftUsed ||
    selection.rightUsed ||
    selection.backUsed ||
    selection.mirrorLeftRight !== 'none' ||
    selection.mirrorBack

  return othersOn ? '정면 없이는 다른 방향만 보낼 수 없어요' : null
}

/**
 * 체크박스 상태 → 서버가 받는 LeftRightPlan/BackPlan (spec 20260917).
 *
 * 대칭이 켜져 있으면 "사용" 체크 값과 무관하게 대칭이 우선한다 — UI가 대칭 켜질 때
 * 반대쪽 "사용"을 자동 비활성화하므로 실제로는 충돌이 없지만, 이 함수 자체는 그
 * 전제 없이도 대칭을 우선하도록 짜서 호출부가 어떤 상태를 넘기든 안전하다.
 */
export function deriveReplanPlans(selection: PartMeshAxisSelection): {
  leftRight: LeftRightPlan
  back: BackPlan
} {
  let leftRight: LeftRightPlan
  if (selection.mirrorLeftRight === 'left') {
    leftRight = 'mirrorFromLeft'
  } else if (selection.mirrorLeftRight === 'right') {
    leftRight = 'mirrorFromRight'
  } else if (selection.leftUsed && selection.rightUsed) {
    leftRight = 'both'
  } else if (selection.leftUsed) {
    leftRight = 'left'
  } else if (selection.rightUsed) {
    leftRight = 'right'
  } else {
    leftRight = 'skip'
  }

  const back: BackPlan = selection.mirrorBack
    ? 'mirrorFromFront'
    : selection.backUsed
      ? 'include'
      : 'skip'

  return { leftRight, back }
}

const VIEW_LABELS: Record<ViewDirection, string> = {
  front: '정면',
  right: '우측',
  back: '후면',
  left: '좌측',
}

type StatusActivityKind = 'running' | 'queued' | 'idle' | 'failed'

interface StatusMeta {
  activity: StatusActivityKind
  title: string
  detail: string
  busy: boolean
}

/** 배치로 보낼 한 파츠의 3D 전송 뷰 선택 (spec 20260917). */
export interface ReplanPartMeshSelection {
  partId: string
  providerConfigId: string
  model: string
  leftRight: LeftRightPlan
  back: BackPlan
}

export interface PartGalleryProps {
  job: Job
  /** 실패한 공정 하나를 다시 돌린다. 진행 중이면 비활성 */
  onRetry: (taskId: string) => void
  retryingTaskId: string | null
  onGenerateViews?: (directions: ViewDirection[]) => Promise<unknown> | void
  onReturnToDescriptions?: (partId: string) => Promise<unknown> | void
  /** 파츠별 "3D 전송 뷰 자유 선택 + 대칭" 배치 액션 (spec 20260917) — 파츠마다 한 번씩 호출된다 */
  onReplanPartMesh?: (selection: ReplanPartMeshSelection) => Promise<unknown>
}

export function PartGallery({
  job,
  onRetry,
  retryingTaskId,
  onGenerateViews,
  onReturnToDescriptions,
  onReplanPartMesh,
}: PartGalleryProps) {
  const cards = partCards(job)
  const tally = generationTally(job)
  const [preview, setPreview] = useState<{
    card: PartCard
    initialDirection: ViewDirection
  } | null>(null)
  const [specification, setSpecification] = useState<PartCard | null>(null)
  const [generatingDirections, setGeneratingDirections] = useState<ViewDirection[]>([])
  const [returningPartId, setReturningPartId] = useState<string | null>(null)

  // 파츠별 체크박스 상태 — 사용자가 손댄 파츠만 여기 남고, 나머지는 매 렌더마다
  // defaultAxisSelection(카드의 현재 이미지 보유 상태)으로 다시 계산된다
  const [axisSelections, setAxisSelections] = useState<Record<string, PartMeshAxisSelection>>({})
  const [meshError, setMeshError] = useState<string | null>(null)
  const [submittingMesh, setSubmittingMesh] = useState(false)

  const axisSelectionFor = (card: PartCard): PartMeshAxisSelection =>
    axisSelections[card.part.id] ?? defaultAxisSelection(card)

  const updateAxisSelection = (partId: string, next: PartMeshAxisSelection) =>
    setAxisSelections((prev) => ({ ...prev, [partId]: next }))

  // // 비정면 선택 생성 요청 처리
  const handleGenerateViews = async (directions: ViewDirection[]) => {
    if (!onGenerateViews) return
    try {
      setGeneratingDirections(directions)
      await onGenerateViews(directions)
    } finally {
      setGeneratingDirections([])
    }
  }

  // // 서술 수정 후 정면 재시도 복귀 요청 처리
  const handleReturnToDescriptions = async (partId: string) => {
    if (!onReturnToDescriptions) return
    try {
      setReturningPartId(partId)
      await onReturnToDescriptions(partId)
    } finally {
      setReturningPartId(null)
    }
  }

  // 정면 사용이 켜지고 유효성 오류가 없는 파츠 — "3D 생성" 배치의 대상
  const eligibleMeshCards = cards.filter((card) => {
    const selection = axisSelectionFor(card)
    return selection.frontUsed && axisSelectionError(selection) === null
  })
  const hasMeshSelectionError = cards.some(
    (card) => axisSelectionError(axisSelectionFor(card)) !== null,
  )

  // // 파츠별 3D 전송 뷰 선택 + 대칭 배치 제출 — 정면 사용이 켜진 파츠만 대상
  const handleReplanPartMesh = async (providerConfigId: string, model: string) => {
    if (!onReplanPartMesh || eligibleMeshCards.length === 0) return

    setMeshError(null)
    setSubmittingMesh(true)
    // 몇 번째 파츠까지 접수됐는지 오류 메시지에 남긴다 — 순차 제출 중간에 실패하면
    // 앞선 파츠들은 이미 서버에 접수된 상태라, "실패했습니다" 한 줄만으로는 사용자가
    // 어디까지 됐는지 알 수 없다(merge-gate 2차 리뷰 B5)
    let submittedCount = 0
    try {
      // RowVersion 낙관적 동시성 충돌을 피하려고 같은 잡에 대한 요청을 순차로 보낸다
      for (const card of eligibleMeshCards) {
        const { leftRight, back } = deriveReplanPlans(axisSelectionFor(card))
        await onReplanPartMesh({ partId: card.part.id, providerConfigId, model, leftRight, back })
        submittedCount += 1
      }
    } catch (error) {
      const reason = apiErrorMessage(error, '3D 생성 요청에 실패했습니다')
      const failedCard = eligibleMeshCards[submittedCount]
      setMeshError(
        submittedCount > 0
          ? `${submittedCount}/${eligibleMeshCards.length}개 파츠는 이미 접수됐고, ` +
              `"${failedCard?.part.name ?? '다음 파츠'}"에서 실패했습니다: ${reason}`
          : reason,
      )
    } finally {
      setSubmittingMesh(false)
    }
  }

  return (
    <section className={styles.partsSection} data-testid="part-gallery">
      <h2 className={styles.partsTitle}>파츠 제작</h2>

      <div className={styles.tally} data-testid="generation-tally">
        <span>
          파츠 {tally.parts} · 이미지 {tally.generated}/{tally.total}
          {tally.failed > 0 ? ` · 실패 ${tally.failed}` : ''}
        </span>

        {/* 부분 성공은 성공과 다른 색이다 — 뭉개면 사용자가 N장을 다 받았다고 믿는다 */}
        <span
          className={styles.statusBadge}
          data-status={job.status}
          data-testid="job-status-badge"
        >
          {jobStatusLabel(job.status)}
        </span>
      </div>

      {/* 정면 완성 후 비정면을 선택 생성할 수 있다는 것을 처음 보는 사람은 모른다 —
          "생성하기" 버튼이 왜 있는지, 안 눌러도 되는지 미리 알려준다 */}
      <p className={styles.onboardingNotice} data-testid="selective-view-notice">
        정면이 완성된 파츠는 필요한 방향만 골라 "생성하기"를 누르세요. 안 누르면 그 방향은 만들지
        않습니다. 납작한 파츠처럼 옆면·뒷면이 필요 없으면 그대로 둬도 됩니다. 나중에 3D 생성
        단계에서 방향별로 포함 여부를 다시 고를 수 있습니다.
      </p>

      <div className={styles.partPipelineHeader} data-testid="part-pipeline-header">
        <span>파츠</span>
        <span>4방향 이미지</span>
        <span>3D 에셋</span>
      </div>

      {onReplanPartMesh ? (
        <PartMeshBatchSection
          eligibleCount={eligibleMeshCards.length}
          hasSelectionError={hasMeshSelectionError}
          error={meshError}
          pending={submittingMesh}
          onSubmit={handleReplanPartMesh}
        />
      ) : null}

      <ul className={styles.partPipelineList} data-testid="part-pipeline-list">
        {cards.map((card) => (
          <li
            key={card.part.id}
            id={partRowId(card.part.id)}
            className={styles.partRow}
            data-testid="part-row"
          >
            <div className="flex flex-col gap-2">
              <button
                type="button"
                className={styles.partRowInfo}
                aria-label={`${card.part.name} 파츠 명세 보기`}
                aria-haspopup="dialog"
                onClick={() => setSpecification(card)}
              >
                <span className={styles.partRowInfoHeader}>
                  <span className={styles.partCardName}>{card.part.name}</span>
                  {card.part.placements.length > 1 ? (
                    <span
                      className={styles.partCountBadge}
                      title={`장면에 ${card.part.placements.length}개`}
                      data-testid="part-count"
                    >
                      ×{card.part.placements.length}
                    </span>
                  ) : null}
                  <span className={styles.partSpecToggle} aria-hidden="true">
                    명세 보기 ↗
                  </span>
                </span>
                <span className={styles.partCardMeta}>{card.part.category ?? '미분류'}</span>
                {card.part.description ? (
                  <span className={styles.partRowDescription}>{card.part.description}</span>
                ) : null}
              </button>

              {/* // 정면 완성 시 서술 복귀 버튼 제공 */}
              {card.views.find((v) => v.viewDirection === 'front')?.imageId ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-7 text-[11px] text-muted-foreground hover:text-foreground justify-start px-0"
                  disabled={returningPartId === card.part.id}
                  onClick={() => handleReturnToDescriptions(card.part.id)}
                  data-testid="return-to-descriptions"
                >
                  {returningPartId === card.part.id
                    ? '서술 복귀 중…'
                    : '↺ 서술 수정 후 정면 다시 생성'}
                </Button>
              ) : null}
            </div>

            <div>
              {onReplanPartMesh ? (
                <PartMeshAxisControls
                  card={card}
                  selection={axisSelectionFor(card)}
                  onChange={(next) => updateAxisSelection(card.part.id, next)}
                />
              ) : null}
              <PartViews
                card={card}
                onRetry={onRetry}
                onPreview={(view) => setPreview({ card, initialDirection: view.viewDirection })}
                onGenerateViews={handleGenerateViews}
                retryingTaskId={retryingTaskId}
                generatingDirections={generatingDirections}
              />
            </div>
            <Part3dState
              state={meshState(job, card.part, job.tasks)}
              partName={card.part.name}
              card={card}
              onRetry={onRetry}
              retryingTaskId={retryingTaskId}
            />
          </li>
        ))}
      </ul>

      {specification ? (
        <PartSpecificationDialog card={specification} onClose={() => setSpecification(null)} />
      ) : null}

      {preview ? (
        <PartImageCarousel
          card={preview.card}
          initialDirection={preview.initialDirection}
          onRetry={onRetry}
          retryingTaskId={retryingTaskId}
          onClose={() => setPreview(null)}
        />
      ) : null}
    </section>
  )
}

/**
 * 파츠 전체에 공유되는 3D 공급자/모델 선택 + 배치 제출 (spec 20260917).
 *
 * **파츠마다 다시 고르지 않는다.** `MeshBackfillRow`(RunResult.tsx)와 같은 자리·같은
 * 패턴이다 — 여러 파츠에 같은 공급자를 쓰는 게 당연하므로 선택은 한 번뿐이다.
 */
function PartMeshBatchSection({
  eligibleCount,
  hasSelectionError,
  error,
  pending,
  onSubmit,
}: {
  eligibleCount: number
  hasSelectionError: boolean
  error: string | null
  pending: boolean
  onSubmit: (providerConfigId: string, model: string) => void
}) {
  const { meshProviders } = useProviders()
  const [providerId, setProviderId] = useState<string | null>(null)
  const [modelId, setModelId] = useState<string | null>(null)

  const provider =
    providerId !== null && meshProviders.some((candidate) => candidate.id === providerId)
      ? providerId
      : (meshProviders[0]?.id ?? null)

  const models = useProviderMeshModels(provider)

  const model =
    modelId !== null && models.models.some((candidate) => candidate.id === modelId)
      ? modelId
      : (models.models[0]?.id ?? null)

  // 공급자가 없으면 줄 자체를 그리지 않는다 — 고를 수 없는 것을 비활성으로 보여 주면
  // "왜 못 고르지" 를 묻게 된다 (MeshBackfillRow 와 같은 근거)
  if (meshProviders.length === 0) {
    return null
  }

  return (
    <div className={styles.meshBackfill} data-testid="part-mesh-batch">
      <p className={styles.meshBackfillNote}>
        파츠마다 위 체크박스로 고른 뷰만 골라 3D를 만듭니다. 정면을 끄면 그 파츠는 이번 3D 생성에서
        통째로 빠집니다. "대칭"을 켜면 반대쪽 면은 사용 여부와 무관하게 자동으로 반전된 이미지로
        채워집니다.
      </p>

      <div className={styles.settingsRow}>
        <ProviderSelect
          providers={meshProviders}
          value={provider}
          onChange={setProviderId}
          label="3D 공급자"
          fieldId="replan-mesh-provider"
          testId="replan-mesh-provider"
          emptyLabel="3D 생성 공급자"
        />

        {provider === null ? null : (
          <ModelSelect
            models={models.models}
            isLoading={models.isLoading}
            errorMessage={models.errorMessage}
            value={model}
            onChange={setModelId}
            label="3D 모델"
            fieldId="replan-mesh-model"
            testId="replan-mesh-model"
          />
        )}

        <Button
          onClick={() => (provider && model ? onSubmit(provider, model) : undefined)}
          disabled={pending || !provider || !model || eligibleCount === 0 || hasSelectionError}
          data-testid="replan-mesh-submit"
        >
          {pending ? '3D 생성 중…' : `3D ${eligibleCount}개 만들기`}
        </Button>
      </div>

      {hasSelectionError ? (
        <span className={styles.partMeshAxisError} data-testid="replan-mesh-selection-error">
          정면 없이 다른 방향만 켜진 파츠가 있어요. 위에서 먼저 고쳐 주세요.
        </span>
      ) : null}

      {error ? (
        <span className={styles.partMeshAxisError} data-testid="replan-mesh-error">
          {error}
        </span>
      ) : null}
    </div>
  )
}

/** 파츠 한 줄의 "3D 전송 뷰 자유 선택 + 대칭" 체크박스 (spec 20260917). 4방향 이미지 위에 항상 펼쳐진다. */
function PartMeshAxisControls({
  card,
  selection,
  onChange,
}: {
  card: PartCard
  selection: PartMeshAxisSelection
  onChange: (next: PartMeshAxisSelection) => void
}) {
  const hasImage = (direction: ViewDirection) =>
    card.views.find((v) => v.viewDirection === direction)?.imageId !== null

  const hasFront = hasImage('front')
  // 정면이 아예 없으면 3D 자체를 논할 수 없다 — 컨트롤을 그리지 않는다
  if (!hasFront) return null

  const hasLeft = hasImage('left')
  const hasRight = hasImage('right')
  const hasBack = hasImage('back')
  const error = axisSelectionError(selection)

  const setMirrorLeft = (checked: boolean) =>
    onChange({
      ...selection,
      mirrorLeftRight: checked ? 'left' : 'none',
      rightUsed: checked ? false : selection.rightUsed,
    })

  const setMirrorRight = (checked: boolean) =>
    onChange({
      ...selection,
      mirrorLeftRight: checked ? 'right' : 'none',
      leftUsed: checked ? false : selection.leftUsed,
    })

  const setMirrorBack = (checked: boolean) =>
    onChange({ ...selection, mirrorBack: checked, backUsed: checked ? false : selection.backUsed })

  return (
    <div className={styles.partMeshAxis} data-testid="part-mesh-axis">
      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.frontUsed}
          onChange={(event) => onChange({ ...selection, frontUsed: event.target.checked })}
          data-testid="axis-front-used"
        />
        정면 사용
      </label>

      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.leftUsed}
          disabled={!hasLeft || selection.mirrorLeftRight !== 'none'}
          onChange={(event) => onChange({ ...selection, leftUsed: event.target.checked })}
          data-testid="axis-left-used"
        />
        좌측 사용
      </label>
      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.mirrorLeftRight === 'left'}
          disabled={!hasLeft}
          onChange={(event) => setMirrorLeft(event.target.checked)}
          data-testid="axis-mirror-left"
        />
        좌측→우측 대칭
      </label>

      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.rightUsed}
          disabled={!hasRight || selection.mirrorLeftRight !== 'none'}
          onChange={(event) => onChange({ ...selection, rightUsed: event.target.checked })}
          data-testid="axis-right-used"
        />
        우측 사용
      </label>
      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.mirrorLeftRight === 'right'}
          disabled={!hasRight}
          onChange={(event) => setMirrorRight(event.target.checked)}
          data-testid="axis-mirror-right"
        />
        우측→좌측 대칭
      </label>

      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.backUsed}
          disabled={!hasBack || selection.mirrorBack}
          onChange={(event) => onChange({ ...selection, backUsed: event.target.checked })}
          data-testid="axis-back-used"
        />
        후면 사용
      </label>
      <label className={styles.partMeshAxisLabel}>
        <input
          type="checkbox"
          checked={selection.mirrorBack}
          onChange={(event) => setMirrorBack(event.target.checked)}
          data-testid="axis-mirror-back"
        />
        정면→후면 대칭
      </label>

      {error ? (
        <span className={styles.partMeshAxisError} data-testid="axis-selection-error">
          {error}
        </span>
      ) : null}
    </div>
  )
}

/** 이미지 미리보기와 같은 native dialog 기반 파츠 분해 명세. */
function PartSpecificationDialog({ card, onClose }: { card: PartCard; onClose: () => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  // 경계 컨텍스트용 — native dialog 는 전부 경계를 제공한다 (download-view-consistency §2)
  const [dialogElement, setDialogElement] = useState<HTMLDialogElement | null>(null)

  useEffect(() => {
    // native dialog 기반 포커스 격리·Esc 닫기
    dialogRef.current?.showModal()
    setDialogElement(dialogRef.current)
  }, [])

  const close = () => dialogRef.current?.close()

  return (
    <dialog
      ref={dialogRef}
      className={styles.partSpecificationDialog}
      aria-label={`${card.part.name} 파츠 명세`}
      onClose={onClose}
      onClick={(event) => {
        // dialog 외부 영역 클릭 닫기
        if (event.target === event.currentTarget) close()
      }}
    >
      <TopLayerBoundary value={dialogElement}>
        <div className={styles.partSpecificationDialogBody}>
          <div className={styles.partSpecificationDialogHeader}>
            <div className={styles.partSpecificationDialogHeading}>
              <span className={styles.partImageDialogTitle}>{card.part.name}</span>
              <span className={styles.partCardMeta}>{card.part.category ?? '미분류'}</span>
            </div>
            <Button
              type="button"
              variant="secondary"
              size="icon-sm"
              onClick={close}
              aria-label="파츠 명세 닫기"
            >
              <span aria-hidden="true">×</span>
            </Button>
          </div>
          <PartSpecification card={card} />
        </div>
      </TopLayerBoundary>
    </dialog>
  )
}

/**
 * 오버레이의 "이동" 이 찾아오는 자리 (사이클 #9 §8.2.4).
 *
 * 도착한 줄을 잠깐 밝히지 않으면 파츠 아홉 개가 비슷한 모양으로 늘어선 목록에서 어디로
 * 내려왔는지 알 수 없다.
 */
export function partRowId(partId: string): string {
  return `part-row-${partId}`
}

/** 팝업 안에서 확인하는 파츠 분해 명세 전체. */
function PartSpecification({ card }: { card: PartCard }) {
  const { part } = card

  return (
    <dl className={styles.partSpecification} data-testid="part-specification">
      <div className={styles.partSpecificationWide}>
        <dt className={styles.partSpecificationLabel}>설명</dt>
        <dd className={styles.partSpecificationValue}>{part.description ?? '설명 없음'}</dd>
      </div>
      <div className={styles.partSpecificationField}>
        <dt className={styles.partSpecificationLabel}>분류</dt>
        <dd className={styles.partSpecificationValue}>{part.category ?? '미분류'}</dd>
      </div>
      <div className={styles.partSpecificationField}>
        <dt className={styles.partSpecificationLabel}>깊이</dt>
        <dd className={styles.partSpecificationValue}>{part.depthOrder ?? '미지정'}</dd>
      </div>
      <div className={styles.partSpecificationWide}>
        {/*
          배치가 스무 개까지 온다 — 한 문단으로 이으면 어디서 끊기는지 안 보이고,
          다음 항목의 `x` 가 앞 줄 끝에 붙어 `×` 처럼 읽힌다 (사이클 #9)
        */}
        <dt className={styles.partSpecificationLabel}>
          좌표
          {part.placements.length > 1 ? (
            <span className={styles.partSpecificationCount}>{part.placements.length}</span>
          ) : null}
        </dt>
        <dd className={styles.partSpecificationValue}>
          {part.placements.length === 0 ? (
            '미지정'
          ) : (
            <ol className={styles.placementList} data-testid="placement-list">
              {part.placements.map((placement, index) => (
                <li key={index} className={styles.placementRow} data-testid="placement-row">
                  <span className={styles.placementIndex}>{index + 1}</span>x{' '}
                  {placement.x.toFixed(2)} · y {placement.y.toFixed(2)} · w {placement.w.toFixed(2)}{' '}
                  · h {placement.h.toFixed(2)}
                </li>
              ))}
            </ol>
          )}
        </dd>
      </div>
      <div className={styles.partSpecificationWide}>
        <dt className={styles.partSpecificationLabel}>가림</dt>
        <dd className={styles.partSpecificationValue}>
          {part.occludedBy.length > 0 ? part.occludedBy.join(' · ') : '없음'}
        </dd>
      </div>
    </dl>
  )
}

/**
 * 파츠 한 줄의 3D 자리 (§11.3).
 *
 * **뷰어를 만들지 않는다** — 첫 관통은 렌더 이미지와 GLB 내려받기로 확인한다.
 * 브라우저 안의 mesh 편집기는 별도 사이클이다.
 */
function Part3dState({
  state,
  partName,
  card,
  onRetry,
  retryingTaskId,
}: {
  state: MeshState
  partName: string
  card?: PartCard
  onRetry: (taskId: string) => void
  retryingTaskId: string | null
}) {
  const readyImageCount = card ? card.views.filter((v) => v.imageId !== null).length : 0
  const isPartialViewsWarning = readyImageCount >= 2 && readyImageCount < 4

  if (state.kind === 'ready') {
    return (
      <div className={styles.part3dState} data-testid="part-3d-state" data-state="ready">
        <MeshTile mesh={state.mesh} partName={partName} />
        {isPartialViewsWarning ? (
          <span
            className="mt-1 block text-[10px] text-amber-500 font-medium leading-tight"
            data-testid="partial-views-warning"
          >
            ⚠️ 4면 미만 입력 시 일부 방향 텍스처 퀄리티가 하락할 수 있습니다
          </span>
        ) : null}
      </div>
    )
  }

  const status = meshStatusMeta(state)

  return (
    <div className={styles.part3dState} data-testid="part-3d-state" data-state={state.kind}>
      <div
        className={styles.meshStatusCard}
        data-testid="mesh-status-card"
        data-activity={status.activity}
        aria-busy={status.busy || undefined}
      >
        <StatusActivity kind={status.activity} />
        <span className={styles.statusCopy}>
          <span className={styles.statusTitle}>{status.title}</span>
          <span className={styles.statusDetail}>{status.detail}</span>
          {isPartialViewsWarning ? (
            <span
              className="mt-1 block text-[10px] text-amber-500 font-medium leading-tight"
              data-testid="partial-views-warning"
            >
              ⚠️ 4면 미만 선택 (품질 하락 가능)
            </span>
          ) : null}
        </span>

        {state.kind === 'running' ? (
          <div
            className={styles.meshProgressTrack}
            role="progressbar"
            aria-label={`${partName} 3D 제작 진행률`}
            aria-valuenow={state.progress}
            aria-valuemin={0}
            aria-valuemax={100}
          >
            <div className={styles.meshProgressFill} style={{ width: `${state.progress}%` }} />
          </div>
        ) : null}

        {state.kind === 'failed' ? (
          <Button
            size="sm"
            variant="outline"
            onClick={() => onRetry(state.taskId)}
            disabled={retryingTaskId !== null}
            data-testid="mesh-retry"
          >
            {retryingTaskId === state.taskId ? '다시 시도 중' : '다시 시도'}
          </Button>
        ) : null}
      </div>
    </div>
  )
}

/** 3D 상태별 활동성·주요 문구·다음 전이 안내. */
function meshStatusMeta(state: Exclude<MeshState, { kind: 'ready' }>): StatusMeta {
  switch (state.kind) {
    case 'notRequested':
      return {
        activity: 'idle',
        title: '3D 생성 안 함',
        detail: '이미지 결과만 제공',
        busy: false,
      }
    case 'awaitingImages':
      return {
        activity: 'queued',
        title: `이미지 ${state.ready}/4`,
        detail: '4방향 모으는 중',
        busy: true,
      }
    case 'planning':
      return {
        activity: 'running',
        title: '3D 준비 중',
        detail: '작업 구성 중',
        busy: true,
      }
    case 'queued':
      return {
        activity: 'queued',
        title: '대기열',
        detail: '앞 작업 후 자동 시작',
        busy: true,
      }
    case 'running':
      return {
        activity: 'running',
        title: `3D 제작 ${state.progress}%`,
        detail: '모델을 만드는 중',
        busy: true,
      }
    case 'failed':
      return {
        activity: 'failed',
        title: meshFailureMessage(state.failureReason),
        detail: '다시 시도할 수 있어요',
        busy: false,
      }
  }
}

/** 상태 전용 활동 표시 — 실행은 회전, 대기열은 호흡, 나머지는 정적. */
function StatusActivity({ kind }: { kind: StatusActivityKind }) {
  return (
    <span
      className={styles.statusActivity}
      data-testid="status-activity"
      data-status-activity
      data-activity={kind}
      aria-hidden="true"
    >
      {kind === 'failed' ? '!' : kind === 'idle' ? '—' : null}
    </span>
  )
}

/** 이미지 슬롯의 활동 상태와 다음 전이 안내. */
function imageStatusMeta(status: PartViewCard['status']): StatusMeta {
  switch (status) {
    case 'running':
      return {
        activity: 'running',
        title: '그리는 중',
        detail: '이미지 생성 중',
        busy: true,
      }
    case 'pending':
      return {
        activity: 'queued',
        title: '대기열',
        detail: '앞 작업 후 자동 시작',
        busy: true,
      }
    case 'canceled':
      return {
        activity: 'idle',
        title: '취소됨',
        detail: '작업이 중단됐어요',
        busy: false,
      }
    case 'unplanned':
      return {
        activity: 'idle',
        title: '생성 안 함',
        detail: '계획되지 않은 방향',
        busy: false,
      }
    default:
      return {
        activity: 'running',
        title: '결과 연결 중',
        detail: '파일을 준비하고 있어요',
        busy: true,
      }
  }
}

/**
 * 완성된 3D — 미리보기와 내려받기.
 *
 * **공급자 링크를 쓰지 않는다.** 그쪽 URL 은 5분이면 만료되므로 우리 endpoint 로 건다.
 */
/** 파츠 행의 정면·우측·후면·좌측 생성 결과. */
function PartViews({
  card,
  onRetry,
  onPreview,
  onGenerateViews,
  retryingTaskId,
  generatingDirections,
}: {
  card: PartCard
  onRetry: (taskId: string) => void
  onPreview: (view: PartViewCard) => void
  onGenerateViews: (directions: ViewDirection[]) => void
  retryingTaskId: string | null
  generatingDirections: ViewDirection[]
}) {
  const frontImageReady = card.views.find((v) => v.viewDirection === 'front')?.imageId !== null

  return (
    <div className={styles.partViews}>
      {card.views.map((view) => (
        <div key={view.viewDirection} className={styles.partView} data-testid="part-view">
          <span className={styles.partViewLabel} data-testid="part-view-label">
            {VIEW_LABELS[view.viewDirection]}
          </span>
          <PartVisual
            partName={card.part.name}
            view={view}
            frontImageReady={frontImageReady}
            onRetry={onRetry}
            onPreview={() => onPreview(view)}
            onGenerateView={() => onGenerateViews([view.viewDirection])}
            isRetrying={view.task?.id === retryingTaskId}
            isGenerating={generatingDirections.includes(view.viewDirection)}
          />
        </div>
      ))}
    </div>
  )
}

/** 방향 타일의 이미지·실패·진행 상태. */
function PartVisual({
  partName,
  view,
  frontImageReady,
  onRetry,
  onPreview,
  onGenerateView,
  isRetrying,
  isGenerating,
}: {
  partName: string
  view: PartViewCard
  frontImageReady?: boolean
  onRetry: (taskId: string) => void
  onPreview: () => void
  onGenerateView?: () => void
  isRetrying: boolean
  isGenerating?: boolean
}) {
  if (view.imageId !== null) {
    return (
      <button
        type="button"
        className={styles.partCardImageButton}
        onClick={onPreview}
        aria-label={`${partName} ${VIEW_LABELS[view.viewDirection]} 이미지 크게 보기`}
        data-testid="part-image-open"
      >
        <img
          className={styles.partCardImage}
          src={generatedImageUrl(view.imageId)}
          alt={`${partName} ${VIEW_LABELS[view.viewDirection]} 생성 이미지`}
          data-testid="part-image"
        />
        <span className={styles.partCardImageHint} aria-hidden="true">
          확대
        </span>
      </button>
    )
  }

  // // 정면 생성이 완료되고 비정면이 미계획(unplanned) 상태일 때 생성 버튼 제공
  if (
    view.status === 'unplanned' &&
    view.viewDirection !== 'front' &&
    frontImageReady &&
    onGenerateView
  ) {
    return (
      <div className={styles.partCardPlaceholder} data-testid="part-unplanned-action">
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={isGenerating}
          onClick={onGenerateView}
          data-testid="generate-view-button"
        >
          {isGenerating ? '생성 요청 중…' : '생성하기'}
        </Button>
      </div>
    )
  }

  if (view.status === 'failed') {
    return (
      <div className={styles.partCardFailed} data-testid="part-failed">
        <span aria-hidden="true">⚠</span>
        <span>{formatFailureReason(view.failureReason, '생성 실패')}</span>
        {view.task !== null ? (
          <Button
            size="sm"
            variant="outline"
            disabled={isRetrying}
            onClick={() => onRetry(view.task!.id)}
            data-testid="part-retry"
          >
            {isRetrying ? '다시 거는 중…' : '다시'}
          </Button>
        ) : null}
      </div>
    )
  }

  const status = imageStatusMeta(view.status)

  return (
    <div
      className={styles.partCardPlaceholder}
      data-testid="part-pending"
      data-activity={status.activity}
      aria-busy={status.busy || undefined}
    >
      <StatusActivity kind={status.activity} />
      <span className={styles.statusCopy}>
        <span className={styles.statusTitle}>{status.title}</span>
        <span className={styles.statusDetail}>{status.detail}</span>
      </span>
    </div>
  )
}

/** 정면·우측·후면·좌측 고정 순서의 원본 이미지 Carousel. */
function PartImageCarousel({
  card,
  initialDirection,
  onRetry,
  retryingTaskId,
  onClose,
}: {
  card: PartCard
  initialDirection: ViewDirection
  onRetry: (taskId: string) => void
  retryingTaskId: string | null
  onClose: () => void
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const initialIndex = Math.max(
    0,
    card.views.findIndex((view) => view.viewDirection === initialDirection),
  )
  const [currentIndex, setCurrentIndex] = useState(initialIndex)
  const currentView = card.views[currentIndex]!

  // 경계 컨텍스트용 (download-view-consistency §2)
  const [dialogElement, setDialogElement] = useState<HTMLDialogElement | null>(null)

  useEffect(() => {
    // native dialog 기반 포커스 격리·Esc 닫기
    dialogRef.current?.showModal()
    setDialogElement(dialogRef.current)
  }, [])

  const close = () => dialogRef.current?.close()
  const move = (offset: number) => {
    // 네 방향 수평 회전 순서의 순환 이동
    setCurrentIndex((index) => (index + offset + card.views.length) % card.views.length)
  }

  return (
    <dialog
      ref={dialogRef}
      className={styles.partImageDialog}
      aria-label={`${card.part.name} 이미지 크게 보기`}
      aria-roledescription="carousel"
      onClose={onClose}
      onKeyDown={(event) => {
        // 키보드 Carousel 이동과 native Esc 닫기의 역할 분리
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault()
          move(event.key === 'ArrowRight' ? 1 : -1)
        }
      }}
      onClick={(event) => {
        // dialog 외부 영역 클릭 닫기
        if (event.target === event.currentTarget) close()
      }}
    >
      <TopLayerBoundary value={dialogElement}>
        <div className={styles.partImageDialogBody}>
          <div className={styles.partImageDialogHeader}>
            <div className={styles.partImageDialogHeading}>
              <span className={styles.partImageDialogTitle}>{card.part.name}</span>
              <span
                className={styles.partCarouselPosition}
                data-testid="part-carousel-position"
                aria-live="polite"
              >
                {VIEW_LABELS[currentView.viewDirection]} · {currentIndex + 1}/{card.views.length}
              </span>
            </div>
            <Button
              type="button"
              variant="secondary"
              size="icon-sm"
              onClick={close}
              aria-label="미리보기 닫기"
            >
              <span aria-hidden="true">×</span>
            </Button>
          </div>

          {/* 현재 방향 원본과 순환 탐색 버튼 */}
          <div className={styles.partCarouselStage}>
            <Button
              type="button"
              variant="secondary"
              size="icon"
              className={styles.partCarouselArrow}
              onClick={() => move(-1)}
              aria-label="이전 방향"
            >
              <span aria-hidden="true">←</span>
            </Button>

            <CarouselVisual
              partName={card.part.name}
              view={currentView}
              onRetry={onRetry}
              isRetrying={currentView.task?.id === retryingTaskId}
            />

            <Button
              type="button"
              variant="secondary"
              size="icon"
              className={styles.partCarouselArrow}
              onClick={() => move(1)}
              aria-label="다음 방향"
            >
              <span aria-hidden="true">→</span>
            </Button>
          </div>

          {/* 네 방향 고정 슬롯과 직접 이동 */}
          <div className={styles.partCarouselThumbs}>
            {card.views.map((view, index) => (
              <button
                key={view.viewDirection}
                type="button"
                className={styles.partCarouselThumb}
                aria-current={index === currentIndex}
                aria-label={`${VIEW_LABELS[view.viewDirection]} 보기`}
                onClick={() => setCurrentIndex(index)}
                data-testid="part-carousel-thumb"
              >
                {view.imageId ? (
                  <img
                    className={styles.partCarouselThumbImage}
                    src={generatedImageUrl(view.imageId)}
                    alt=""
                  />
                ) : (
                  <span className={styles.partCarouselThumbEmpty} aria-hidden="true">
                    {view.status === 'failed' ? '!' : '—'}
                  </span>
                )}
                <span>{VIEW_LABELS[view.viewDirection]}</span>
              </button>
            ))}
          </div>
        </div>
      </TopLayerBoundary>
    </dialog>
  )
}

/** Carousel 현재 방향의 이미지·실패·대기 상태. */
function CarouselVisual({
  partName,
  view,
  onRetry,
  isRetrying,
}: {
  partName: string
  view: PartViewCard
  onRetry: (taskId: string) => void
  isRetrying: boolean
}) {
  if (view.imageId) {
    return (
      <img
        className={styles.partImagePreview}
        src={generatedImageUrl(view.imageId)}
        alt={`${partName} ${VIEW_LABELS[view.viewDirection]} 확대 이미지`}
        data-testid="part-image-preview"
      />
    )
  }

  if (view.status === 'failed') {
    return (
      <div className={styles.partCarouselFailed} data-testid="part-carousel-failed">
        <span className={styles.partCarouselFailedMark} aria-hidden="true">
          !
        </span>
        <span className={styles.partCarouselFailedTitle}>
          {VIEW_LABELS[view.viewDirection]} 이미지 생성 실패
        </span>
        <span>{formatFailureReason(view.failureReason, '생성 실패')}</span>
        {view.task ? (
          <Button
            size="sm"
            variant="outline"
            disabled={isRetrying}
            onClick={() => onRetry(view.task!.id)}
          >
            {isRetrying ? '다시 생성 중…' : '이 방향 다시 생성'}
          </Button>
        ) : null}
      </div>
    )
  }

  return (
    <div className={styles.partCarouselPending}>
      {VIEW_LABELS[view.viewDirection]} · {imageStatusMeta(view.status).title}
    </div>
  )
}
