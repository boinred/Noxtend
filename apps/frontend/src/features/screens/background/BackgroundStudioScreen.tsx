/**
 * Design Ref: §5.1 · §5.2 · FR-13 — 배경 스튜디오.
 *
 * **URL 이 작업의 주소다.** 진행 상태를 컴포넌트가 들고 있으면 새로고침 한 번에
 * 10분짜리 작업을 잃는다 (R-9). `jobId` 가 경로에 있으므로 이 화면은 상태 머신이
 * 아니라 **URL 의 함수**다 — 진입 경로가 무엇이든 같은 화면이 나온다.
 *
 * 단계 전환도 그래서 setState 가 아니라 navigate 다.
 */
import { useState } from 'react'
import { lazy, Suspense } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageContainer } from '@/features/screens/PageContainer'
import { Button } from '@/components/ui/button'
import {
  useAddMeshProduction,
  useCancelJob,
  useGenerateSelectedViews,
  useJob,
  useReplanPartMesh,
  useRetryTask,
  useReturnToDescriptions,
  useStartJob,
} from '@/app/queries/useJob'
import { useJobCalls, useModelPrices } from '@/app/queries/useTuning'
import { modelPriceHint } from '@/domain/tuning/usage'
import {
  useProviders,
  useProviderModels,
  useProviderImageModels,
  useProviderMeshModels,
} from '@/app/queries/useProviders'
import { useUpload } from '@/app/queries/useUpload'
import { apiErrorMessage } from '@/app/queries/errors'
import {
  ROUTES,
  backgroundJobPath,
  backgroundWithImagePath,
  readModelSelection,
} from '@/routes/paths'
import { ModeTabs } from './ModeTabs'
import { ModelSummary } from './ModelSummary'
import { PromptModePanel } from './PromptModePanel'
import { ImageDropzone } from './ImageDropzone'
import { ProviderSelect } from './ProviderSelect'
import { ModelSelect } from './ModelSelect'
import { RunProgress } from './RunProgress'
import { StageProgress } from './StageProgress'
import { RunResult } from './RunResult'
import { ReviewGate } from './ReviewGate'
import { LiveActionBar } from './LiveActionBar'
import { BackgroundPipelinePreview } from './BackgroundPipelinePreview'
import { BackgroundPipelineStepper } from './BackgroundPipelineStepper'
import { backgroundStyles as styles } from './backgroundStyles'
import { formatFailureReason } from './failureMessages'
import { canOpenSceneTab, hasResultToShow, isActive } from '@/domain/job/types'
import { StudioViewTabs } from './StudioViewTabs'
import { SimilarityInspector } from './SimilarityInspector'
import type { SceneCaptureHandle } from './SceneAssemblyView'
import { useSceneLayout } from '@/app/queries/useSceneLayout'
import type { StudioMode } from './ModeTabs'

export function BackgroundStudioScreen() {
  const { jobId } = useParams<{ jobId: string }>()

  // jobId 가 있으면 진행/결과, 없으면 입력. 화면이 URL 의 함수라는 것이 이 분기다
  return jobId ? <JobView jobId={jobId} /> : <InputView />
}

/** 입력 단계 — 업로드 · 공급자 선택 · 분석 시작. 폼이므로 `form`(720) 이다 (§5.0). */
function InputView() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const {
    textProviders,
    imageProviders,
    meshProviders,
    isLoading,
    error: providersError,
    refetch: refetchProviders,
  } = useProviders()
  const upload = useUpload()
  const startJob = useStartJob()

  /**
   * "다시 분석" 이 넘긴 이미지.
   *
   * 같은 이미지로 프롬프트만 바꿔 돌리는 것이 튜닝의 기본 동작인데, 매번 파일을
   * 다시 고르게 하면 그 흐름이 끊긴다. 이미 서버에 있으므로 다시 올릴 이유도 없다.
   */
  const reusedImageId = params.get('image')

  /**
   * "다시 시도" 가 넘긴 모델 선택.
   *
   * 첫 렌더의 초기값으로만 쓴다 — 이후 사용자가 고른 값이 이긴다. 아래 폴백 규칙이
   * 그대로 적용되므로, 이어받은 공급자가 지워졌거나 사용 중지됐으면 목록 첫 항목으로
   * 내려앉는다.
   */
  const carried = readModelSelection(params)

  const [mode, setMode] = useState<StudioMode>('image')
  const [file, setFile] = useState<File | null>(null)
  const [providerId, setProviderId] = useState<string | null>(
    carried.text?.providerConfigId ?? null,
  )
  const [modelId, setModelId] = useState<string | null>(carried.text?.model ?? null)
  const [imageProviderId, setImageProviderId] = useState<string | null>(
    carried.image?.providerConfigId ?? null,
  )
  const [imageModelId, setImageModelId] = useState<string | null>(carried.image?.model ?? null)
  // 다시 시도 URL 이 3D 선택도 나른다 — 같은 이미지로 모델만 바꿔 비교할 때
  // 통제 변수가 깨지지 않게 한다
  const [meshProviderId, setMeshProviderId] = useState<string | null>(
    carried.mesh?.providerConfigId ?? null,
  )
  const [meshModelId, setMeshModelId] = useState<string | null>(carried.mesh?.model ?? null)
  // 검수 게이트 opt-in (review-gate §목표) — 전 카테고리 공통(D-01)
  // **기본값이 켬이다.** 끄고 접수하면 분해 직후 파츠 수 × 4 장이 곧바로 나간다 —
  // 파츠 26개면 이미지 104장이다. 깜빡한 대가가 한쪽으로만 크므로 안전한 쪽을 기본으로 둔다.
  // URL 에 넘겨받은 선택이 있으면 그 값을 따른다
  const [requiresReview, setRequiresReview] = useState(carried.requiresReview ?? true)
  // 3D 팬아웃 opt-in — **기본은 꺼짐**. 켜면 공급자·모델이 접수에 실려 나가고,
  // 파츠의 4방향이 모이는 대로 3D 가 자동으로 돈다(파츠당 약 $0.40).
  // 이어받기(FR-08)는 예외 — 이전 작업이 3D 를 골랐다면 그 선택도 되살린다
  const [producesMeshes, setProducesMeshes] = useState(Boolean(carried.mesh))
  const [error, setError] = useState<string | null>(null)

  // 목록이 오면 첫 항목을 고른다. 사용자가 고른 값이 있으면 건드리지 않는다
  const selectedProvider =
    providerId !== null && textProviders.some((provider) => provider.id === providerId)
      ? providerId
      : (textProviders[0]?.id ?? null)

  const models = useProviderModels(selectedProvider)

  // 단가는 고르는 순간의 판단 근거다 — 관리자 표까지 가야 알 수 있으면 아무도 안 본다
  const { prices } = useModelPrices()
  const priceHint = (id: string) => modelPriceHint(prices, id)

  // 모델도 같은 규칙이지만 한 가지가 더 있다: 고른 모델이 현재 공급자의 목록에 없으면
  // 버린다. 공급자를 바꿨을 때 이전 공급자의 모델이 남아 접수에서 거절되는 것을 막는다
  const selectedModel =
    modelId !== null && models.models.some((model) => model.id === modelId)
      ? modelId
      : (models.models[0]?.id ?? null)

  // 이미지 공급자는 텍스트와 따로 고른다 (D-4) — 목록도 엔드포인트도 다르다
  const selectedImageProvider =
    imageProviderId !== null && imageProviders.some((provider) => provider.id === imageProviderId)
      ? imageProviderId
      : (imageProviders[0]?.id ?? null)

  const imageModels = useProviderImageModels(selectedImageProvider)

  const selectedImageModel =
    imageModelId !== null && imageModels.models.some((model) => model.id === imageModelId)
      ? imageModelId
      : (imageModels.models[0]?.id ?? null)

  // 3D 공급자는 선택이다 (§11.1) — 없으면 이미지까지만 도는 기존 작업이 된다
  const selectedMeshProvider =
    meshProviderId !== null && meshProviders.some((provider) => provider.id === meshProviderId)
      ? meshProviderId
      : (meshProviders[0]?.id ?? null)

  const meshModels = useProviderMeshModels(selectedMeshProvider)

  const selectedMeshModel =
    meshModelId !== null && meshModels.models.some((model) => model.id === meshModelId)
      ? meshModelId
      : (meshModels.models[0]?.id ?? null)

  const hasImage = Boolean(file) || Boolean(reusedImageId)
  const pending = upload.isPending || startJob.isPending

  // 이미지 모델을 못 고르면 접수를 막는다 (§5.4) — 서버도 거절하지만, 워커까지 갔다
  // 오는 오류보다 여기서 막히는 편이 낫다
  const canStart =
    hasImage &&
    Boolean(selectedProvider && selectedModel) &&
    Boolean(selectedImageProvider && selectedImageModel) &&
    // **공급자만 고르고 모델을 못 불러온 상태에서는 막는다** (§11.1). 그대로 보내면
    // 작업이 3D 를 만들 것처럼 보이면서 아무것도 만들지 않는다
    (selectedMeshProvider === null || selectedMeshModel !== null) &&
    !pending

  async function handleStart() {
    if (!hasImage || !selectedProvider || !selectedModel) return
    if (!selectedImageProvider || !selectedImageModel) return

    setError(null)

    try {
      // 새로 고른 파일이 있으면 올리고, 없으면 이어받은 것을 그대로 쓴다.
      // 같은 이미지를 다시 올리면 저장소에 사본이 쌓이고 골든 세트와도 어긋난다
      const uploadId = file ? (await upload.mutateAsync(file)).id : reusedImageId!

      const accepted = await startJob.mutateAsync({
        category: 'background',
        uploadId,
        providerConfigId: selectedProvider,
        model: selectedModel,
        imageProviderConfigId: selectedImageProvider,
        imageModel: selectedImageModel,
        requiresReview,
        // 체크를 껐으면 아예 안 보낸다 — 두 필드가 없으면 서버가 이미지까지만 돈다.
        // 둘 다 있을 때만 보낸다 — 한쪽만 가면 서버가 접수를 거절한다
        ...(producesMeshes && selectedMeshProvider && selectedMeshModel
          ? {
              meshProviderConfigId: selectedMeshProvider,
              meshModel: selectedMeshModel,
            }
          : {}),
      })

      // 여기가 FR-13 의 핵심이다. 이후 상태는 URL 이 갖는다
      navigate(backgroundJobPath(accepted.id))
    } catch (cause) {
      setError(apiErrorMessage(cause, '분석을 시작하지 못했습니다'))
    }
  }

  return (
    <PageContainer width="max" title="배경 스튜디오" testId="background-studio">
      <div className={styles.studioInput} data-testid="studio-content">
        <BackgroundPipelinePreview
          requiresReview={requiresReview}
          producesMeshes={producesMeshes}
        />
        <ModeTabs mode={mode} onChange={setMode} />

        {mode === 'prompt' ? (
          <PromptModePanel />
        ) : (
          <>
            <ImageDropzone file={file} reusedImageId={reusedImageId} onSelect={setFile} />
            {isLoading ? <p role="status">공급자를 불러오는 중…</p> : null}
            {providersError ? (
              <div role="alert" className={styles.notice}>
                <p>{apiErrorMessage(providersError, '공급자 목록을 불러올 수 없습니다')}</p>
                <Button variant="outline" onClick={() => void refetchProviders()}>
                  공급자 다시 조회
                </Button>
              </div>
            ) : null}

            {isLoading || providersError ? null : (
              <div className={styles.settingsRow}>
                <ProviderSelect
                  providers={textProviders}
                  value={selectedProvider}
                  onChange={setProviderId}
                />

                {/* 공급자를 골라야 물어볼 대상이 생긴다 */}
                {selectedProvider === null ? null : (
                  <ModelSelect
                    models={models.models}
                    isLoading={models.isLoading}
                    errorMessage={models.errorMessage}
                    value={selectedModel}
                    onChange={setModelId}
                    priceHint={priceHint}
                  />
                )}
              </div>
            )}

            {/*
              Hide the duplicate second notice only when no provider exists at all. A
              text-only connection still needs an explicit image-provider notice because
              the missing capability is a different next action.
            */}
            {isLoading ||
            providersError ||
            (textProviders.length === 0 && imageProviders.length === 0) ? null : (
              <div className={styles.settingsRow} data-testid="image-settings">
                <ProviderSelect
                  providers={imageProviders}
                  value={selectedImageProvider}
                  onChange={setImageProviderId}
                  label="이미지 공급자"
                  fieldId="studio-image-provider"
                  testId="image-provider-select"
                  emptyLabel="이미지 생성 공급자"
                />

                {selectedImageProvider === null ? null : (
                  <ModelSelect
                    models={imageModels.models}
                    isLoading={imageModels.isLoading}
                    errorMessage={imageModels.errorMessage}
                    value={selectedImageModel}
                    onChange={setImageModelId}
                    label="이미지 모델"
                    fieldId="studio-image-model"
                    testId="image-model-select"
                    priceHint={priceHint}
                  />
                )}
              </div>
            )}

            {/* 검수 게이트 opt-in (review-gate §목표) — 켜면 분해 직후 사람이 파츠를 검수한다 */}
            <label className="mt-5 flex items-center gap-2">
              <input
                type="checkbox"
                checked={requiresReview}
                onChange={(event) => setRequiresReview(event.target.checked)}
                data-testid="requires-review-checkbox"
              />
              <span className={styles.label}>파츠 검수하기</span>
            </label>

            {/*
              3D 팬아웃 opt-in. **기본은 꺼짐** — 켜면 파츠마다 4방향 이미지가 모이는 즉시
              3D 공정이 자동으로 나간다(파츠당 약 $0.40). 접수할 때 공급자·모델만 있으면
              도는 구조라, 모르고 켜 둔 대가가 한쪽으로만 크다.
            */}
            {meshProviders.length === 0 ? null : (
              <label className="mt-5 flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={producesMeshes}
                  onChange={(event) => setProducesMeshes(event.target.checked)}
                  data-testid="produces-meshes-checkbox"
                />
                <span className={styles.label}>이미지 생성 완료 후 검수 없이 3D 까지 생성하기</span>
              </label>
            )}

            {/*
              3D 는 선택이다 (§11.1). Tripo 설정이 없으면 이 줄 자체를 그리지 않는다 —
              고를 수 없는 것을 비활성으로 보여 주면 "왜 못 고르지" 를 묻게 된다.
            */}
            {meshProviders.length === 0 || !producesMeshes ? null : (
              <div className={styles.settingsRow} data-testid="mesh-settings">
                <ProviderSelect
                  providers={meshProviders}
                  value={selectedMeshProvider}
                  onChange={setMeshProviderId}
                  label="3D 공급자"
                  fieldId="studio-mesh-provider"
                  testId="mesh-provider-select"
                  emptyLabel="3D 생성 공급자"
                />

                {selectedMeshProvider === null ? null : (
                  <ModelSelect
                    models={meshModels.models}
                    isLoading={meshModels.isLoading}
                    errorMessage={meshModels.errorMessage}
                    value={selectedMeshModel}
                    onChange={setMeshModelId}
                    label="3D 모델"
                    fieldId="studio-mesh-model"
                    testId="mesh-model-select"
                    priceHint={priceHint}
                  />
                )}
              </div>
            )}

            {error ? (
              <p className={styles.rejection} role="alert" data-testid="start-error">
                {error}
              </p>
            ) : null}

            <div className={styles.actions}>
              <Button
                variant="default"
                onClick={handleStart}
                disabled={!canStart}
                data-testid="start-analysis"
              >
                {pending ? '시작하는 중…' : '분석 시작'}
              </Button>
            </div>
          </>
        )}
      </div>
    </PageContainer>
  )
}

/** 진행 · 결과 · 실패 — 입력 화면과 같은 max 페이지 기준선 유지. */
// Design Ref: scene-assembly §2.3 — three/fiber/drei 는 이 청크 뒤에만 있다 (SC-07)
const SceneAssemblyView = lazy(() =>
  import('./SceneAssemblyView').then((m) => ({ default: m.SceneAssemblyView })),
)

function JobView({ jobId }: { jobId: string }) {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const { job, isLoading, isNotFound, error: jobError, refetch } = useJob(jobId)
  const cancelJob = useCancelJob()
  const retryTask = useRetryTask(jobId)
  const addMesh = useAddMeshProduction(jobId)
  const generateViews = useGenerateSelectedViews(jobId)
  const returnToDesc = useReturnToDescriptions(jobId)
  const replanPartMesh = useReplanPartMesh(jobId)

  // 이름표 원본. 사용 중지·삭제된 공급자도 이름은 보여야 하므로 걸러지지 않은 목록을 쓴다
  const { providers } = useProviders()

  // `useJobCalls` 의 첫 소비자다 (사이클 #7) — 정의만 있고 아무 화면도 쓰지 않았다.
  // 갱신에 별도 장치가 없다: useJob 이 폴링하고 재시도가 이 키를 무효화한다
  const { calls } = useJobCalls(jobId)
  const { prices } = useModelPrices()

  // 탭 상태는 URL 에 산다 (scene-assembly §7.3-7) — 새로고침·공유에 살아남는다.
  // GLB 가 없는데 ?view=scene 로 들어오면 분석 탭으로 떨어진다 (FR-04)
  const sceneEnabled = job !== null && job !== undefined && canOpenSceneTab(job)
  const view = params.get('view') === 'scene' && sceneEnabled ? 'scene' : 'analysis'
  const sceneLayout = useSceneLayout(job, sceneEnabled && view === 'scene')

  // 결정적 캡처 손잡이 (background-similarity-tuning §8.2) — 장면이 준비되면 올라온다
  const [sceneCapture, setSceneCapture] = useState<SceneCaptureHandle | null>(null)

  const selectView = (next: 'analysis' | 'scene') => {
    const nextParams = new URLSearchParams(params)
    if (next === 'scene') nextParams.set('view', 'scene')
    else nextParams.delete('view')
    setParams(nextParams, { replace: true })
  }

  /**
   * 입력으로 돌아간다 — **이미지와 모델 선택을 이어받아서.**
   *
   * 같은 이미지로 프롬프트만 바꿔 다시 돌리는 것이 가장 흔한 다음 행동이다.
   * 매번 파일을 다시 고르게 하면 그 흐름이 끊기고, 저장소에는 같은 이미지의
   * 사본이 쌓인다.
   *
   * **모델도 같이 넘긴다.** 이어받지 않으면 선택이 목록 첫 항목으로 되돌아가고,
   * 모델만 바꿔 비교하려던 사용자는 이미지 말고 다른 변수까지 바뀐 채로 돌리게 된다.
   */
  const goToInput = (sourceImageId?: string) =>
    navigate(
      sourceImageId
        ? backgroundWithImagePath(sourceImageId, {
            ...job?.models,
            requiresReview: job?.requiresReview,
          })
        : ROUTES.background,
    )

  if (isLoading) {
    return (
      <PageContainer width="max" testId="background-studio">
        <p role="status">작업을 불러오는 중…</p>
      </PageContainer>
    )
  }

  // 없는 jobId — 북마크가 오래됐거나 작업이 지워졌다 (§5.4 Resume)
  if (isNotFound && !job) {
    return (
      <PageContainer width="max" title="배경 스튜디오" testId="background-studio">
        <div className={styles.studioResult} data-testid="studio-content">
          <p className={styles.notice} data-testid="job-not-found">
            해당 작업을 찾을 수 없습니다.
          </p>
          <Button variant="default" onClick={() => goToInput()} data-testid="job-not-found-back">
            새로 분석하기
          </Button>
        </div>
      </PageContainer>
    )
  }

  if (!job) {
    return (
      <PageContainer width="max" title="배경 스튜디오" testId="background-studio">
        <div role="alert" className={styles.notice}>
          <p>{apiErrorMessage(jobError, '작업을 불러올 수 없습니다. 연결을 확인해 주세요')}</p>
          <Button variant="outline" onClick={() => void refetch()}>
            작업 다시 조회
          </Button>
        </div>
      </PageContainer>
    )
  }

  const failedTask = job.tasks.find((t) => t.status === 'failed')

  return (
    <PageContainer width="max" title="배경 스튜디오" testId="background-studio">
      <div className={styles.studioResult} data-testid="studio-content">
        {jobError ? (
          <div role="alert" className={styles.notice}>
            <p>{apiErrorMessage(jobError, '작업을 다시 조회할 수 없습니다')}</p>
            <p>마지막 조회 결과와 미저장 편집을 유지했습니다.</p>
            <Button variant="outline" onClick={() => void refetch()}>
              작업 다시 조회
            </Button>
          </div>
        ) : null}
        <div className="mb-4" data-testid="background-top-pipeline-progress">
          <BackgroundPipelineStepper tasks={job.tasks} jobStatus={job.status} />
        </div>
        {/* 모델은 접수 시점에 고정된다 — 상태 분기 밖에 두어야 실패 화면에서도 남는다 */}
        <ModelSummary models={job.models} providers={providers} />

        {job.status === 'failed' ? (
          <div className={styles.failure} data-testid="run-failure">
            <p className={styles.failureTitle}>분석에 실패했습니다</p>
            <p className={styles.failureReason} data-testid="failure-reason">
              {formatFailureReason(job.failureReason)}
            </p>

            {/* 어느 단계에서 멈췄는지 그 자리에 보여준다 — 코드만으로는 알 수 없다 */}
            <StageProgress tasks={job.tasks} />
            <div className="mt-4 flex items-center justify-center gap-2">
              {failedTask ? (
                <Button
                  variant="default"
                  onClick={() => retryTask.mutate(failedTask.id)}
                  disabled={retryTask.isPending}
                  data-testid="retry-analysis"
                >
                  {retryTask.isPending ? '다시 시도 중…' : '실패한 단계 다시 시도'}
                </Button>
              ) : null}
              <Button
                variant="outline"
                onClick={() => goToInput(job.sourceImageId)}
                data-testid="back-to-input"
              >
                설정 바꿔서 다시 시작
              </Button>
            </div>
          </div>
        ) : job.status === 'canceled' ? (
          <>
            <p className={styles.notice} data-testid="run-canceled">
              취소된 작업입니다.
            </p>
            <Button
              variant="default"
              onClick={() => goToInput(job.sourceImageId)}
              data-testid="canceled-back"
            >
              새로 분석하기
            </Button>
          </>
        ) : job.status === 'pendingReview' ? (
          <ReviewGate jobId={jobId} sourceImageId={job.sourceImageId} onApproved={() => {}} />
        ) : hasResultToShow(job) ? (
          <>
            {/* 진행 중 결과 화면의 비용 통제 동작 */}
            {isActive(job.status) ? (
              <LiveActionBar
                job={job}
                canceling={cancelJob.isPending}
                onCancel={() =>
                  cancelJob.mutate(jobId, {
                    onSuccess: () => goToInput(job.sourceImageId),
                  })
                }
              />
            ) : null}

            {/* 분석 / 3D 배경 (scene-assembly §5.1) — 기존 결과 화면은 탭 안으로만 이동 */}
            <StudioViewTabs view={view} onSelect={selectView} sceneEnabled={sceneEnabled} />

            {view === 'scene' ? (
              sceneLayout.layout !== null ? (
                // 장면 + 유사도 inspector — Canvas 는 계속 mounted 다 (§14.1):
                // inspector 를 여닫아도 WebGL 컨텍스트가 늘지 않는다
                <div className={styles.sceneRow}>
                  <div className={styles.sceneMain}>
                    <Suspense
                      fallback={<div className={styles.sceneEmpty}>3D 배경을 불러오는 중…</div>}
                    >
                      <SceneAssemblyView
                        layout={sceneLayout.layout}
                        // setState 에 함수를 그대로 주면 updater 로 실행된다 — 한 번 감싼다
                        onCaptureHandle={(handle) => setSceneCapture(() => handle)}
                      />
                    </Suspense>
                  </div>
                  <SimilarityInspector
                    jobId={jobId}
                    sourceImageId={job.sourceImageId}
                    layout={sceneLayout.layout}
                    capture={sceneCapture}
                  />
                </div>
              ) : (
                <div className={styles.sceneEmpty} data-testid="scene-pending">
                  {sceneLayout.isError
                    ? '조립 명세를 불러오지 못했습니다'
                    : '3D 배경을 불러오는 중…'}
                </div>
              )
            ) : (
              <RunResult
                job={job}
                live={isActive(job.status)}
                onRestart={() => goToInput(job.sourceImageId)}
                calls={calls}
                prices={prices}
                onRetryTask={(taskId) => retryTask.mutate(taskId)}
                retryingTaskId={retryTask.isPending ? retryTask.variables : null}
                onAddMesh={(selection) => addMesh.mutate(selection)}
                addingMesh={addMesh.isPending}
                onGenerateViews={(directions) => generateViews.mutateAsync(directions)}
                onReturnToDescriptions={(partId) => returnToDesc.mutateAsync(partId)}
                onReplanPartMesh={(selection) => replanPartMesh.mutateAsync(selection)}
              />
            )}
          </>
        ) : (
          <RunProgress
            sourceImageId={job.sourceImageId}
            tasks={job.tasks}
            cancelPending={cancelJob.isPending}
            // 취소는 즉시 반환된다 (§4.2 #8). 워커를 기다리지 않고 입력으로 돌아간다
            onCancel={() =>
              cancelJob.mutate(jobId, { onSuccess: () => goToInput(job.sourceImageId) })
            }
          />
        )}
      </div>
    </PageContainer>
  )
}
