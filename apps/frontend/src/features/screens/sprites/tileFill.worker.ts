import { fillTiles } from '@/domain/sprites/reconstruction'

// 고정 { name, message } 형식, 사용자 정의 Error 하위 클래스는 structured clone 미보존
self.onmessage = (event: MessageEvent<Parameters<typeof fillTiles>[0]>) => {
  try {
    self.postMessage({ fill: fillTiles(event.data) })
  } catch (error) {
    const { name, message } = error instanceof Error ? error : new Error(String(error))
    self.postMessage({ error: { name, message } })
  }
}
