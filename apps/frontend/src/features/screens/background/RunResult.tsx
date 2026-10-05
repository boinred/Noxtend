/**
 * Design Ref: §5.2 결과 단계 — 장면 명세 · 파츠 오버레이 · **파츠 제작 리스트** · 사용량.
 *
 * 사이클 #7 에서 무게 중심이 옮겨간다. #5 는 파츠 **명세**(서술·분류·좌표·깊이·가림)를
 * 보여줬는데 그 전부가 글자였다. 이제 그 자리에 그림이 온다 — **사용자가 처음으로
 * 결과물다운 결과물을 본다.**
 *
 * 명세는 각 파츠 정보에서 필요할 때만 팝업으로 연다. 좌표와 가림은 조립 단계의 재료이지
 * 지금 사용자가 매번 볼 것이 아니다.
 */
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { useProviderMeshModels, useProviders } from '@/app/queries/useProviders'
import { canAddMeshProduction, meshReadyPartCount } from '@/domain/job/types'
import { estimateMeshBatchCost, formatUsd } from '@/domain/tuning/usage'
import { ModelSelect } from './ModelSelect'
import { ProviderSelect } from './ProviderSelect'
import { PartGallery, partRowId } from './PartGallery'
import { PartsOverlay } from './PartsOverlay'
import { ScenePanel } from './ScenePanel'
import { UsagePanel } from './UsagePanel'
import { backgroundStyles as styles } from './backgroundStyles'
import type { Job, ViewDirection } from '@/domain/job/types'
import type { ReplanPartMeshSelection } from './PartGallery'
import type { MeshUsage } from '@/domain/tuning/usage'
import type { LlmCall, ModelPrice } from '@/domain/tuning/types'

export interface RunResultProps {
  job: Job
  onRestart: () => void
  /** 진행 중 부분 결과 — 새 유료 실행 동작을 숨긴다. */
  live?: boolean
  /**
   * 끝난 작업에 3D 를 붙인다 (사이클 #11).
   *
   * 없으면 줄 자체가 안 그려진다 — 3D 공급자가 등록되지 않은 환경이 그렇다.
   */
  onAddMesh?: (selection: { providerConfigId: string; model: string }) => void
  addingMesh?: boolean
  /** 공정별 사용량 (FR-18). 아직 안 왔으면 빈 배열이고 패널이 통째로 빠진다 */
  calls?: LlmCall[]
  /** 3D 결과의 시점별 비용 계산에 사용하는 모델 단가표. */
  prices?: ModelPrice[]
  onRetryTask?: (taskId: string) => void
  retryingTaskId?: string | null
  onGenerateViews?: (directions: ViewDirection[]) => Promise<unknown> | void
  onReturnToDescriptions?: (partId: string) => Promise<unknown> | void
  onReplanPartMesh?: (selection: ReplanPartMeshSelection) => Promise<unknown>
}

export function RunResult({
  job,
  onRestart,
  live = false,
  onAddMesh,
  addingMesh = false,
  calls = [],
  prices = [],
  onRetryTask,
  retryingTaskId = null,
  onGenerateViews,
  onReturnToDescriptions,
  onReplanPartMesh,
}: RunResultProps) {
  /**
   * 오버레이에서 파츠 상세로 내려보낸다.
   *
   * **도착한 줄을 잠깐 밝힌다.** 없으면 스크롤이 끝난 뒤 어느 줄로 왔는지 모른다.
   * 같은 줄을 연속으로 눌러도 다시 반응하도록 reflow 를 한 번 강제한다.
   */
  const jumpToPart = (partId: string) => {
    const row = document.getElementById(partRowId(partId))
    if (row === null) return

    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    row.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'center' })

    document.querySelectorAll('[data-landed]').forEach((el) => el.removeAttribute('data-landed'))
    void row.offsetWidth
    row.dataset.landed = 'true'
  }

  return (
    <div data-testid="run-result">
      {job.scene ? <ScenePanel scene={job.scene} /> : null}

      {/* 좌표를 원본 위에 겹쳐야 사람이 맞는지 판단할 수 있다 (§2.3-7) */}
      <PartsOverlay
        sourceImageId={job.sourceImageId}
        parts={job.parts.map((part) => ({
          ...part,
          label: `${part.depthOrder}. ${part.name}`,
        }))}
        scene={job.scene}
        action={{ label: '이동', onClick: jumpToPart }}
      />

      <PartGallery
        job={job}
        onRetry={onRetryTask ?? (() => {})}
        retryingTaskId={retryingTaskId}
        onGenerateViews={onGenerateViews}
        onReturnToDescriptions={onReturnToDescriptions}
        onReplanPartMesh={onReplanPartMesh}
      />

      {/* 스튜디오는 접힌 채로 둔다 (D-14) — 궁금한 사람만 편다 */}
      <UsagePanel
        calls={calls}
        meshes={meshUsageFrom(job)}
        prices={prices}
        partNameByTaskId={partNameByTaskId(job)}
      />

      {!live && onAddMesh && canAddMeshProduction(job) ? (
        <MeshBackfillRow
          count={meshReadyPartCount(job)}
          prices={prices}
          onAdd={onAddMesh}
          pending={addingMesh}
        />
      ) : null}

      {live ? null : (
        <div className={styles.actions}>
          {/* 전체 재실행 — 완료 결과에서 텍스트 모델을 바꿔 장면부터 다시 보는 동작 */}
          <Button onClick={onRestart} data-testid="restart-analysis">
            다시 분석
          </Button>
        </div>
      )}
    </div>
  )
}

/**
 * 3D 를 뒤늦게 붙이는 줄 (사이클 #11 §7.2).
 *
 * **이미지를 다시 만들지 않는다는 것이 이 줄의 전부다.** 전체 재실행은 비용의 99.5% 가
 * 이미 갖고 있는 이미지인데, 여기서는 3D 크레딧만 나간다.
 *
 * 버튼은 유료 호출 수와 설정 단가 기준 예상 총액을 함께 나른다. 실제 크레딧은 공급자가
 * 완료 응답에서 준 값을 사용량 표에 기록한다.
 */
function MeshBackfillRow({
  count,
  prices,
  onAdd,
  pending,
}: {
  count: number
  prices: ModelPrice[]
  onAdd: (selection: { providerConfigId: string; model: string }) => void
  pending: boolean
}) {
  const { meshProviders } = useProviders()
  const [providerId, setProviderId] = useState<string | null>(null)
  const [modelId, setModelId] = useState<string | null>(null)

  // 목록이 오면 첫 항목을 고른다. 사용자가 고른 값이 있으면 건드리지 않는다
  const provider =
    providerId !== null && meshProviders.some((candidate) => candidate.id === providerId)
      ? providerId
      : (meshProviders[0]?.id ?? null)

  const models = useProviderMeshModels(provider)

  const model =
    modelId !== null && models.models.some((candidate) => candidate.id === modelId)
      ? modelId
      : (models.models[0]?.id ?? null)

  const expectedCost = model === null ? null : estimateMeshBatchCost(prices, model, count)

  // 공급자가 없으면 줄 자체를 그리지 않는다 — 고를 수 없는 것을 비활성으로 보여 주면
  // "왜 못 고르지" 를 묻게 된다 (§7.1)
  if (meshProviders.length === 0) {
    return null
  }

  return (
    <div className={styles.meshBackfill} data-testid="mesh-backfill">
      <p className={styles.meshBackfillNote}>
        이미지를 다시 만들지 않고, 4면이 갖춰진 파츠만 3D 로 이어서 만듭니다.
      </p>

      <div className={styles.settingsRow}>
        <ProviderSelect
          providers={meshProviders}
          value={provider}
          onChange={setProviderId}
          label="3D 공급자"
          fieldId="backfill-mesh-provider"
          testId="backfill-mesh-provider"
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
            fieldId="backfill-mesh-model"
            testId="backfill-mesh-model"
          />
        )}

        <Button
          onClick={() =>
            provider && model ? onAdd({ providerConfigId: provider, model }) : undefined
          }
          // 모델을 못 불러온 상태에서 보내면 서버가 거절한다 — 여기서 막는다
          disabled={pending || !provider || !model}
          data-testid="add-mesh"
        >
          {pending
            ? '3D 시작 중'
            : `3D ${count}개 만들기${expectedCost === null ? '' : ` · 예상 ${formatUsd(expectedCost)}`}`}
        </Button>
      </div>
    </div>
  )
}

/** 완료된 파츠의 최신 3D 결과를 사용량 계산 입력으로 바꾼다. */
function meshUsageFrom(job: Job): MeshUsage[] {
  const meshModel = job.models.mesh?.model
  if (meshModel === undefined) return []

  return job.parts.flatMap((part) => {
    if (part.generatedMesh === null) return []

    // 재시도는 공정 행을 늘리지 않지만, 방어적으로 가장 나중 공정을 결과와 연결한다.
    const task = job.tasks
      .filter((candidate) => candidate.kind === 'reconstruct' && candidate.partId === part.id)
      .at(-1)

    return [
      {
        id: part.generatedMesh.id,
        taskId: task?.id ?? `mesh-${part.id}`,
        partName: part.name,
        model: meshModel,
        creditsConsumed: part.generatedMesh.creditsConsumed,
        createdAt: part.generatedMesh.createdAt,
      },
    ]
  })
}

/**
 * 사용량 표에 파츠 이름을 붙이기 위한 대응표.
 *
 * 내역은 공정 id 만 갖는다 — 파츠 id 를 함께 담지 않은 것은 공정이 이미 파츠를
 * 가리키기 때문이다 (백엔드 §3.1). 두 곳에 두면 재생성 때 어긋난다.
 */
function partNameByTaskId(job: Job): Record<string, string> {
  const nameByPart = new Map(job.parts.map((part) => [part.id, part.name]))
  const result: Record<string, string> = {}

  for (const task of job.tasks) {
    if (task.partId !== null) {
      const name = nameByPart.get(task.partId)
      if (name !== undefined) {
        result[task.id] = name
      }
    }
  }

  return result
}
