import { Icon } from '@/features/shell/Icon'
import './spritePromptProgress.css'
import { Button } from '@/components/ui/button'
import type { UploadResult } from '@/app/queries/useUpload'
import { sourceImageUrl } from '@/app/queries/media'
import { spriteStyles as styles } from './spriteStyles'

export interface SpritePromptPanelProps {
  prompt: string
  onPromptChange: (prompt: string) => void
  results: readonly UploadResult[]
  selectedId: string | null
  onSelect: (id: string) => void
  canGenerate: boolean
  generating: boolean
  error: string | null
  onGenerate: () => void
}

export function SpritePromptPanel({
  prompt,
  onPromptChange,
  results,
  selectedId,
  onSelect,
  canGenerate,
  generating,
  error,
  onGenerate,
}: SpritePromptPanelProps) {
  return (
    <div className={styles.stack}>
      <div className={styles.field}>
        <label htmlFor="sprite-prompt">장면 설명</label>
        <textarea
          id="sprite-prompt"
          data-testid="sprite-prompt-input"
          className={styles.textarea}
          value={prompt}
          onChange={(event) => onPromptChange(event.target.value)}
          placeholder="예: 해질녘 항구 마을. 낮은 채도의 청록과 주황."
          aria-describedby="sprite-prompt-count"
        />
        <span id="sprite-prompt-count" className={styles.hint}>
          {prompt.trim().length}/1000
        </span>
      </div>
      {generating ? (
        <div className={styles.promptProgress}>
          <div className={styles.promptProgressHeading}>
            <div className={styles.promptProgressIcon}>
              <Icon name="image" size={20} />
            </div>
            <div className="min-w-0">
              <p role="status" className="text-sm font-semibold leading-normal text-foreground">
                기준 이미지 생성 중
              </p>
              <p className={styles.promptProgressDetail}>설명한 장면을 이미지로 만들고 있습니다.</p>
            </div>
          </div>
          <div
            className={styles.promptProgressTrack}
            role="progressbar"
            aria-label="기준 이미지 생성 중"
          >
            <div className={styles.promptProgressSegment} />
          </div>
          <p className={styles.promptProgressNotice}>
            <Icon name="info" size={14} className="mt-0.5 shrink-0" />
            <span>이 화면을 유지해 주세요. 페이지를 떠나면 생성 결과를 다시 볼 수 없습니다.</span>
          </p>
        </div>
      ) : (
        <div className={styles.row}>
          <Button
            variant="outline"
            data-testid="sprite-prompt-generate"
            disabled={!canGenerate}
            onClick={onGenerate}
          >
            기준 이미지 생성
          </Button>
        </div>
      )}
      {error && !generating ? (
        <p role="alert" className={styles.error}>
          {error}
        </p>
      ) : null}
      <div className={styles.results}>
        {results.map((result, index) => (
          <button
            key={result.id}
            type="button"
            data-testid="sprite-prompt-result"
            className={styles.result}
            aria-pressed={selectedId === result.id}
            aria-label={`생성 결과 ${index + 1} 선택`}
            onClick={() => onSelect(result.id)}
          >
            <img src={sourceImageUrl(result.id)} alt="" className={styles.image} />
          </button>
        ))}
      </div>
    </div>
  )
}
