/**
 * 파츠 한 줄의 3D 자리 — 타일과 내려받기 메뉴.
 *
 * Design Ref: §4.1 · Plan D-02·D-03
 *
 * **타일은 이미지다.** 파츠가 여덟이면 행도 여덟인데, 행마다 살아 있는 WebGL 컨텍스트를
 * 두면 브라우저 상한(대개 16개)에 가깝게 붙고 목록을 스크롤하는 것만으로 GPU 를 태운다.
 * 누르는 순간에만 컨텍스트가 하나 생긴다.
 *
 * **내려받기는 여기 없다.** 목록에 형식 메뉴까지 세우면 행마다 누를 것이 둘이 되는데,
 * 받기 전에 결과를 보는 것이 자연스러운 순서다. 뷰어 안에 같은 메뉴가 있다.
 */
import { Suspense, lazy, useState } from 'react'
import { meshPreviewUrl } from '@/app/queries/media'
import { backgroundStyles as styles } from './backgroundStyles'
import type { GeneratedMesh } from '@/domain/job/types'

/**
 * **비동기 경계.** 3D 를 실제로 여는 사람만 model-viewer 를 받는다 (D-08).
 *
 * 이것을 정적 import 로 바꾸면 초기 번들이 무거워지고 `check-bundle.mjs` 가 빌드를 막는다.
 */
const MeshViewerDialog = lazy(() => import('./MeshViewerDialog'))

export function MeshTile({ mesh, partName }: { mesh: GeneratedMesh; partName: string }) {
  const [isViewerOpen, setViewerOpen] = useState(false)

  return (
    <div className={styles.meshResult} data-testid="mesh-result">
      <button
        type="button"
        className={styles.meshTileButton}
        onClick={() => setViewerOpen(true)}
        aria-label={`${partName} 3D 보기`}
        aria-haspopup="dialog"
        data-testid="mesh-tile"
      >
        {/* 미리보기가 없어도 누를 수 있어야 한다 (FR-10) — 자리표시도 버튼 안에 있다 */}
        {mesh.hasPreview ? (
          <img
            className={styles.meshTileImage}
            src={meshPreviewUrl(mesh.id)}
            alt=""
            loading="lazy"
          />
        ) : (
          <span className={styles.meshTilePlaceholder}>3D</span>
        )}
        <span className={styles.meshTileHint} aria-hidden="true">
          돌려 보기
        </span>
      </button>

      {/*
        **닫으면 언마운트된다** (NFR-03). 감추기만 하면 WebGL 컨텍스트가 살아 있어
        여닫을수록 탭이 무거워진다
      */}
      {isViewerOpen ? (
        <Suspense fallback={null}>
          <MeshViewerDialog mesh={mesh} partName={partName} onClose={() => setViewerOpen(false)} />
        </Suspense>
      ) : null}
    </div>
  )
}
