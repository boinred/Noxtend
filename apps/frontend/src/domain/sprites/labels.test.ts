import { describe, expect, it } from 'vitest'
import {
  spriteOutputKindLabel,
  spriteOutputLabel,
  spriteRepeatLabel,
  spriteViewLabel,
} from './labels'

describe('sprite labels', () => {
  it('matches the input screen wording', () => {
    expect(spriteViewLabel('sideView')).toBe('횡스크롤')
    expect(spriteViewLabel('topDown')).toBe('탑다운')
    expect(spriteViewLabel('isometric')).toBe('아이소메트릭')
  })

  it('names repeat axes by view', () => {
    expect(spriteRepeatLabel('x', 'isometric')).toBe('격자 X축')
    expect(spriteRepeatLabel('y', 'isometric')).toBe('격자 Y축')
    expect(spriteRepeatLabel('x', 'topDown')).toBe('가로')
    expect(spriteRepeatLabel('y', 'sideView')).toBe('세로')
    expect(spriteRepeatLabel('both', 'isometric')).toBe('양쪽')
  })

  it('names the output kinds', () => {
    expect(spriteOutputKindLabel('layers')).toBe('배경 레이어')
    expect(spriteOutputKindLabel('tiles')).toBe('반복 타일')
  })

  it('describes the output kind', () => {
    expect(
      spriteOutputLabel({ view: 'sideView', outputKind: 'layers', tileWidth: 128, repeat: 'both' }),
    ).toBe('배경 레이어')
    expect(
      spriteOutputLabel({ view: 'isometric', outputKind: 'tiles', tileWidth: 64, repeat: 'x' }),
    ).toBe('반복 타일 · 64px · 격자 X축')
  })
})
