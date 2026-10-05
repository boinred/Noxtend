/**
 * Design Ref: character-studio §6.1 · 계획 §D-08 — 파츠 힌트 입력(그룹 접이식).
 *
 * 카테고리 그룹마다 접이식 섹션(`<details>`), 각 행 `[포함 토글] 파츠명 [변형 select] [개수 스테퍼]`.
 * 규칙(개수 클램프·단일 고정·변형 폴백)은 `partHintSelection` 이 갖고, 여기서는 렌더와 상태
 * 갱신만 한다. 전송 페이로드는 부모가 `buildPartHints(PART_TAXONOMY, selection)` 로 만든다.
 *
 * 접근성(DESIGN.md): 진행형 공개(접이식), 명시적 라벨·focus-visible, 색 외 상태 표현(토글 텍스트).
 */
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import type { CharacterGender } from '@/domain/job/types'
import { GenderSelect } from './GenderSelect'
import {
  PART_GROUPS,
  PART_GROUP_LABELS,
  taxaByGroup,
  type PartTaxon,
} from './characterPartTaxonomy'
import {
  clampCount,
  defaultRowState,
  type HintRowState,
  type HintSelection,
} from './partHintSelection'

export interface PartHintPanelProps {
  selection: HintSelection
  onChange: (next: HintSelection) => void

  /**
   * 성별 — 파츠 힌트가 아니라 **바디의 속성**이다.
   *
   * 도메인이 "성별은 베이스바디 구성을 가른다"(`Gender.cs`)고 못박고 생성 프롬프트도
   * 베이스바디를 그리는 데 필수로 쓴다. 그래서 독립 필드로 위에 떨어뜨려 두지 않고
   * 바디 그룹 바로 위 한 줄에 붙인다 — 무엇에 딸린 값인지가 자리로 읽힌다.
   *
   * 접이식 안에 넣지 않는 이유는 **필수값**이기 때문이다. 섹션이 접혀 있으면 안 고른
   * 사용자가 왜 시작이 막히는지 알 수 없다.
   */
  gender: CharacterGender | null
  onGenderChange: (gender: CharacterGender) => void
}

export function PartHintPanel({ selection, onChange, gender, onGenderChange }: PartHintPanelProps) {
  // 한 행의 상태를 병합해 갱신한다 — 없던 행은 기본값에서 출발한다
  function updateRow(taxon: PartTaxon, patch: Partial<HintRowState>) {
    const current = selection[taxon.type] ?? defaultRowState(taxon)
    onChange({ ...selection, [taxon.type]: { ...current, ...patch } })
  }

  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="text-[0.8125rem] font-semibold text-muted-foreground">파츠 힌트</legend>
      <p className="text-[0.8125rem] text-muted-foreground">
        넣을 파츠 종류를 고르면 그 종류만 추출합니다. 아무것도 고르지 않으면 자동으로 감지합니다.
      </p>

      {/* 아래 택소노미에 없는 파츠(몬스터 전용 부위 등)는 검수 게이트에서 사각형으로
          직접 추가할 수 있다 — 여기서 못 고른다고 포기하지 않도록 눈에 띄게 알린다 */}
      <p
        className="rounded-[10px] border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-[0.8125rem] text-foreground dark:border-amber-400/40 dark:bg-amber-400/10"
        data-testid="hint-manual-add-notice"
      >
        아래 목록에 없는 파츠는 1차 분석 이후 검수 단계에서 직접 추가할 수 있습니다.
      </p>

      <GenderSelect value={gender} onChange={onGenderChange} />

      {PART_GROUPS.map((group) => {
        const taxa = taxaByGroup(group)
        if (taxa.length === 0) {
          return null
        }

        const includedCount = taxa.filter((taxon) => selection[taxon.type]?.included).length

        return (
          <details key={group} className="rounded-[10px] border border-border bg-card">
            <summary
              data-testid={`hint-group-${group}`}
              className="flex cursor-pointer items-center justify-between px-3 py-2 text-sm font-semibold text-foreground focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1"
            >
              <span>{PART_GROUP_LABELS[group]}</span>
              {/* 색 외 상태 표현 — 접힌 섹션에도 선택 수가 텍스트로 보인다 */}
              {includedCount > 0 && (
                <span className="text-xs font-normal text-muted-foreground">
                  {includedCount}개 선택
                </span>
              )}
            </summary>

            <div className="flex flex-col gap-1 border-t border-border px-3 py-2">
              {taxa.map((taxon) => (
                <HintRow
                  key={taxon.type}
                  taxon={taxon}
                  state={selection[taxon.type] ?? defaultRowState(taxon)}
                  onToggle={(included) => updateRow(taxon, { included })}
                  onVariant={(variant) => updateRow(taxon, { variant })}
                  onCount={(count) => updateRow(taxon, { count: clampCount(count) })}
                />
              ))}
            </div>
          </details>
        )
      })}
    </fieldset>
  )
}

interface HintRowProps {
  taxon: PartTaxon
  state: HintRowState
  onToggle: (included: boolean) => void
  onVariant: (variant: string) => void
  onCount: (count: number) => void
}

function HintRow({ taxon, state, onToggle, onVariant, onCount }: HintRowProps) {
  const rowId = `hint-${taxon.type}`

  return (
    <div className="py-1">
      <div className="flex items-center gap-2">
        {/* 포함 토글 — 명시적 라벨과 연결 */}
        <input
          id={rowId}
          type="checkbox"
          checked={state.included}
          onChange={(event) => onToggle(event.target.checked)}
          className="size-4 accent-primary focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1"
        />
        <label htmlFor={rowId} className="text-sm text-foreground">
          {taxon.type}
        </label>

        {/*
          변형 배타 선택 — 이름 바로 옆에 둔다. 오른쪽 끝에 밀어두면 왼쪽 이름을 보는
          동안 무엇을 고르는 중인지 눈에 안 들어온다
        */}
        {taxon.variants && state.included && (
          <Select value={state.variant ?? taxon.variants[0]} onValueChange={onVariant}>
            <SelectTrigger className="h-7 w-28 text-xs" aria-label={`${taxon.type} 변형`}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {taxon.variants.map((variant) => (
                <SelectItem key={variant} value={variant}>
                  {variant}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}

        {/*
          개수 스테퍼 — 다중 파츠만, 포함됐을 때만. 단일 파츠는 1 고정이라 노출하지 않는다.
          변형 드롭다운과 같은 이유로 이름 옆에 붙인다 — 오른쪽 끝으로 밀면 무엇의 개수를
          고치는 중인지 눈이 따라가지 못한다
        */}
        {taxon.multiple && state.included && (
          <div
            className="inline-flex items-center gap-1"
            role="group"
            aria-label={`${taxon.type} 개수`}
          >
            <button
              type="button"
              onClick={() => onCount(state.count - 1)}
              disabled={state.count <= 1}
              aria-label="개수 줄이기"
              className="size-6 rounded-md border border-border text-sm disabled:opacity-45 focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1"
            >
              −
            </button>
            <span className="w-6 text-center text-sm tabular-nums" aria-live="polite">
              {state.count}
            </span>
            <button
              type="button"
              onClick={() => onCount(state.count + 1)}
              disabled={state.count >= 20}
              aria-label="개수 늘리기"
              className="size-6 rounded-md border border-border text-sm disabled:opacity-45 focus-visible:outline-2 focus-visible:outline-primary focus-visible:outline-offset-1"
            >
              +
            </button>
          </div>
        )}

        <div className="flex-1" />
      </div>

      {/* 왜 이렇게 나뉘는지 — 3D 모델러가 "발목형/무릎형" 만 보고 고를 수는 없다 */}
      {taxon.note ? (
        <p className="ml-6 text-xs text-muted-foreground" data-testid="hint-note">
          {taxon.note}
        </p>
      ) : null}
    </div>
  )
}
