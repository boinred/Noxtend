/**
 * Design Ref: download-view-consistency §2 — top-layer escape hatch.
 *
 * A native `<dialog>.showModal()` paints in the browser top layer, above every
 * z-index. Popups that portal to `document.body` (Radix menus, tooltips) end up
 * BENEATH it — present in the DOM, invisible on screen, and unclickable.
 *
 * A dialog publishes its own element here; popup portals target it instead of
 * `document.body`, landing inside the same top layer. Outside any provider the
 * value is null and portals behave exactly as before.
 */
import { createContext, useContext } from 'react'

const TopLayerBoundaryContext = createContext<HTMLElement | null>(null)

export const TopLayerBoundary = TopLayerBoundaryContext.Provider

export function useTopLayerBoundary(): HTMLElement | null {
  return useContext(TopLayerBoundaryContext)
}
