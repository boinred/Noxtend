/**
 * Design Ref: character-studio §6.2 · §D-05 — 파츠 택소노미 정본(프론트 상수).
 *
 * 힌트 UI 가 이 상수를 렌더하고 전송 페이로드(`PartHint[]`)를 구성한다. 같은 목록을 캐릭터
 * Extract 시드 프롬프트의 문장이 참조한다 — 백본은 택소노미를 모른다(§2.4).
 *
 * **enum 으로 굳히지 않는다**(§D-05). 택소노미는 튜닝 데이터라 조정될 여지가 크고, 지금은
 * 프론트·프롬프트가 같은 정본을 보고 쓰게만 한다. 통합은 안정 후 별도 과제(§11-2).
 */

/** 그룹 — 접이식 섹션 하나에 해당한다. */
export type PartGroup = 'base' | 'head' | 'top' | 'shoes' | 'weapon' | 'accessory'

/** 그룹 순서 = 화면 표시 순서. */
export const PART_GROUPS: readonly PartGroup[] = [
  'base',
  'head',
  'top',
  'shoes',
  'weapon',
  'accessory',
]

/** 그룹 화면 표기. */
export const PART_GROUP_LABELS: Record<PartGroup, string> = {
  // 화면 표기는 "바디" 다. 추출 프롬프트가 모델에게 쓰는 용어("베이스바디")와는 별개다 —
  // 그쪽은 프롬프트 계약이라 바꾸려면 새 버전을 시드해야 한다
  base: '바디',
  head: '머리',
  top: '의상',
  shoes: '신발',
  weapon: '무기/방어구',
  accessory: '악세사리',
}

/**
 * 택소노미 한 항목.
 *
 * `type` 은 전송되는 종류 키이자 화면 표기다(한국어). `variants` 가 있으면 배타 select 로
 * 하나를 고른다. `multiple` 이 false 면 개수 스테퍼를 1 로 고정한다 — 개수(multiplicity)와
 * 분리(separation)는 다른 축이라(계획 §D-05), 다중이 실제 의미 있는 파츠에서만 개수를 받는다.
 */
export interface PartTaxon {
  group: PartGroup
  type: string
  variants?: readonly string[]
  multiple: boolean
  /**
   * 왜 이 기준으로 나뉘는지 한 줄.
   *
   * **선택지 이름만으로 알 수 있으면 달지 않는다.** "민머리/단발형/장발형" 은 이름이
   * 곧 설명이고, 거기 문장을 덧붙이면 읽을 것만 늘어난다. "발목형/무릎형" 처럼 나누는
   * 기준(관절)이 이름에 안 드러날 때만 쓴다.
   */
  note?: string
}

/**
 * 초기 택소노미. **최종 확정이 아니다**(계획 §11) — 프롬프트·데이터로 조정한다.
 *
 * 개수를 받는 파츠(반지·팔찌 등)만 `multiple: true`. 형태가 나뉘는 파츠(신발·장갑 등)만
 * `variants`. 표면 특징(눈·코·문신)은 분리하지 않으므로(§D-04) 택소노미에 없다.
 */
export const PART_TAXONOMY: readonly PartTaxon[] = [
  // 베이스바디 — 각 파츠가 독립 3D 에셋이라 몸통도 강제하지 않고 선택 파츠로 둔다.
  // 몸통을 고르면 성별에 따라 속옷·실루엣이 달라진다. 꼬리·날개·뿔은 비인간 확장 부위
  { group: 'base', type: '몸통', multiple: false },
  // 비인간 캐릭터(수인·마족·천사 등)용. 이름만으로 용도가 드러나 화면 설명은 달지 않는다
  { group: 'base', type: '꼬리', multiple: false },
  { group: 'base', type: '날개', variants: ['한 쌍형', '여러 쌍형'], multiple: false },
  { group: 'base', type: '뿔', multiple: true },

  // 머리 — 얼굴~목은 별개 파츠, 머리카락도 별개(§D-04)
  { group: 'head', type: '머리', multiple: false },
  // 짧은머리·민머리가 없어 짧은 머리 캐릭터를 힌트로 표현할 수 없었다 (실사용 관측, 2026-09-01)
  {
    group: 'head',
    type: '머리카락',
    // 민머리는 머리카락 파츠가 아예 안 나오는 경우다 — 삭발형과 같은 뜻이라 하나만 둔다
    // 선택지 이름이 곧 설명이라 note 를 달지 않는다 — 신발·장갑처럼 "왜 이 기준으로
    // 나뉘는지" 가 안 보이는 경우에만 붙인다
    variants: ['민머리', '짧은머리', '단발형', '장발형', '묶음형'],
    multiple: false,
  },

  // 의상
  { group: 'top', type: '상의', multiple: false },
  { group: 'top', type: '하의', multiple: false },
  { group: 'top', type: '겉옷', multiple: false },
  { group: 'top', type: '원피스', multiple: false },

  // 신발 — 길이 변형
  {
    group: 'shoes',
    type: '신발',
    variants: ['발목형', '무릎형'],
    multiple: false,
    // 관절 위아래를 따로 뽑아야 3D 에서 그 관절이 접힌다 — 하나로 뽑으면 무릎이 안 굽는다
    note: '덮는 길이. 발목이나 무릎에서 파츠를 나눠야 그 관절이 움직인다',
  },

  // 무기/방어구 — "갑옷"은 별도 힌트에서 제외한다(2026-08-18). 상의·하의가 덮는 부위와
  // 그대로 겹쳐서, "갑옷"을 상의·하의와 같이 힌트로 주면 같은 부위가 두 파츠(의상 파츠 +
  // 갑주 조각)로 중복 렌더되는 문제가 실사용에서 반복 확인됐다. 다만 갑옷 자체는 3D
  // 리깅 가치가 있는 별도 오브젝트라, 힌트는 없애되 백엔드 Extract 프롬프트에서 상의·
  // 하의 힌트 범위 안에 있으면 갑주 조각을 자동으로 서브파츠로 분리하도록 남겨뒀다
  // (가방 힌트가 몸체/스트랩/버클로 자동 분리되는 것과 같은 방식) — 이 파일에서 "갑옷"을
  // 없애는 것과는 별개로 SeedPrompts.cs가 처리한다.
  { group: 'weapon', type: '무기', multiple: true },
  { group: 'weapon', type: '방패', multiple: false },

  // 악세사리 — 개수가 실제 의미 있는 것들. "벨트"는 여기(힌트 목록)에는 없지만
  // 힌트 없이도 항상 분리되는 파츠다(2026-08-28 반전) — "갑옷"과 같은 방식으로,
  // 상의·하의·원피스 힌트 범위 안에서 백엔드 Extract 프롬프트(SeedPrompts.cs)가
  // 자동으로 별도 파츠("벨트")로 뽑고, 그 옷 파츠는 벨트 없이 서술·생성된다. 힌트로
  // 벨트를 따로 켜고 끌 필요가 없어 이 목록에는 없다(갑옷과 같은 이유, 위 §75-81 참고).
  // 예전에는 벨트를 표면 장식으로 흡수해 별도 파츠 자체를 없앴었는데, 하의 재생성 시
  // 벨트 없는 이미지가 필요하다는 요구로 다시 갑옷과 같은 자동 분리 대상으로 되돌렸다.
  { group: 'accessory', type: '반지', multiple: true },
  { group: 'accessory', type: '팔찌', multiple: true },
  { group: 'accessory', type: '목걸이', multiple: true },
  { group: 'accessory', type: '귀걸이', multiple: true },
  {
    group: 'accessory',
    type: '장갑',
    variants: ['손목형', '팔꿈치형'],
    multiple: false,
    note: '덮는 길이. 손목이나 팔꿈치에서 파츠를 나눠야 그 관절이 움직인다',
  },
  { group: 'accessory', type: '가방', multiple: false },
  { group: 'accessory', type: '모자', multiple: false },
]

/** 그룹별 택소노미 — UI 가 섹션마다 렌더한다. */
export function taxaByGroup(group: PartGroup): PartTaxon[] {
  return PART_TAXONOMY.filter((taxon) => taxon.group === group)
}
