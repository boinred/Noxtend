/**
 * Design Ref: §5.1 · §5.4 — `/admin` 공급자 관리.
 *
 * 표이므로 `content-max`(1280) 다 (§5.0).
 */
import { useState } from 'react'
import { PageContainer } from '@/features/screens/PageContainer'
import { AdminTabs } from './AdminTabs'
import { Button } from '@/components/ui/button'
import { useProviderMutations, useProviders } from '@/app/queries/useProviders'
import { apiErrorMessage } from '@/app/queries/errors'
import { ProviderForm } from './ProviderForm'
import { ProviderList } from './ProviderList'
import { adminStyles as styles } from './adminStyles'
import type { Provider, ProviderInput } from '@/domain/provider/types'
import type { ProviderTestResult } from '@/app/queries/useProviders'

type FormState = { mode: 'closed' } | { mode: 'create' } | { mode: 'edit'; provider: Provider }

export function AdminScreen() {
  const { providers, isLoading } = useProviders()
  const { create, update, remove, test } = useProviderMutations()

  const [form, setForm] = useState<FormState>({ mode: 'closed' })
  const [testingId, setTestingId] = useState<string | null>(null)
  const [testResults, setTestResults] = useState<
    Record<string, ProviderTestResult | { error: string }>
  >({})

  function handleSubmit(input: ProviderInput) {
    if (form.mode === 'edit') {
      update.mutate(
        { id: form.provider.id, input },
        { onSuccess: () => setForm({ mode: 'closed' }) },
      )
      return
    }

    create.mutate(input, { onSuccess: () => setForm({ mode: 'closed' }) })
  }

  function handleDelete(provider: Provider) {
    // 삭제는 되돌릴 수 없고, 이 공급자를 쓰던 작업은 다시 실행할 수 없게 된다
    if (!window.confirm(`'${provider.displayName}' 공급자를 삭제할까요?`)) return

    remove.mutate(provider.id)
  }

  function handleTest(provider: Provider) {
    setTestingId(provider.id)

    test.mutate(provider.id, {
      onSuccess: (result) => setTestResults((prev) => ({ ...prev, [provider.id]: result })),
      onError: (error) =>
        setTestResults((prev) => ({
          ...prev,
          // 공급자 원문이 아니라 정규화된 메시지다 (§4.2 #13)
          [provider.id]: { error: apiErrorMessage(error, '오류') },
        })),
      onSettled: () => setTestingId(null),
    })
  }

  const pending = create.isPending || update.isPending
  const formError = errorMessage(form.mode === 'edit' ? update.error : create.error)

  return (
    <PageContainer
      width="max"
      title="AI 공급자"
      subtitle="스튜디오에서 고를 수 있는 공급자를 등록합니다. 키는 저장 후 다시 볼 수 없습니다."
      testId="admin-screen"
    >
      <AdminTabs />

      <div className={styles.toolbar}>
        <span />
        <Button
          variant="default"
          onClick={() => setForm({ mode: 'create' })}
          disabled={form.mode === 'create'}
          data-testid="provider-add"
        >
          + 추가
        </Button>
      </div>

      {form.mode !== 'closed' ? (
        <ProviderForm
          // key 를 바꿔 폼 상태를 새로 만든다. 없으면 수정 대상을 바꿔도 이전 값이 남는다
          key={form.mode === 'edit' ? form.provider.id : 'create'}
          editing={form.mode === 'edit' ? form.provider : undefined}
          pending={pending}
          error={formError}
          onSubmit={handleSubmit}
          onCancel={() => setForm({ mode: 'closed' })}
        />
      ) : null}

      {isLoading ? null : (
        <ProviderList
          providers={providers}
          testingId={testingId}
          testResults={testResults}
          onEdit={(provider) => setForm({ mode: 'edit', provider })}
          onDelete={handleDelete}
          onTest={handleTest}
        />
      )}
    </PageContainer>
  )
}

function errorMessage(error: unknown): string | null {
  return error ? apiErrorMessage(error, '저장하지 못했습니다') : null
}
