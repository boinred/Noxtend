import { generationTally, meshTally, type Job } from '@/domain/job/types'
import type { JobProgressSummary } from './JobProgressPanel'

export function productionProgressSummaries(job: Job, imageLabel: string): JobProgressSummary[] {
  const images = generationTally(job)
  const meshes = meshTally(job)
  const summaries: JobProgressSummary[] = [
    {
      id: 'analysis',
      label: '원본 분석 성공',
      value: `${job.tasks.some((task) => task.kind === 'analyze' && task.status === 'succeeded') ? 1 : 0} / 1`,
      hint: '원본 이미지 분석 성공 수 / 원본 이미지 수',
      icon: 'eye',
    },
    {
      id: 'images',
      label: imageLabel,
      value: images.total > 0 ? `${images.generated} / ${images.total}` : '대상 확인 중',
      hint: '현재 방향 이미지 수 / 계획된 생성 슬롯 수',
      icon: 'image',
    },
  ]
  if (job.models.mesh !== null || meshes.planned > 0) {
    summaries.push({
      id: 'meshes',
      label: '3D 모델 생성 성공',
      value: meshes.planned > 0 ? `${meshes.ready} / ${meshes.planned}` : '대상 확인 중',
      hint: '준비된 모델 수 / 실제 제작 대상 파츠 수',
      icon: 'cube',
    })
  }
  return summaries
}
