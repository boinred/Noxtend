import { describe, expect, it } from 'vitest'
import { canRequestSelectedView, hasPartialViewsWarning } from './PartGallery'
import type { PartCard } from '@/domain/job/types'

// // 선택적 비정면 생성 프론트엔드 UI/UX 흐름 (Slice 3) 도메인 로직 검증
describe('PartGallery Selective View Generation Logic', () => {
  const mockPartCard: PartCard = {
    part: {
      id: 'part-1',
      name: '상의',
      ordinal: 0,
      description: '검은 가죽 재킷',
      category: '상의',
      placements: [],
      depthOrder: 1,
      occludedBy: [],
      generatedImageId: 'img-front',
      generatedImages: [{ id: 'img-front', viewDirection: 'front' }],
      generatedMesh: null,
    },
    views: [
      {
        viewDirection: 'front',
        task: null,
        status: 'succeeded',
        imageId: 'img-front',
        failureReason: null,
      },
      {
        viewDirection: 'right',
        task: null,
        status: 'unplanned',
        imageId: null,
        failureReason: null,
      },
      {
        viewDirection: 'back',
        task: null,
        status: 'unplanned',
        imageId: null,
        failureReason: null,
      },
      {
        viewDirection: 'left',
        task: null,
        status: 'unplanned',
        imageId: null,
        failureReason: null,
      },
    ],
    task: null,
    status: 'succeeded',
    imageId: 'img-front',
    failureReason: null,
  }

  it('정면(Front) 이미지가 준비된 경우 미계획(unplanned) 비정면 타일 생성 요청이 가능하다', () => {
    expect(canRequestSelectedView(mockPartCard, 'right')).toBe(true)
    expect(canRequestSelectedView(mockPartCard, 'back')).toBe(true)
    expect(canRequestSelectedView(mockPartCard, 'left')).toBe(true)
    // 정면 자체는 선택 생성 대상이 아님
    expect(canRequestSelectedView(mockPartCard, 'front')).toBe(false)
  })

  it('정면 이미지가 준비되지 않은 경우 비정면 타일 생성 요청을 차단한다', () => {
    const unreadyCard: PartCard = {
      ...mockPartCard,
      views: mockPartCard.views.map((v) =>
        v.viewDirection === 'front' ? { ...v, imageId: null, status: 'running' } : v,
      ),
    }

    expect(canRequestSelectedView(unreadyCard, 'right')).toBe(false)
  })

  it('준비된 이미지가 2개 이상 4개 미만일 때 4면 미만 경고 뱃지를 활성화한다', () => {
    // Front 1개만 있을 때: 아직 3D 입력 최소 2면 조건 미충족
    expect(hasPartialViewsWarning(mockPartCard)).toBe(false)

    // 2면(Front + Right) 완성 시 4면 미만 경고 뱃지 활성화
    const twoViewsCard: PartCard = {
      ...mockPartCard,
      views: mockPartCard.views.map((v) =>
        v.viewDirection === 'right' ? { ...v, imageId: 'img-right', status: 'succeeded' } : v,
      ),
    }
    expect(hasPartialViewsWarning(twoViewsCard)).toBe(true)

    // 4면 전면 완성 시 경고 뱃지 비활성화
    const fourViewsCard: PartCard = {
      ...mockPartCard,
      views: mockPartCard.views.map((v) => ({
        ...v,
        imageId: `img-${v.viewDirection}`,
        status: 'succeeded',
      })),
    }
    expect(hasPartialViewsWarning(fourViewsCard)).toBe(false)
  })
})
