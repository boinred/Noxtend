/**
 * 서술 확인 단계 화면 계산 (review-gate-staged 사이클 1) — 렌더와 떼어 단위 검증.
 */
import type { Bounds, ReviewPart } from '@/domain/job/types'

/** 다시 쓴 파츠를 상단으로. 같은 그룹 안은 서버 순서 유지(안정 정렬). */
export function sortForDescriptionReview(parts: ReviewPart[]): ReviewPart[] {
  return [...parts].sort(
    (a, b) =>
      Number(b.descriptionSource === 'rewritten') - Number(a.descriptionSource === 'rewritten'),
  )
}

/** 서술이 빈 파츠 — 생성 시작 비활성·행 강조 기준. */
export function emptyDescriptionParts(parts: ReviewPart[]): ReviewPart[] {
  return parts.filter((part) => !part.description?.trim())
}

/** 행 칩 문구 — 직접 추가(상자 출처)와 서술 출처를 함께. */
export function descriptionChips(part: ReviewPart): string[] {
  const chips: string[] = []
  if (part.source === 'manual') chips.push('직접 추가')
  if (part.descriptionSource === 'rewritten') chips.push('다시 씀')
  if (part.descriptionSource === 'human') chips.push('직접 수정')
  return chips
}

export interface ThumbnailStyle {
  width: number
  height: number
  backgroundSize: string
  backgroundPosition: string
}

/**
 * 원본 이미지를 상자 영역만 보이게 CSS 배경으로 크롭한다. 서버 크롭 없음.
 *
 * `imageAspect` = 원본 가로/세로 — 정규화 좌표 비율에 곱해야 실제 픽셀 비율이 된다.
 * 긴 변을 `max` 에 맞춘다.
 */
export function thumbnailStyle(box: Bounds, imageAspect: number, max: number): ThumbnailStyle {
  const ratio = (box.w * imageAspect) / box.h
  const width = ratio >= 1 ? max : max * ratio
  const height = ratio >= 1 ? max / ratio : max

  // background-position % 는 (컨테이너 - 이미지) 기준 — 남는 폭이 0 이면 위치 무의미
  const position = (offset: number, size: number) => (size >= 1 ? 0 : (offset / (1 - size)) * 100)

  return {
    width,
    height,
    backgroundSize: `${100 / box.w}% ${100 / box.h}%`,
    backgroundPosition: `${position(box.x, box.w)}% ${position(box.y, box.h)}%`,
  }
}

/** 색 입력·스포이트의 소문자 hex 를 서버 형식(대문자)으로. */
export function normalizeHex(hex: string): string {
  return hex.toUpperCase()
}
