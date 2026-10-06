import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Button } from '@/components/ui/button'
import { useUpload } from '@/app/queries/useUpload'
import { useProviders, useProviderModels, useProviderImageModels } from '@/app/queries/useProviders'
import { useStartSpriteJob } from '@/app/queries/useSprites'
import { apiErrorMessage, apiErrorStatus } from '@/app/queries/errors'
import { readSpriteSource, spriteBackgroundJobPath } from '@/routes/paths'
import type { SpriteView, SpriteOutputKind, SpriteRepeat } from '@/domain/sprites/types'
import { ProviderSelect } from '../background/ProviderSelect'
import { ModelSelect } from '../background/ModelSelect'
import { ImageDropzone } from '../background/ImageDropzone'
import { spriteStyles as styles } from './spriteStyles'

export function SpriteInput() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const source = readSpriteSource(params)
  const [file, setFile] = useState<File | null>(null)
  const [view, setView] = useState<SpriteView | ''>('')
  const [kind, setKind] = useState<SpriteOutputKind | ''>('')
  const [tileWidth, setTileWidth] = useState<64 | 128 | 256>(128)
  const [repeat, setRepeat] = useState<SpriteRepeat>('both')
  const providers = useProviders()
  const [textId, setTextId] = useState<string | null>(null)
  const [imageId, setImageId] = useState<string | null>(null)
  const textProvider =
    providers.textProviders.find((p) => p.id === textId) ?? providers.textProviders[0]
  const imageProvider =
    providers.imageProviders.find((p) => p.id === imageId) ?? providers.imageProviders[0]
  const textModels = useProviderModels(textProvider?.id ?? null)
  const imageModels = useProviderImageModels(imageProvider?.id ?? null)
  const [textModelId, setTextModel] = useState<string | null>(null)
  const [imageModelId, setImageModel] = useState<string | null>(null)
  const textModel = textModels.models.find((m) => m.id === textModelId) ?? textModels.models[0]
  const imageModel = imageModels.models.find((m) => m.id === imageModelId) ?? imageModels.models[0]
  const capability = imageModel?.sprite
  const supported =
    capability?.supportsTransparency === true &&
    Array.isArray(capability.sizes) &&
    capability.sizes.length > 0 &&
    capability.sizes.every(
      (size) =>
        size != null &&
        Number.isInteger(size.width) &&
        size.width > 0 &&
        Number.isInteger(size.height) &&
        size.height > 0,
    )
  const upload = useUpload()
  const start = useStartSpriteJob()
  const [uploadError, setUploadError] = useState<string | null>(null)
  const busy = upload.isPending || start.isPending
  async function submit() {
    if (
      !view ||
      !kind ||
      !textProvider ||
      !imageProvider ||
      !textModel ||
      !imageModel ||
      (!file && !source)
    )
      return
    setUploadError(null)
    try {
      const identity = file ? { uploadId: (await upload.mutateAsync(file)).id } : source!
      start.mutate(
        {
          requestId: crypto.randomUUID(),
          ...identity,
          providerConfigId: textProvider.id,
          model: textModel.id,
          imageProviderConfigId: imageProvider.id,
          imageModel: imageModel.id,
          settings: { view, outputKind: kind, tileWidth, repeat },
        },
        { onSuccess: (receipt) => navigate(spriteBackgroundJobPath(receipt.id)) },
      )
    } catch (error) {
      setUploadError(apiErrorMessage(error, '이미지 업로드에 실패했습니다. 다시 시도해 주세요'))
    }
  }
  return (
    <div className={styles.stack}>
      <section className={styles.panel} aria-label="이미지 입력">
        {source ? (
          <p className={styles.hint}>
            기존 작업 결과를 원본으로 사용합니다. 새 이미지를 선택하면 선택한 이미지로 시작합니다.
          </p>
        ) : null}
        {(params.has('sourceJobId') || params.has('sourceGeneratedImageId')) && !source ? (
          <p role="alert" className={styles.error}>
            원본 작업·결과 ID 쌍이 유효하지 않습니다. 이미지를 선택해 주세요.
          </p>
        ) : null}
        <ImageDropzone file={file} onSelect={setFile} />
      </section>
      <section className={styles.panel} aria-label="제작 설정">
        <div className={styles.grid}>
          <div className={styles.field}>
            <label htmlFor="sprite-view">시점</label>
            <Select
              value={view || undefined}
              onValueChange={(value) => setView(value as SpriteView)}
            >
              <SelectTrigger id="sprite-view" aria-required="true">
                <SelectValue placeholder="시점 선택" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="sideView">횡스크롤</SelectItem>
                <SelectItem value="topDown">탑다운</SelectItem>
                <SelectItem value="isometric">아이소메트릭</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div className={styles.field}>
            <label htmlFor="sprite-kind">결과 유형</label>
            <Select
              value={kind || undefined}
              onValueChange={(value) => setKind(value as SpriteOutputKind)}
            >
              <SelectTrigger id="sprite-kind" aria-required="true">
                <SelectValue placeholder="결과 유형 선택" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="layers">배경 레이어</SelectItem>
                <SelectItem value="tiles">반복 타일</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        {view ? (
          <p className={styles.hint}>
            원본과 선택한 시점이 다르면{' '}
            {view === 'sideView' ? '횡스크롤' : view === 'topDown' ? '탑다운' : '아이소메트릭'}{' '}
            시점으로 새로 재구성합니다. 단순 좌표 변환이 아닙니다.
          </p>
        ) : null}
        {kind === 'tiles' ? (
          <div className={styles.grid}>
            <div className={styles.field}>
              <label htmlFor="sprite-tile-width">타일 너비</label>
              <Select
                value={String(tileWidth)}
                onValueChange={(value) => setTileWidth(Number(value) as 64 | 128 | 256)}
              >
                <SelectTrigger id="sprite-tile-width">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {[64, 128, 256].map((n) => (
                    <SelectItem key={n} value={String(n)}>
                      {n}px
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className={styles.field}>
              <label htmlFor="sprite-repeat">반복 방향</label>
              <Select value={repeat} onValueChange={(value) => setRepeat(value as SpriteRepeat)}>
                <SelectTrigger id="sprite-repeat">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="x">{view === 'isometric' ? '격자 X축' : '가로'}</SelectItem>
                  <SelectItem value="y">{view === 'isometric' ? '격자 Y축' : '세로'}</SelectItem>
                  <SelectItem value="both">양쪽</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        ) : null}
        {kind === 'tiles' ? (
          <p className={styles.hint}>
            {view === 'isometric' ? '2:1 다이아몬드 · 격자의 두 축 기준' : '정사각 격자'} · 최종
            타일 픽셀과 모델 요청 크기는 다릅니다.
          </p>
        ) : null}
        {providers.isLoading ? (
          <p role="status">공급자를 불러오는 중…</p>
        ) : providers.isError ? (
          <div role="alert">
            <p className={styles.error}>
              {apiErrorMessage(providers.error, '공급자를 불러올 수 없습니다')}
            </p>
            <Button variant="outline" onClick={() => void providers.refetch()}>
              공급자 다시 조회
            </Button>
          </div>
        ) : (
          <>
            <div className={styles.grid}>
              <ProviderSelect
                providers={providers.textProviders}
                value={textProvider?.id ?? null}
                onChange={(id) => {
                  setTextId(id)
                  setTextModel(null)
                }}
                label="분석 공급자"
                fieldId="sprite-text-provider"
                testId="sprite-text-provider"
              />
              <ModelSelect
                {...textModels}
                value={textModel?.id ?? null}
                onChange={setTextModel}
                label="분석 모델"
                fieldId="sprite-text-model"
                testId="sprite-text-model"
              />
            </div>
            <div className={styles.grid}>
              <ProviderSelect
                providers={providers.imageProviders}
                value={imageProvider?.id ?? null}
                onChange={(id) => {
                  setImageId(id)
                  setImageModel(null)
                }}
                label="이미지 공급자"
                fieldId="sprite-image-provider"
                testId="sprite-image-provider"
                emptyLabel="이미지 공급자"
              />
              <ModelSelect
                {...imageModels}
                value={imageModel?.id ?? null}
                onChange={setImageModel}
                label="이미지 모델"
                fieldId="sprite-image-model"
                testId="sprite-image-model"
              />
            </div>
            {imageModel && !supported ? (
              <p role="alert" className={styles.error}>
                투명 지원과 양수 생성 크기가 확인된 이미지 모델이 필요합니다. 현재 모델로 시작할 수
                없습니다.
              </p>
            ) : null}
            {supported ? (
              <p className={styles.hint}>
                지원 생성 크기: {capability.sizes.map((s) => `${s.width}×${s.height}`).join(', ')} ·
                결과 크기는 분석 후 확정
              </p>
            ) : null}
          </>
        )}
      </section>
      {uploadError ? (
        <p role="alert" className={styles.error}>
          {uploadError}
        </p>
      ) : null}
      {start.error ? (
        <div role="alert">
          <p className={styles.error}>
            {apiErrorStatus(start.error) === 409
              ? '접수 충돌입니다. 입력을 확인하고 새 작업으로 다시 시작해 주세요. '
              : ''}
            {apiErrorMessage(start.error, '접수 응답을 받지 못했습니다')}
          </p>
          {(apiErrorStatus(start.error) === null ||
            apiErrorStatus(start.error) === 0 ||
            (apiErrorStatus(start.error) ?? 0) >= 500) &&
          start.variables ? (
            <Button
              variant="outline"
              disabled={busy}
              onClick={() =>
                start.mutate(start.variables!, {
                  onSuccess: (receipt) => navigate(spriteBackgroundJobPath(receipt.id)),
                })
              }
            >
              같은 접수 요청 재전송
            </Button>
          ) : null}
        </div>
      ) : null}
      <Button
        disabled={
          busy ||
          providers.isError ||
          !view ||
          !kind ||
          (!file && !source) ||
          !supported ||
          !textModel ||
          !!textModels.errorMessage ||
          !!imageModels.errorMessage
        }
        onClick={() => void submit()}
      >
        {busy ? '접수 중…' : '2D 분석 시작'}
      </Button>
    </div>
  )
}
