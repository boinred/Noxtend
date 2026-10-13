import { sourceImageUrl } from '@/app/queries/media'
import { Icon } from '@/features/shell/Icon'

export interface JobThumbnailProps {
  sourceImageId: string | null
  className: string
  lazy?: boolean
}

// 원본 확정 전 2D 작업은 이미지가 없어 같은 크기의 자리 표시를 둔다
export function JobThumbnail({ sourceImageId, className, lazy }: JobThumbnailProps) {
  if (sourceImageId === null) {
    return (
      <span
        className={`${className} grid shrink-0 place-items-center text-[var(--text-dim)]`}
        data-testid="job-thumb-placeholder"
        aria-hidden="true"
      >
        <Icon name="image" size={20} />
      </span>
    )
  }
  return (
    <img
      className={className}
      src={sourceImageUrl(sourceImageId)}
      alt=""
      loading={lazy ? 'lazy' : undefined}
    />
  )
}
