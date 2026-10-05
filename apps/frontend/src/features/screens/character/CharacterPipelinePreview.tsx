/**
 * Design Ref: character-studio 생성 전 예상 파이프라인 GUI 미리보기.
 * DESIGN.md 지침을 엄격히 준수합니다.
 * - 요약: `0/7 · 시작 전 예상 공정 · 0% 완료`
 * - 색상: mineral neutral, sage green (primary)
 * - 7단계 이상 시 접을 수 있는 상세 목록 제공
 */
import { useState } from 'react'
import { cn } from '@/lib/utils'
import { Icon } from '@/features/shell/Icon'

export interface CharacterPipelinePreviewProps {
  requiresReview: boolean
  producesMeshes: boolean
  className?: string
}

interface StagePreviewItem {
  id: string
  label: string
  enabled: boolean
  note: string
}

// note 는 "이 단계에서 사용자가 할 일" 한 줄이다 — 시스템이 뭘 하는지가 아니라, 사용자가
// 손을 대야 하는지 아닌지가 기준이다. 대부분 자동이고 검수만 실제 조작이 필요하다
export function getPreviewStages(
  requiresReview: boolean,
  producesMeshes: boolean,
): StagePreviewItem[] {
  return [
    { id: 'analyze', label: '장면 분석', enabled: true, note: '자동 진행' },
    { id: 'extract', label: '파츠 추출', enabled: true, note: '자동 진행' },
    { id: 'decompose', label: '파츠 분해', enabled: true, note: '자동 진행' },
    {
      id: 'review',
      label: '파츠 검수',
      enabled: requiresReview,
      note: requiresReview ? '수동검수' : '자동 진행',
    },
    { id: 'rewrite', label: '서술 재작성', enabled: true, note: '자동 진행' },
    { id: 'generate', label: '4방향 생성', enabled: true, note: '자동 진행' },
    {
      id: 'reconstruct',
      label: '3D 재구성',
      enabled: producesMeshes,
      note: producesMeshes ? '자동 진행' : '3D 생략',
    },
  ]
}

export function CharacterPipelinePreview({
  requiresReview,
  producesMeshes,
  className,
}: CharacterPipelinePreviewProps) {
  const stages = getPreviewStages(requiresReview, producesMeshes)
  const enabledCount = stages.filter((s) => s.enabled).length
  const totalCount = stages.length
  const [expanded, setExpanded] = useState(true)

  return (
    <div
      className={cn('mb-6 rounded-xl border border-border bg-card p-4 shadow-xs', className)}
      data-testid="character-pipeline-preview"
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex flex-col gap-0.5">
          <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-primary">
            <Icon name="sparkles" size={14} />
            <span>예상 공정 흐름</span>
          </div>
          <div className="font-mono text-xs text-muted-foreground">
            0/{totalCount} · 시작 전 예상 공정 · 0% 완료 ({enabledCount}개 단계 활성)
          </div>
        </div>
        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-muted-foreground hover:bg-muted transition-colors"
          aria-expanded={expanded}
        >
          <span>{expanded ? '상세 접기' : '상세 보기'}</span>
          <Icon
            name="chevron-down"
            size={14}
            className={cn('transition-transform duration-200', expanded && 'rotate-180')}
          />
        </button>
      </div>

      {/* 프로그레스 트랙 미리보기 (시작 전 0%) */}
      <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div className="h-full w-0 bg-primary transition-all duration-300" />
      </div>

      {expanded ? (
        <div className="mt-3 grid grid-cols-7 gap-1.5 max-[768px]:grid-cols-4 max-[480px]:grid-cols-2">
          {stages.map((stage, idx) => (
            <div
              key={stage.id}
              className={cn(
                'flex flex-col items-center justify-center rounded-lg border p-2 text-center transition-colors duration-160',
                stage.enabled
                  ? 'border-border bg-background text-foreground'
                  : 'border-border/40 bg-muted/30 text-muted-foreground/50 opacity-60',
              )}
              data-testid={`preview-stage-${stage.id}`}
              data-enabled={stage.enabled}
            >
              <div className="flex items-center gap-1 font-mono text-[0.6875rem] text-muted-foreground">
                <span>{idx + 1}.</span>
              </div>
              <span className="text-[0.75rem] font-semibold leading-tight mt-0.5">
                {stage.label}
              </span>
              <span
                className={cn(
                  'mt-1 inline-flex items-center rounded-full px-1.5 py-0.5 text-[0.625rem] font-medium',
                  stage.enabled
                    ? 'bg-primary/10 text-primary'
                    : 'bg-muted text-muted-foreground/60 line-through',
                )}
              >
                {stage.note}
              </span>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  )
}
