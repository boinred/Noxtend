import { spriteStyles as styles } from './spriteStyles'

export interface SpritePromptPanelProps {
  prompt: string
  onPromptChange: (prompt: string) => void
}

export function SpritePromptPanel({ prompt, onPromptChange }: SpritePromptPanelProps) {
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
    </div>
  )
}
