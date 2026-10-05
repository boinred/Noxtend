import { providerCapabilityLabel } from '@/domain/provider/types'
import { adminStyles as styles } from './adminStyles'
import type { ProviderCapability } from '@/domain/provider/types'

const CAPABILITY_MARKS: Record<ProviderCapability, string> = {
  textAnalysis: 'T',
  imageGeneration: '◈',
  meshGeneration: '⬢',
  similarityEvaluation: '≈',
}

interface ProviderCapabilityBadgeProps {
  capability: ProviderCapability
  testId?: string
}

export function ProviderCapabilityBadge({ capability, testId }: ProviderCapabilityBadgeProps) {
  return (
    <span className={styles.capabilityBadge} data-capability={capability} data-testid={testId}>
      <span className={styles.capabilityMark} data-capability-mark aria-hidden="true">
        {CAPABILITY_MARKS[capability]}
      </span>
      {providerCapabilityLabel(capability)}
    </span>
  )
}
