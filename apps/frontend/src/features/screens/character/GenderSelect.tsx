/**
 * Design Ref: character-studio §6.1 — 성별 배타 선택(남/여).
 *
 * 캐릭터는 성별이 필수라(§D-01) 기본 선택을 두지 않고 사용자가 명시적으로 고르게 한다 —
 * 기본값을 두면 안 고른 것과 고른 것이 구분되지 않는다. 색 외에 `aria-pressed` 와
 * 선택 표식(✓)으로 상태를 표현하고 라벨을 명시한다(DESIGN.md).
 *
 * **칩 형태다.** 이전에는 라벨을 위에 얹은 세로 배치의 분절 컨트롤이었는데, 이 값이
 * "바디" 그룹 바로 위 한 줄에 놓이면서 세로로 두 줄을 쓸 이유가 없어졌다. 라벨과
 * 선택지를 한 줄에 두면 성별이 바디의 속성이라는 것이 자리로도 읽힌다.
 */
import type { CharacterGender } from '@/domain/job/types'

const OPTIONS: readonly { value: CharacterGender; label: string }[] = [
  { value: 'female', label: '여성' },
  { value: 'male', label: '남성' },
]

export interface GenderSelectProps {
  value: CharacterGender | null
  onChange: (gender: CharacterGender) => void
}

export function GenderSelect({ value, onChange }: GenderSelectProps) {
  return (
    <div className="flex items-center gap-3" data-testid="gender-field">
      <span className="text-[0.8125rem] font-semibold text-muted-foreground">성별</span>

      <div
        className="flex flex-wrap gap-2"
        role="group"
        aria-label="성별"
        data-testid="gender-select"
      >
        {OPTIONS.map((option) => {
          const selected = value === option.value

          return (
            <button
              key={option.value}
              type="button"
              aria-pressed={selected}
              onClick={() => onChange(option.value)}
              data-testid={`gender-${option.value}`}
              className="inline-flex cursor-pointer items-center gap-1.5 rounded-full border border-border bg-card px-3.5 py-1.5 text-sm font-medium text-muted-foreground transition-[background,color,border-color] duration-[var(--dur-hover)] ease-[var(--ease-out)] hover:border-primary/40 hover:text-foreground aria-pressed:border-primary aria-pressed:bg-primary/12 aria-pressed:font-semibold aria-pressed:text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1"
            >
              {/* 색 외 상태 표현 — 선택된 칩만 표식을 갖는다 (DESIGN.md) */}
              {selected && (
                <span aria-hidden="true" className="text-xs leading-none text-primary">
                  ✓
                </span>
              )}
              {option.label}
            </button>
          )
        })}
      </div>
    </div>
  )
}
