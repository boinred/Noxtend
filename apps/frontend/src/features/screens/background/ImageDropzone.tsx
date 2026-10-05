/**
 * Design Ref: §5.1 · §5.4 — 드래그 앤 드롭 + 클릭 선택.
 *
 * `<button>` 을 쓴 것이 A11y 대응이다. `<div onClick>` 이었다면 키보드로 파일을 고를
 * 방법이 없다 — 버튼은 Enter·Space 를 공짜로 준다 (§5.4 A11y).
 *
 * 검증은 `domain/job/rules` 가 한다. 여기서 조건을 다시 쓰면 서버·도메인·화면 셋이 된다.
 */
import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent, DragEvent } from 'react'
import { Icon } from '@/features/shell/Icon'
import { sourceImageUrl } from '@/app/queries/media'
import {
  ALLOWED_IMAGE_TYPES,
  UPLOAD_HINT,
  uploadRejectionMessage,
  validateUpload,
} from '@/domain/job/rules'
import { backgroundStyles as styles } from './backgroundStyles'
import type { UploadRejection } from '@/domain/job/rules'

export interface ImageDropzoneProps {
  file: File | null

  /**
   * 이미 올라간 이미지를 이어받는다 — "다시 분석" 이 이 값을 넘긴다.
   *
   * 새로 고른 파일이 있으면 그쪽이 이긴다. 이어받은 이미지를 두고 다른 것을 고르는
   * 것이 자연스러운 순서이기 때문이다.
   */
  reusedImageId?: string | null

  onSelect: (file: File | null) => void
}

export function ImageDropzone({ file, reusedImageId, onSelect }: ImageDropzoneProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)
  const [rejection, setRejection] = useState<UploadRejection | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)

  // objectURL 은 해제하지 않으면 페이지가 살아 있는 동안 누적된다
  useEffect(() => {
    if (!file) {
      setPreviewUrl(null)
      return
    }

    const url = URL.createObjectURL(file)
    setPreviewUrl(url)

    return () => URL.revokeObjectURL(url)
  }, [file])

  function accept(candidate: File | undefined) {
    if (!candidate) return

    const failure = validateUpload({ type: candidate.type, size: candidate.size })
    setRejection(failure)

    // 거부된 파일은 이전 선택도 지운다 — 반쯤 유효한 상태를 남기지 않는다
    onSelect(failure ? null : candidate)
  }

  function handleDrop(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault()
    setDragging(false)
    accept(event.dataTransfer.files[0])
  }

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    accept(event.target.files?.[0])
    // 같은 파일을 다시 고를 수 있게 초기화한다. 안 하면 change 가 안 뜬다
    event.target.value = ''
  }

  // 새로 고른 파일이 우선. 없으면 이어받은 이미지를 보여준다
  const shownUrl = previewUrl ?? (reusedImageId ? sourceImageUrl(reusedImageId) : null)
  const isReused = previewUrl === null && Boolean(reusedImageId)

  return (
    <div>
      <button
        type="button"
        className={styles.dropzone}
        data-dragging={dragging}
        data-testid="image-dropzone"
        onClick={() => inputRef.current?.click()}
        onDragOver={(event) => {
          event.preventDefault()
          setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
        aria-label="이미지를 끌어다 놓거나 클릭해 선택"
      >
        {shownUrl ? (
          <>
            <img className={styles.preview} src={shownUrl} alt="" data-testid="image-preview" />
            {/* 이어받은 것인지 새로 고른 것인지 말해준다 — 안 그러면 왜 이미 차 있는지 모른다 */}
            <span className={styles.fileName} data-testid="dropzone-caption">
              {isReused ? '직전 이미지를 다시 씁니다 · 눌러서 바꿀 수 있습니다' : file?.name}
            </span>
          </>
        ) : (
          <>
            <Icon name="upload" size={28} />
            <span>이미지를 끌어다 놓거나 클릭해 선택</span>
            <span className={styles.dropzoneHint}>{UPLOAD_HINT}</span>
          </>
        )}
      </button>

      <input
        ref={inputRef}
        type="file"
        accept={ALLOWED_IMAGE_TYPES.join(',')}
        hidden
        onChange={handleChange}
        data-testid="image-input"
      />

      {rejection ? (
        <p className={styles.rejection} role="alert" data-testid="upload-rejection">
          {uploadRejectionMessage(rejection)}
        </p>
      ) : null}
    </div>
  )
}
