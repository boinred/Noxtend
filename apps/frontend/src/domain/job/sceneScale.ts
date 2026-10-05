export interface ScaleReferenceView {
  object: string
  realWorldSize: string
  heightMeters: number | null
}

/** 구조화 미터 높이 우선, 과거 자연어 설명 폴백. */
export function formatScaleReference(scale: ScaleReferenceView): string {
  const size =
    scale.heightMeters !== null ? `높이 ${scale.heightMeters}m (구조화 기준)` : scale.realWorldSize

  return `${scale.object} — ${size}`
}
