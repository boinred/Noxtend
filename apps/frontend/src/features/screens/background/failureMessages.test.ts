import { describe, expect, it } from 'vitest'
import { formatFailureReason } from './failureMessages'

describe('작업 실패 안내', () => {
  it.each(['HttpRequestException', 'PROVIDER_CALL_FAILED'])(
    '외부 전송 실패 %s를 재시도 안내로 바꾼다',
    (reason) => {
      expect(formatFailureReason(reason)).toBe(
        '외부 API와 보안 연결이 일시적으로 끊겼습니다. 잠시 후 다시 시도해 주세요.',
      )
    },
  )

  it('도메인 실패 코드는 진단을 위해 유지한다', () => {
    expect(formatFailureReason('SCENE_INCOMPLETE')).toBe('SCENE_INCOMPLETE')
  })

  it('실패 사유가 없으면 기본 안내를 보여준다', () => {
    expect(formatFailureReason(null, '생성 실패')).toBe('생성 실패')
  })
})
