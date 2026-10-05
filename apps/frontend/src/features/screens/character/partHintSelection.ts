/**
 * Design Ref: character-studio §6.2 · 계획 §D-08 — 힌트 선택 상태와 전송 페이로드 변환.
 *
 * **순수 로직만 둔다.** 그룹 접이식 UI(`PartHintPanel`)는 이 함수들로 상태를 바꾸고
 * `PartHint[]` 를 만든다 — 렌더와 규칙을 분리해 규칙만 단위 테스트한다(코드베이스 관례).
 */
import type { PartHint } from '@/domain/job/types'
import type { PartTaxon } from './characterPartTaxonomy'

/** 힌트 개수 범위 — 도메인 배치 규칙·접수 검증과 같은 1~20 (character-studio §D-02). */
const MIN_COUNT = 1
const MAX_COUNT = 20

/** 한 행의 선택 상태. */
export interface HintRowState {
  included: boolean
  count: number
  /** 변형이 있는 항목에서만 의미가 있다. */
  variant?: string
}

/** 종류 키(taxon.type) → 행 상태. */
export type HintSelection = Record<string, HintRowState>

/** 개수를 1~20 정수로 자른다 — 스테퍼 입력·직접 입력 양쪽을 방어한다. */
export function clampCount(count: number): number {
  return Math.min(MAX_COUNT, Math.max(MIN_COUNT, Math.floor(count)))
}

/** 기본 행 상태 — 포함 해제·개수 1, 변형이 있으면 첫 변형을 고른다. */
export function defaultRowState(taxon: PartTaxon): HintRowState {
  return { included: false, count: 1, variant: taxon.variants?.[0] }
}

/**
 * 선택 상태를 전송 페이로드로 변환한다.
 *
 * 포함된 항목만 담고, 단일 파츠(`multiple:false`)는 개수를 1 로 고정한다. 변형이 있는
 * 항목은 변형을 싣되 상태에 없으면 첫 변형으로 폴백하고, 변형이 없는 항목은 `variant` 를
 * 넣지 않는다 — wire 에 빈 필드를 만들지 않는다.
 */
export function buildPartHints(
  taxonomy: readonly PartTaxon[],
  selection: HintSelection,
): PartHint[] {
  const hints: PartHint[] = []

  for (const taxon of taxonomy) {
    const row = selection[taxon.type]
    if (!row?.included) {
      continue
    }

    const count = taxon.multiple ? clampCount(row.count) : 1
    const hint: PartHint = { type: taxon.type, count }

    // 변형 축이 있는 파츠만 variant 를 싣는다. 상태에 없으면 첫 변형으로 폴백
    if (taxon.variants && taxon.variants.length > 0) {
      hint.variant = row.variant ?? taxon.variants[0]
    }

    hints.push(hint)
  }

  return hints
}

/**
 * 전송 페이로드를 선택 상태로 되돌린다 — "다시 시도" 이어받기의 입력(character-mesh-ui §FR-08).
 *
 * `buildPartHints` 의 역함수다. 택소노미에 없는 종류(옛 job이 지금은 없는 종류를 들고 있는
 * 경우)는 조용히 건너뛴다 — 선택 UI 가 그릴 수 없는 행을 만들면 화면이 깨진다.
 */
export function selectionFromPartHints(
  taxonomy: readonly PartTaxon[],
  hints: readonly PartHint[],
): HintSelection {
  const known = new Set(taxonomy.map((taxon) => taxon.type))
  const selection: HintSelection = {}

  for (const hint of hints) {
    if (!known.has(hint.type)) {
      continue
    }

    selection[hint.type] = { included: true, count: hint.count, variant: hint.variant }
  }

  return selection
}
