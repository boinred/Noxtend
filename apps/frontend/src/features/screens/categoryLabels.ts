/**
 * Design Ref: §3.4 — 카테고리 키 ↔ 라벨 ↔ 경로의 단일 지점.
 *
 * `AssetCategory` 의 키는 API 계약과 같은 `background` 를 사용하고 표시 라벨만 `배경` 으로 쓴다.
 *
 * sidebar-layout §10.2 — 경로는 `ROUTES` 를 통해서만 참조한다.
 * 리터럴을 두면 경로를 바꿀 때 이 파일이 누락된다.
 * 한 줄 설명(`description`)은 홈 카드가 사라지면서 소비자를 잃어 제거했다.
 */
import { ASSET_CATEGORIES, assetCategoryLabel } from '@/domain/job/types'
import { ROUTES } from '@/routes/paths'
import type { JobSummary } from '@/domain/job/types'
import { productionModeOf } from '@/domain/sprites/rules'
import type { SpritePhase } from '@/domain/sprites/types'
import type { AssetCategory } from '@/domain/job/types'
import type { IconName } from '@/features/shell/Icon'

export interface CategoryMeta {
  category: AssetCategory
  /** 화면에 보이는 이름 */
  label: string
  icon: IconName
  /** 이 카테고리 전용 화면의 경로 */
  path: string
  /** 이번 사이클에서 내용이 비어 있는가 (Plan §1.4 결정 3) */
  comingSoon: boolean
}

/** 라벨은 도메인에서, 경로는 라우팅 계층에서 온다. 여기서는 아이콘만 덧붙인다. */
const META: Record<AssetCategory, CategoryMeta> = {
  character: {
    category: 'character',
    label: assetCategoryLabel('character'),
    icon: 'user',
    path: ROUTES.character,
    // character-studio 사이클에서 실제 화면이 생겼다. ComingSoonScreen 자동 라우트에서 빠진다
    comingSoon: false,
  },
  object: {
    category: 'object',
    label: assetCategoryLabel('object'),
    icon: 'cube',
    path: ROUTES.object,
    comingSoon: true,
  },
  background: {
    category: 'background',
    label: assetCategoryLabel('background'),
    icon: 'globe',
    path: ROUTES.background,
    // background-studio 사이클에서 실제 화면이 생겼다. ComingSoonScreen 라우트도 함께 빠진다
    comingSoon: false,
  },
}

export const CATEGORY_META_LIST: readonly CategoryMeta[] = ASSET_CATEGORIES.map(
  (category) => META[category],
)

export function categoryMeta(category: AssetCategory): CategoryMeta {
  return META[category]
}

// 라벨 자체가 필요하면 도메인의 `categoryLabel` 을 직접 쓴다.
// 여기서 재수출하면 같은 이름이 두 곳에 생겨 어느 쪽이 정본인지 흐려진다.

/** 경로로부터 카테고리를 되찾는다. ComingSoonScreen 이 어떤 항목인지 판별할 때 쓴다. */
export function categoryFromPath(path: string): AssetCategory | null {
  const found = CATEGORY_META_LIST.find((meta) => meta.path === path)
  return found?.category ?? null
}

export function jobCategoryLabel(job: Pick<JobSummary, 'category' | 'productionMode'>): string {
  return `${productionModeOf(job) === 'twoD' ? '2D' : '3D'} ${assetCategoryLabel(job.category)}`
}

export function jobSummaryCount(job: JobSummary): string {
  return productionModeOf(job) === 'twoD' && job.sprite
    ? `에셋 ${job.sprite.assetCount}개 · 승인 ${job.sprite.approvedAssetCount}개 · 누적 이미지 ${job.sprite.imageCount}개`
    : `파츠 ${job.partCount}개`
}

export function spritePhaseLabel(phase: SpritePhase): string {
  return {
    analyzing: '제작 대상 분석',
    planReview: '제작 계획 검수',
    baseGeneration: '기준 이미지 생성',
    baseReview: '기준 이미지 검수',
    frameGeneration: '애니메이션 프레임 생성',
    frameReview: '애니메이션 검수',
    exportReady: '내보내기 대기',
    packaging: '패키징',
    completed: '완료',
  }[phase]
}
