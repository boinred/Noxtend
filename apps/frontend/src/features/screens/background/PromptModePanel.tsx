/**
 * Design Ref: §5.1 · §5.4 — 프롬프트 모드는 **껍데기다**.
 *
 * 입력창과 비활성 버튼만 둔다. 자리를 지금 잡는 이유는 탭이 두 개라는 사실이
 * 화면 구조의 일부이기 때문이고, 동작을 넣지 않는 이유는 이번 범위 밖이기 때문이다 (§2.2 Out of Scope).
 */
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { backgroundStyles as styles } from './backgroundStyles'

export function PromptModePanel() {
  const [prompt, setPrompt] = useState('')

  return (
    <div data-testid="prompt-mode-panel">
      <div className={styles.field}>
        <label className={styles.label} htmlFor="studio-prompt">
          장면 설명
        </label>
        <textarea
          id="studio-prompt"
          className={styles.textarea}
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          placeholder="예: 해질녘 항구 마을. 낮은 채도의 청록과 주황."
          data-testid="prompt-input"
        />
      </div>

      <div className={styles.actions}>
        <span className={styles.dropzoneHint} data-testid="prompt-coming-soon">
          프롬프트로 배경을 만드는 기능은 준비 중입니다
        </span>
        <Button variant="default" disabled data-testid="prompt-generate">
          생성
        </Button>
      </div>
    </div>
  )
}
