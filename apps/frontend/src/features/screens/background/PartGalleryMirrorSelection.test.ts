import { describe, expect, it } from 'vitest'
import { axisSelectionError, deriveReplanPlans } from './PartGallery'

// 파츠별 "3D 전송 뷰 자유 선택 + 대칭" 체크박스 상태 → LeftRightPlan/BackPlan 파생 (spec 20260917)
describe('deriveReplanPlans', () => {
  it('아무것도 안 켜면 둘 다 skip', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: false,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: false,
      }),
    ).toEqual({ leftRight: 'skip', back: 'skip' })
  })

  it('좌우 둘 다 사용 체크 → both', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: true,
        rightUsed: true,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: false,
      }).leftRight,
    ).toBe('both')
  })

  it('Left만 사용 체크 → left', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: true,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: false,
      }).leftRight,
    ).toBe('left')
  })

  it('Left 카드의 좌우대칭을 켜면 사용 체크박스 값과 무관하게 mirrorFromLeft', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: false,
        rightUsed: true,
        backUsed: false,
        mirrorLeftRight: 'left',
        mirrorBack: false,
      }).leftRight,
    ).toBe('mirrorFromLeft')
  })

  it('Right 카드의 좌우대칭을 켜면 mirrorFromRight', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: false,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'right',
        mirrorBack: false,
      }).leftRight,
    ).toBe('mirrorFromRight')
  })

  it('Back 사용 체크 → include', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: false,
        rightUsed: false,
        backUsed: true,
        mirrorLeftRight: 'none',
        mirrorBack: false,
      }).back,
    ).toBe('include')
  })

  it('전후대칭 켜면 사용 체크와 무관하게 mirrorFromFront', () => {
    expect(
      deriveReplanPlans({
        frontUsed: true,
        leftUsed: false,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: true,
      }).back,
    ).toBe('mirrorFromFront')
  })
})

// 정면 꺼짐 + 다른 축 켜짐은 유효성 오류 (spec 20260917 프론트 UI 설계)
describe('axisSelectionError', () => {
  it('정면 켜짐이면 다른 축이 뭐든 유효하다', () => {
    expect(
      axisSelectionError({
        frontUsed: true,
        leftUsed: true,
        rightUsed: true,
        backUsed: true,
        mirrorLeftRight: 'left',
        mirrorBack: true,
      }),
    ).toBeNull()
  })

  it('정면 꺼짐 + 나머지도 전부 꺼짐이면 유효하다 (이 파츠는 이번엔 3D 안 만듦)', () => {
    expect(
      axisSelectionError({
        frontUsed: false,
        leftUsed: false,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: false,
      }),
    ).toBeNull()
  })

  it('정면 꺼짐 + 좌측 사용 켜짐이면 오류', () => {
    expect(
      axisSelectionError({
        frontUsed: false,
        leftUsed: true,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: false,
      }),
    ).not.toBeNull()
  })

  it('정면 꺼짐 + 전후대칭 켜짐이면 오류', () => {
    expect(
      axisSelectionError({
        frontUsed: false,
        leftUsed: false,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'none',
        mirrorBack: true,
      }),
    ).not.toBeNull()
  })

  it('정면 꺼짐 + 좌우대칭 켜짐이면 오류', () => {
    expect(
      axisSelectionError({
        frontUsed: false,
        leftUsed: false,
        rightUsed: false,
        backUsed: false,
        mirrorLeftRight: 'left',
        mirrorBack: false,
      }),
    ).not.toBeNull()
  })
})
