/**
 * `<model-viewer>` 를 React 안에서 쓰는 얇은 래퍼.
 *
 * Design Ref: §4.3 · D-10 · Plan D-01
 *
 * **이 파일은 비동기 청크에만 있어야 한다.** `@google/model-viewer` 를 여기서 정적으로
 * import 하므로, 이 모듈을 정적으로 부르는 코드가 생기면 초기 번들이 무거워지고
 * `scripts/check-bundle.mjs` 가 빌드를 실패시킨다.
 */
import { useEffect, useRef, useState } from 'react'
import '@google/model-viewer'
import { backgroundStyles as styles } from './backgroundStyles'

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace React.JSX {
    interface IntrinsicElements {
      'model-viewer': React.DetailedHTMLProps<React.HTMLAttributes<HTMLElement>, HTMLElement>
    }
  }
}

type Phase = 'loading' | 'ready' | 'failed'

/** 사용자가 움직임을 줄여 달라고 했는가 (FR-12). */
function prefersReducedMotion(): boolean {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
}

export function ModelViewerCanvas({ src, alt }: { src: string; alt: string }) {
  const ref = useRef<HTMLElement>(null)
  const [phase, setPhase] = useState<Phase>('loading')

  useEffect(() => {
    const element = ref.current
    if (!element) return

    // **속성을 JSX 로 넘기지 않고 직접 단다.**
    //
    // React 19 는 custom element 의 알 수 없는 prop 을 요소의 프로퍼티로 넘기려 하는데,
    // 실측에서 `src`·`alt`·`loading` 이 속성으로도 프로퍼티로도 실리지 않았다 —
    // 요소는 그려지고 예외도 없이 **소스 없이 빈 화면**으로 남았다. 조용한 무동작이다.
    //
    // 여기서 직접 달면 React 의 custom element 규약이 어떻든 결과가 같다.
    element.setAttribute('alt', alt)

    // `eager` 가 없으면 아무것도 안 받는다 — model-viewer 는 요소가 화면에 보일 때만
    // 모델을 받는데, 다이얼로그가 열리는 순간에는 교차 판정이 아직 나지 않아 조용히
    // 빈 화면으로 머문다 (실측으로 확인)
    element.setAttribute('loading', 'eager')
    element.setAttribute('camera-controls', '')
    element.setAttribute('shadow-intensity', '1')

    if (prefersReducedMotion()) {
      element.removeAttribute('auto-rotate')
    } else {
      element.setAttribute('auto-rotate', '')
    }

    // **소스를 마지막에 단다** — 앞의 설정이 끝난 뒤에 받기 시작해야 한다
    element.setAttribute('src', src)

    // **custom element 의 이벤트는 React 의 onX 규약을 타지 않는다.** 직접 듣는다
    const onLoad = () => setPhase('ready')
    const onError = () => setPhase('failed')

    element.addEventListener('load', onLoad)
    element.addEventListener('error', onError)

    return () => {
      element.removeEventListener('load', onLoad)
      element.removeEventListener('error', onError)
    }
  }, [src, alt])

  if (phase === 'failed') {
    return (
      <div className={styles.meshViewerFallback} data-testid="mesh-viewer-failed">
        <p>3D 를 불러오지 못했습니다.</p>
        <p>내려받아 확인해 주세요.</p>
      </div>
    )
  }

  return (
    <div className={styles.meshViewerStage}>
      {/* 속성은 전부 위 effect 가 단다 — React 의 custom element 규약에 기대지 않는다 */}
      <model-viewer ref={ref} className={styles.meshViewerModel} data-testid="mesh-viewer-model" />

      {phase === 'loading' ? (
        <p className={styles.meshViewerLoading} data-testid="mesh-viewer-loading">
          3D 를 불러오는 중…
        </p>
      ) : null}
    </div>
  )
}
