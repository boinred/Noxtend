/**
 * Design Ref: §3.4 — 화면이 공통 헤더에 자기 컨트롤을 주입하는 통로.
 *
 * 헤더 내용은 화면마다 다르다. 캔버스만 카테고리 탭·모드 뱃지·실행 버튼을 갖고
 * 나머지 4개는 비어 있다. 이 차이를 헤더 안의 조건 분기가 아니라 구조로 표현한다.
 * 화면이 늘어도 헤더 코드는 그대로다 (§2.0 Rationale).
 *
 * 슬롯 DOM 요소를 ref 가 아니라 **state** 로 들고 있어야 한다.
 * ref 는 첫 렌더에 null 이고, 나중에 채워져도 재렌더가 일어나지 않아
 * 포털이 영영 그려지지 않는다. state setter 를 ref 콜백으로 넘기면
 * 요소가 붙는 순간 재렌더가 일어나 포털이 생긴다.
 */
import { createContext, useContext, useState } from 'react'
import { createPortal } from 'react-dom'
import type { ReactNode } from 'react'

const SlotElementContext = createContext<HTMLElement | null>(null)
const SlotRefContext = createContext<((element: HTMLElement | null) => void) | null>(null)

export function HeaderSlotProvider({ children }: { children: ReactNode }) {
  const [element, setElement] = useState<HTMLElement | null>(null)

  return (
    <SlotElementContext value={element}>
      <SlotRefContext value={setElement}>{children}</SlotRefContext>
    </SlotElementContext>
  )
}

/** 헤더가 슬롯 자리를 등록할 때 쓰는 ref 콜백 */
export function useHeaderSlotRef(): (element: HTMLElement | null) => void {
  const setElement = useContext(SlotRefContext)
  return setElement ?? (() => {})
}

/**
 * 화면이 헤더에 컨트롤을 넣는다. 언마운트 시 React 가 알아서 걷어간다 (§4.3 #5).
 * 슬롯이 아직 없으면 아무것도 그리지 않는다 (§6).
 */
export function HeaderSlot({ children }: { children: ReactNode }) {
  const element = useContext(SlotElementContext)
  if (!element) return null
  return createPortal(children, element)
}
