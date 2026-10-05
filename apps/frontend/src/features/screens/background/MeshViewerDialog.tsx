/**
 * 3D 결과를 돌려 보는 다이얼로그.
 *
 * Design Ref: §4.4 · §5 · D-08·D-11·D-12
 *
 * **이 파일이 비동기 청크의 경계다.** `MeshTile` 이 `lazy(() => import('./MeshViewerDialog'))`
 * 로만 부르므로, 여기서 딸려 오는 `@google/model-viewer` 는 3D 를 실제로 여는 사람만 받는다.
 * 누군가 이 모듈을 정적으로 import 하면 초기 번들이 무거워지고 `check-bundle.mjs` 가
 * 빌드를 막는다 — 규칙이 문서가 아니라 장치다.
 */
import { useEffect, useRef, useState } from 'react'
import { TopLayerBoundary } from '@/lib/top-layer-boundary'
import { Button } from '@/components/ui/button'
import { meshDownloadUrl } from '@/app/queries/media'
import { backgroundStyles as styles } from './backgroundStyles'
import { MeshDownloadMenu } from './MeshDownloadMenu'
import { ModelViewerCanvas } from './ModelViewerCanvas'
import type { GeneratedMesh } from '@/domain/job/types'

/**
 * WebGL 을 쓸 수 있는가 (D-07 · D-10).
 *
 * **붙이기 전에 본다.** 붙인 뒤 실패를 기다리면 그 사이 빈 사각형이 보이고, 사용자는
 * 결과가 깨진 것으로 읽는다.
 *
 * `getContext` 하나만 보는 것은 의도한 것이다 — E2E 가 결정적으로 끌 수 있는 지점이
 * 하나여야 이 분기를 실제로 검사할 수 있다 (§8.2 F-09).
 */
function hasWebgl(): boolean {
  try {
    const canvas = document.createElement('canvas')
    return canvas.getContext('webgl2') !== null || canvas.getContext('webgl') !== null
  } catch {
    return false
  }
}

export default function MeshViewerDialog({
  mesh,
  partName,
  onClose,
}: {
  mesh: GeneratedMesh
  partName: string
  onClose: () => void
}) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  // 경계 컨텍스트용 — ref 는 첫 렌더에 null 이라 state 여야 제공 시점이 맞는다 (§2.2)
  const [dialogElement, setDialogElement] = useState<HTMLDialogElement | null>(null)

  useEffect(() => {
    // native dialog 기반 포커스 격리·Esc 닫기 — `PartImageCarousel` 과 같은 규약
    dialogRef.current?.showModal()
    setDialogElement(dialogRef.current)
  }, [])

  const close = () => dialogRef.current?.close()
  const webgl = hasWebgl()

  return (
    <dialog
      ref={dialogRef}
      className={styles.meshViewerDialog}
      aria-label={`${partName} 3D 보기`}
      onClose={onClose}
      onClick={(event) => {
        // dialog 외부 영역 클릭 닫기
        if (event.target === event.currentTarget) close()
      }}
      data-testid="mesh-viewer-dialog"
    >
      {/* 팝업 포털의 목적지 — 없으면 메뉴가 top layer 아래 깔린다 (Plan §1 실측) */}
      <TopLayerBoundary value={dialogElement}>
        <div className={styles.meshViewerBody}>
          <div className={styles.meshViewerHeader}>
            <div className={styles.meshViewerHeading}>
              <span className={styles.partImageDialogTitle}>{partName}</span>
              {/* **보고 있는 것이 GLB 라는 사실을 적는다** (D-12) — 없으면 "왜 FBX 는
                안 보이지" 를 묻게 된다 */}
              <span className={styles.partCardMeta}>GLB</span>
            </div>
            <div className={styles.meshViewerActions}>
              <MeshDownloadMenu mesh={mesh} partName={partName} />
              <Button
                type="button"
                variant="secondary"
                size="icon-sm"
                onClick={close}
                aria-label="3D 보기 닫기"
              >
                <span aria-hidden="true">×</span>
              </Button>
            </div>
          </div>

          {webgl ? (
            <ModelViewerCanvas src={meshDownloadUrl(mesh.id)} alt={`${partName} 3D 모델`} />
          ) : (
            <div className={styles.meshViewerFallback} data-testid="mesh-viewer-unsupported">
              <p>이 브라우저에서는 3D 를 볼 수 없습니다.</p>
              {/* **내려받기는 남긴다** — 볼 수 없는 것과 받을 수 없는 것은 다르다 */}
              <p>내려받아 확인해 주세요.</p>
            </div>
          )}

          {mesh.hasFbx ? (
            <p className={styles.meshViewerNotice}>
              FBX 는 이 뷰어에서 볼 수 없습니다. 내려받아 엔진에서 확인해 주세요.
            </p>
          ) : null}
        </div>
      </TopLayerBoundary>
    </dialog>
  )
}
