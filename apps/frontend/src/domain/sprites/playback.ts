export function frameAt(timeMs: number, fps: number, frameCount: number): number {
  return Math.floor((timeMs * fps) / 1000) % frameCount
}
