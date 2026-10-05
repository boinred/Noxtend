/**
 * Design Ref: character-studio §6.1 · §6.3 · FR-13 — 캐릭터 스튜디오.
 * character-mesh-ui §FR-01~08 — 3D(mesh) 선택 UI·이어받기.
 *
 * **배경 스튜디오의 카테고리 비의존 컴포넌트를 재사용한다** — 업로드·공급자/모델 선택·
 * 진행·결과는 그대로 쓰고, 캐릭터 고유값(성별·파츠 힌트)만 더한다. URL 이 작업의 주소라는
 * 규칙도 같다(§FR-13): `jobId` 유무가 입력/진행·결과를 가른다.
 *
 * **3D(mesh) 선택은 켜져 있지만 기본은 꺼짐이다**(character-mesh-ui D-02). 배경은 첫
 * mesh 공급자를 자동으로 고르지만, 캐릭터는 파츠가 최대 20개라 같은 기본값이면 첫
 * 실행에서 크레딧이 조용히 나간다 — 사용자가 명시적으로 켜야 한다.
 */
import { useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageContainer } from '@/features/screens/PageContainer'
import { Button } from '@/components/ui/button'
import {
  useAddMeshProduction,
  useJob,
  useCancelJob,
  useReplanPartMesh,
  useRetryTask,
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
  characterJobPath,
  characterWithImagePath,
  readCharacterSelection,
} from '@/routes/paths'
import { ImageDropzone } from '@/features/screens/background/ImageDropzone'
import { ProviderSelect } from '@/features/screens/background/ProviderSelect'
import { ModelSelect } from '@/features/screens/background/ModelSelect'
import { RunProgress } from '@/features/screens/background/RunProgress'
import { StageProgress } from '@/features/screens/background/StageProgress'
import { RunResult } from '@/features/screens/background/RunResult'
import { ReviewGate } from '@/features/screens/background/ReviewGate'
import { ModelSummary } from '@/features/screens/background/ModelSummary'
import { backgroundStyles as styles } from '@/features/screens/background/backgroundStyles'
import { formatFailureReason } from '@/features/screens/background/failureMessages'
import { PartHintPanel } from './PartHintPanel'
import { CharacterPipelineStepper } from './CharacterPipelineStepper'
import { CharacterPipelinePreview } from './CharacterPipelinePreview'
import { PART_TAXONOMY } from './characterPartTaxonomy'
import { buildPartHints, selectionFromPartHints, type HintSelection } from './partHintSelection'
import type { CharacterGender } from '@/domain/job/types'

export function CharacterStudioScreen() {
  const { jobId } = useParams<{ jobId: string }>()

  // jobId 가 있으면 진행/결과, 없으면 입력 — 배경과 같은 URL 함수 분기
  return jobId ? <JobView jobId={jobId} /> : <InputView />
}

/** 입력 단계 — 업로드 · 성별 · 파츠 힌트 · 공급자/모델(텍스트·이미지·3D) 선택 · 생성 시작. */
function InputView() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const { textProviders, imageProviders, meshProviders, isLoading } = useProviders()
  const upload = useUpload()
  const startJob = useStartJob()

  /**
   * "다시 시도" 가 넘긴 이미지·모델·성별·파츠 힌트 (character-mesh-ui §FR-08).
   *
   * 첫 렌더의 초기값으로만 쓴다 — 이후 사용자가 고른 값이 이긴다. 배경과 같은 이유다:
   * 같은 이미지로 일부만 바꿔 비교하는 것이 튜닝의 기본 동작인데, 이어받지 않으면
   * 매번 통제 변수가 깨진다.
   */
  const reusedImageId = params.get('image')
  const carried = readCharacterSelection(params)

  const [file, setFile] = useState<File | null>(null)
  const [gender, setGender] = useState<CharacterGender | null>(carried.gender)
  const [hintSelection, setHintSelection] = useState<HintSelection>(
    carried.partHints ? selectionFromPartHints(PART_TAXONOMY, carried.partHints) : {},
  )
  const [providerId, setProviderId] = useState<string | null>(
    carried.text?.providerConfigId ?? null,
  )
  const [modelId, setModelId] = useState<string | null>(carried.text?.model ?? null)
  const [imageProviderId, setImageProviderId] = useState<string | null>(
    carried.image?.providerConfigId ?? null,
  )
  const [imageModelId, setImageModelId] = useState<string | null>(carried.image?.model ?? null)
  // 새로 시작할 때 3D 기본값은 꺼짐이다(D-02). 다만 "다시 시도"로 이어받은 mesh
  // 선택은 초기값으로 그대로 살아난다(D-06) — 아래 selectedMeshProvider 참고
  const [meshProviderId, setMeshProviderId] = useState<string | null>(
    carried.mesh?.providerConfigId ?? null,
  )
  const [meshModelId, setMeshModelId] = useState<string | null>(carried.mesh?.model ?? null)
  // 검수 게이트 opt-in (review-gate §목표) — 기본은 꺼짐. 켜면 분해 직후 자동 생성 대신
  // 검수 대기로 멈추고, 사람이 파츠를 확인·추가·삭제한 뒤 전체 승인으로만 진행한다
  // **기본값이 켬이다.** 끄고 접수하면 분해 직후 파츠 수 × 4 장이 곧바로 나간다 —
  // 파츠 26개면 이미지 104장이다. 깜빡한 대가가 한쪽으로만 크므로 안전한 쪽을 기본으로 둔다.
  // URL 에 넘겨받은 선택이 있으면 그 값을 따른다
  const [requiresReview, setRequiresReview] = useState(carried.requiresReview ?? true)
  // 3D 팬아웃 opt-in — **기본은 꺼짐**. 켜면 공급자·모델이 접수에 실려 나가고,
  // 파츠의 4방향이 모이는 대로 3D 가 자동으로 돈다(파츠당 약 $0.40).
  // 이어받기(FR-08)는 예외 — 이전 작업이 3D 를 골랐다면 그 선택도 되살린다(D-06)
  const [producesMeshes, setProducesMeshes] = useState(Boolean(carried.mesh))
  const [error, setError] = useState<string | null>(null)

  // 목록이 오면 첫 항목을 고른다. 사용자가 고른 값이 있으면 건드리지 않는다 (배경과 같은 규칙)
  const selectedProvider =
    providerId !== null && textProviders.some((provider) => provider.id === providerId)
      ? providerId
      : (textProviders[0]?.id ?? null)

  const models = useProviderModels(selectedProvider)

  const { prices } = useModelPrices()
  const priceHint = (id: string) => modelPriceHint(prices, id)

  // 공급자를 바꿨을 때 이전 공급자의 모델이 남아 접수에서 거절되는 것을 막는다
  const selectedModel =
    modelId !== null && models.models.some((model) => model.id === modelId)
      ? modelId
      : (models.models[0]?.id ?? null)

  const selectedImageProvider =
    imageProviderId !== null && imageProviders.some((provider) => provider.id === imageProviderId)
      ? imageProviderId
      : (imageProviders[0]?.id ?? null)

  const imageModels = useProviderImageModels(selectedImageProvider)

  const selectedImageModel =
    imageModelId !== null && imageModels.models.some((model) => model.id === imageModelId)
      ? imageModelId
      : (imageModels.models[0]?.id ?? null)

  // 3D 는 선택이다(§FR-03 D-02). 배경과 달리 목록 첫 항목으로 자동 켜지 않는다 —
  // 고른 적 없으면 계속 null 이고, 페이로드에 mesh 필드가 실리지 않는다
  const selectedMeshProvider =
    meshProviderId !== null && meshProviders.some((provider) => provider.id === meshProviderId)
      ? meshProviderId
      : null

  const meshModels = useProviderMeshModels(selectedMeshProvider)

  const selectedMeshModel =
    meshModelId !== null && meshModels.models.some((model) => model.id === meshModelId)
      ? meshModelId
      : (meshModels.models[0]?.id ?? null)

  const hasImage = Boolean(file) || Boolean(reusedImageId)
  const pending = upload.isPending || startJob.isPending

  // 캐릭터는 성별이 필수다(§D-01) — 서버도 거절하지만 여기서 막는 편이 낫다.
  // 이미지 생성이 캐릭터의 목적이라 이미지 공급자·모델도 필수다
  const canStart =
    hasImage &&
    gender !== null &&
    Boolean(selectedProvider && selectedModel) &&
    Boolean(selectedImageProvider && selectedImageModel) &&
    // 3D 를 켰는데 모델을 못 골랐으면 막는다(FR-04) — 그대로 보내면 3D 를 만들 것처럼
    // 보이면서 아무것도 만들지 않는다
    (selectedMeshProvider === null || selectedMeshModel !== null) &&
    !pending

  async function handleStart() {
    if (!hasImage || gender === null) return
    if (!selectedProvider || !selectedModel) return
    if (!selectedImageProvider || !selectedImageModel) return

    setError(null)

    try {
      const uploadId = file ? (await upload.mutateAsync(file)).id : reusedImageId!
      const partHints = buildPartHints(PART_TAXONOMY, hintSelection)

      const accepted = await startJob.mutateAsync({
        category: 'character',
        uploadId,
        providerConfigId: selectedProvider,
        model: selectedModel,
        imageProviderConfigId: selectedImageProvider,
        imageModel: selectedImageModel,
        gender,
        requiresReview,
        // 빈 힌트는 보내지 않는다 — 서버가 "없음" 으로 폴백한다(§4.2)
        ...(partHints.length > 0 ? { partHints } : {}),
        // 둘 다 있을 때만 보낸다 — 한쪽만 가면 서버가 접수를 거절한다
        // 체크를 껐으면 아예 안 보낸다 — 두 필드가 없으면 서버가 이미지까지만 돈다
        ...(producesMeshes && selectedMeshProvider && selectedMeshModel
          ? { meshProviderConfigId: selectedMeshProvider, meshModel: selectedMeshModel }
          : {}),
      })

      // FR-13 — 이후 상태는 URL 이 갖는다
      navigate(characterJobPath(accepted.id))
    } catch (cause) {
      setError(apiErrorMessage(cause, '생성을 시작하지 못했습니다'))
    }
  }

  return (
    <PageContainer width="max" title="캐릭터 스튜디오" testId="character-studio">
      <div className={styles.studioInput} data-testid="studio-content">
        <CharacterPipelinePreview requiresReview={requiresReview} producesMeshes={producesMeshes} />

        {/* 처음 쓰는 사람에게 전체 흐름을 한 줄로 먼저 알려준다 */}
        <p className={styles.onboardingNotice} data-testid="input-intro-notice">
          캐릭터 이미지 한 장을 올리면 자동으로 파츠를 나누고 방향별 이미지를 만듭니다. 성별은
          필수지만 파츠 힌트·3D 생성 여부는 전부 선택 사항입니다.
        </p>

        <ImageDropzone file={file} reusedImageId={reusedImageId} onSelect={setFile} />

        <div className={styles.field}>
          {/* 성별은 바디의 속성이라 힌트 패널이 바디 그룹 바로 위에 렌더한다 */}
          <PartHintPanel
            selection={hintSelection}
            onChange={setHintSelection}
            gender={gender}
            onGenderChange={setGender}
          />
        </div>

        {isLoading ? null : (
          <div className={styles.settingsRow}>
            <ProviderSelect
              providers={textProviders}
              value={selectedProvider}
              onChange={setProviderId}
            />

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

        {isLoading || (textProviders.length === 0 && imageProviders.length === 0) ? null : (
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
          3D 공정이 자동으로 나간다(파츠당 약 $0.40, 캐릭터 하나에 $9 남짓). 접수할 때
          공급자·모델만 있으면 도는 구조라, 모르고 켜 둔 대가가 한쪽으로만 크다.
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
          3D 는 선택이다(FR-01·FR-02). 등록된 mesh 공급자가 없으면 줄 자체를 안 그린다 —
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
            data-testid="start-generation"
          >
            {pending ? '시작하는 중…' : '생성 시작'}
          </Button>
        </div>
      </div>
    </PageContainer>
  )
}

/** 진행 · 결과 · 실패 — 배경 스튜디오와 같은 컴포넌트를 카테고리만 캐릭터로 바꿔 재사용한다. */
function JobView({ jobId }: { jobId: string }) {
  const navigate = useNavigate()
  const { job, isLoading, isNotFound } = useJob(jobId)
  const cancelJob = useCancelJob()
  const retryTask = useRetryTask(jobId)
  const addMesh = useAddMeshProduction(jobId)
  const replanPartMesh = useReplanPartMesh(jobId)
  const { providers } = useProviders()
  const { calls } = useJobCalls(jobId)
  const { prices } = useModelPrices()

  /**
   * 입력으로 돌아간다 — 이미지·모델(텍스트·이미지·3D)·성별·파츠 힌트를 이어받아서
   * (character-mesh-ui §FR-08). 배경 `goToInput` 과 같은 이유다.
   */
  const goToInput = (sourceImageId?: string) =>
    navigate(
      sourceImageId
        ? characterWithImagePath(sourceImageId, {
            ...job?.models,
            gender: job?.gender ?? null,
            partHints: job?.partHints ?? null,
            requiresReview: job?.requiresReview,
          })
        : ROUTES.character,
    )

  if (isLoading) {
    return <PageContainer width="max" testId="character-studio" children={null} />
  }

  if (isNotFound || !job) {
    return (
      <PageContainer width="max" title="캐릭터 스튜디오" testId="character-studio">
        <div className={styles.studioResult} data-testid="studio-content">
          <p className={styles.notice} data-testid="job-not-found">
            해당 작업을 찾을 수 없습니다.
          </p>
          <Button variant="default" onClick={() => goToInput()} data-testid="job-not-found-back">
            새로 생성하기
          </Button>
        </div>
      </PageContainer>
    )
  }

  const failedTask = job.tasks.find((t) => t.status === 'failed')

  return (
    <PageContainer width="max" title="캐릭터 스튜디오" testId="character-studio">
      <div className={styles.studioResult} data-testid="studio-content">
        <div className="mb-4" data-testid="character-top-pipeline-progress">
          <CharacterPipelineStepper tasks={job.tasks} jobStatus={job.status} />
        </div>
        <ModelSummary models={job.models} providers={providers} />

        {job.status === 'failed' ? (
          <div className={styles.failure} data-testid="run-failure">
            <p className={styles.failureTitle}>생성에 실패했습니다</p>
            <p className={styles.failureReason} data-testid="failure-reason">
              {formatFailureReason(job.failureReason)}
            </p>
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
              새로 생성하기
            </Button>
          </>
        ) : job.status === 'pendingReview' ? (
          <ReviewGate jobId={jobId} sourceImageId={job.sourceImageId} onApproved={() => {}} />
        ) : job.status === 'succeeded' || job.status === 'partiallySucceeded' ? (
          <RunResult
            job={job}
            onRestart={() => goToInput(job.sourceImageId)}
            calls={calls}
            prices={prices}
            onRetryTask={(taskId) => retryTask.mutate(taskId)}
            retryingTaskId={retryTask.isPending ? retryTask.variables : null}
            onAddMesh={(selection) => addMesh.mutate(selection)}
            addingMesh={addMesh.isPending}
            onReplanPartMesh={(selection) => replanPartMesh.mutateAsync(selection)}
          />
        ) : (
          <RunProgress
            sourceImageId={job.sourceImageId}
            tasks={job.tasks}
            cancelPending={cancelJob.isPending}
            onCancel={() =>
              cancelJob.mutate(jobId, { onSuccess: () => goToInput(job.sourceImageId) })
            }
          />
        )}
      </div>
    </PageContainer>
  )
}
