/**
 * Design Ref: §5.4 관리자 — 목록.
 *
 * **화면 어디에도 키 평문이 없다.** 서버가 마스킹만 내려보내므로 (§4.2 #9) 여기서
 * 할 일은 그것을 그대로 보여주는 것뿐이다 — 복원하거나 복사하는 동작을 두지 않는다.
 */
import { Button } from '@/components/ui/button'
import { providerKindLabel } from '@/domain/provider/types'
import { adminStyles as styles } from './adminStyles'
import { ProviderCapabilityBadge } from './ProviderCapabilityBadge'
import type { Provider } from '@/domain/provider/types'
import type { ProviderTestResult } from '@/app/queries/useProviders'

export interface ProviderListProps {
  providers: Provider[]
  testingId: string | null
  testResults: Record<string, ProviderTestResult | { error: string }>
  onEdit: (provider: Provider) => void
  onDelete: (provider: Provider) => void
  onTest: (provider: Provider) => void
}

/**
 * 값이 있을 때만 한 줄을 만든다.
 *
 * **`=== null` 로는 부족하다.** 응답에 그 열이 아예 없으면 `undefined` 인데 그것을
 * 통과시키면 `undefined개` 를 적거나, 숫자 메서드를 부르다 화면이 통째로 죽는다.
 * 지원하지 않는 기능은 `null`, 낡은 응답은 `undefined` — 둘 다 "할 말 없음" 이다.
 */
function say(value: number | null | undefined, format: (n: number) => string): string | null {
  return typeof value === 'number' ? format(value) : null
}

export function ProviderList({
  providers,
  testingId,
  testResults,
  onEdit,
  onDelete,
  onTest,
}: ProviderListProps) {
  if (providers.length === 0) {
    return (
      <div className={styles.panel}>
        <p className={styles.empty} data-testid="provider-list-empty">
          등록된 공급자가 없습니다. 추가하면 스튜디오에서 고를 수 있습니다.
        </p>
      </div>
    )
  }

  return (
    <div className={styles.panel}>
      <table className={styles.table} data-testid="provider-table">
        <thead>
          <tr>
            <th>이름</th>
            <th>종류</th>
            <th>사용 용도</th>
            <th>키</th>
            <th>상태</th>
            <th aria-label="동작" />
          </tr>
        </thead>
        <tbody>
          {providers.map((provider) => (
            <tr key={provider.id} data-testid="provider-row">
              <td>{provider.displayName}</td>
              <td>{providerKindLabel(provider.kind)}</td>
              <td>
                <div className={styles.capabilityList}>
                  {provider.capabilities.map((capability) => (
                    <ProviderCapabilityBadge
                      key={capability}
                      capability={capability}
                      testId="provider-capability"
                    />
                  ))}
                </div>
              </td>
              <td>
                <span className={styles.maskedKey} data-testid="provider-key-masked">
                  {provider.apiKeyMasked}
                </span>
              </td>
              <td>
                <span className={styles.state} data-enabled={provider.isEnabled}>
                  <span className={styles.stateDot} />
                  {provider.isEnabled ? '사용' : '중지'}
                </span>
              </td>
              <td>
                <div className={styles.actions}>
                  <div className={styles.actionRow}>
                    <Button
                      variant="secondary"
                      onClick={() => onTest(provider)}
                      disabled={testingId === provider.id}
                      data-testid="provider-test"
                    >
                      {/* 진행 표시가 버튼 자체에 있어야 어느 행을 확인 중인지 분명하다 */}
                      {testingId === provider.id ? '확인 중…' : '연결 확인'}
                    </Button>
                    <Button
                      variant="secondary"
                      onClick={() => onEdit(provider)}
                      data-testid="provider-edit"
                    >
                      수정
                    </Button>
                    <Button
                      variant="destructive"
                      onClick={() => onDelete(provider)}
                      data-testid="provider-delete"
                    >
                      삭제
                    </Button>
                  </div>
                  {/* 버튼 **뒤**에 온다. 같은 줄에 두면 긴 메시지가 열 밖으로 넘쳐
                      상태 열을 침범한다 — 실측으로 40px 넘침을 확인했다 */}
                  {renderTestResult(provider.id, testResults)}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * 결과를 행 안에 두는 이유는 공급자가 여럿일 때 어느 것의 결과인지 분명해야 하기 때문이다.
 *
 * 진행 중 표시는 여기 없다 — 버튼 라벨이 맡는다. 별도 문구로 두면 같은 정보가 두 곳에
 * 생기고, 버튼은 "연결 확인" 이라 적힌 채 비활성이라 무슨 일이 일어나는지 흐려진다.
 */
function renderTestResult(providerId: string, results: ProviderListProps['testResults']) {
  const result = results[providerId]
  if (!result) return null

  if ('error' in result) {
    return (
      <span
        className={styles.testResult}
        data-ok="false"
        role="status"
        data-testid="provider-test-result"
      >
        실패 · {result.error}
      </span>
    )
  }

  // Capability-specific connection evidence
  const evidence = [
    say(result.textModelCount, (n) => `텍스트 모델 ${n}개`),
    say(result.imageModelCount, (n) => `이미지 모델 ${n}개`),
    // 3D 는 이 자리가 비어 있었다 — 공급자가 붙었는데 증거가 없는 상태였다
    say(result.meshModelCount, (n) => `3D 모델 ${n}개`),
    // **잔액은 세는 값이 아니라 남은 양이다.** 파츠 하나가 크레딧 30 이라,
    // 모르고 시작하면 작업 도중에 멈춘다. 못 읽었으면 아무 말도 하지 않는다 —
    // 모르는 것을 0 으로 적으면 사용자가 다 썼다고 읽는다
    say(result.meshCreditBalance, (n) => `크레딧 ${n.toLocaleString('ko-KR')}`),
  ].filter((item): item is string => item !== null)

  return (
    <span
      className={styles.testResult}
      data-ok="true"
      role="status"
      data-testid="provider-test-result"
    >
      정상 · {evidence.join(' · ')} · {result.latencyMs}ms
    </span>
  )
}
