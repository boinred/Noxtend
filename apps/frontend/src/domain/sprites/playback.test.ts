import { describe, expect, it } from 'vitest'
import { frameAt } from './playback'

describe('frameAt', () => {
  it('wraps eight frames at eight fps without duplicating the end', () => {
    expect(frameAt(0, 8, 8)).toBe(0)
    expect(frameAt(875, 8, 8)).toBe(7)
    expect(frameAt(1000, 8, 8)).toBe(0)
  })
  it('uses each asset fps and keeps a static frame still', () => {
    expect(frameAt(500, 4, 8)).toBe(2)
    expect(frameAt(500, 8, 8)).toBe(4)
    expect(frameAt(1500, 30, 1)).toBe(0)
  })
})
