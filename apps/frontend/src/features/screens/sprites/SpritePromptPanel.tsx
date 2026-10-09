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
      <div className={styles.row}>
        <Button
          variant="outline"
          data-testid="sprite-prompt-generate"
          disabled={!canGenerate}
          onClick={onGenerate}
        >
          {generating ? '생성 중…' : '기준 이미지 생성'}
        </Button>
      </div>
      {generating ? (
        <p role="status" className={styles.hint}>
          생성 중에 페이지를 떠나면 결과를 다시 볼 수 없습니다
        </p>
      ) : null}
      {error ? (
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
