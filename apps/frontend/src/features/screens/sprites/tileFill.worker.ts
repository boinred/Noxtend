import { fillTiles } from '@/domain/sprites/reconstruction'

// Error 하위 클래스는 postMessage 에서 사라져 name 으로 전달
self.onmessage = (event: MessageEvent<Parameters<typeof fillTiles>[0]>) => {
  try {
    self.postMessage({ fill: fillTiles(event.data) })
  } catch (error) {
    const { name, message } = error instanceof Error ? error : new Error(String(error))
    self.postMessage({ error: { name, message } })
  }
}
