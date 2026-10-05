/**
 * Design Ref: §8.3 L1-F #1·#2 — 업로드 검증.
 *
 * 서버의 `UploadRulesTests` 와 짝을 이룬다. 두 벌인 것이 의도이며 (§3.4),
 * 규칙이 어긋나면 사용자가 통과한 파일을 서버가 거부하는 형태로 드러난다.
 */
import { describe, expect, it } from 'vitest'
import { MAX_IMAGE_BYTES, validateUpload } from './rules'

describe('validateUpload', () => {
  // #1 — 형식 화이트리스트
  it.each(['image/png', 'image/jpeg', 'image/webp'])('허용 형식을 통과시킨다: %s', (type) => {
    expect(validateUpload({ type, size: 1024 })).toBeNull()
  })

  it('대소문자가 달라도 허용한다', () => {
    expect(validateUpload({ type: 'IMAGE/PNG', size: 1024 })).toBeNull()
  })

  it.each(['image/gif', 'image/svg+xml', 'application/pdf', 'text/html', ''])(
    '화이트리스트 밖 형식을 거부한다: %s',
    (type) => {
      expect(validateUpload({ type, size: 1024 })).toBe('UNSUPPORTED_TYPE')
    },
  )

  // #2 — 크기 상한 · 빈 파일
  it('빈 파일을 거부한다', () => {
    expect(validateUpload({ type: 'image/png', size: 0 })).toBe('EMPTY')
  })

  it('상한을 넘는 파일을 거부한다', () => {
    expect(validateUpload({ type: 'image/png', size: MAX_IMAGE_BYTES + 1 })).toBe('TOO_LARGE')
  })

  it('상한과 정확히 같은 크기는 통과시킨다', () => {
    expect(validateUpload({ type: 'image/png', size: MAX_IMAGE_BYTES })).toBeNull()
  })

  it('형식과 크기가 모두 틀리면 빈 파일을 먼저 알린다', () => {
    // 서버와 같은 순서다. 내용이 없으면 형식은 부차적이다
    expect(validateUpload({ type: 'application/pdf', size: 0 })).toBe('EMPTY')
  })
})
