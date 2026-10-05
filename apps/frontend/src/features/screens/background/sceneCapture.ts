/**
 * 결정적 평가 캡처 (background-similarity-tuning §8).
 *
 * **이 파일은 SceneAssemblyView 의 비동기 청크 안에서만 import 된다** — three 를
 * 정적 import 하므로 정적 경로에 새면 번들 가드가 빌드를 실패시킨다.
 *
 * 현재 화면의 renderer·scene 을 그대로 쓰되 **임시 WebGLRenderTarget 한 프레임**만
 * 그린다. 숨은 두 번째 Canvas 도, 전역 preserveDrawingBuffer 도 쓰지 않는다 —
 * 컨텍스트가 늘면 "Too many active WebGL contexts" 를 다시 부른다 (실측).
 * 카메라는 서버가 저장한 revision 의 수치값으로 만들어 Orbit 상태와 무관하다.
 */
import { DirectionalLight, PerspectiveCamera, Vector3, WebGLRenderTarget } from 'three'
import type { Material, Mesh, Object3D, Scene, WebGLRenderer } from 'three'
import type { SceneNumericCamera, SceneNumericLight } from '@/domain/job/types'

/** 프레임 계약 — 긴 변 1024 · DPR 1 · PNG. 비율은 원본 이미지를 따른다 (사용자 결정,
 * §8.1 의 정사각 고정을 대체: 정사각은 세로형 원본에 레터박스를 만들고 평가가
 * 서로 다른 프레임을 보게 했다). */
export const RENDER_FRAME_SIZE = 1024

/** 짧은 변 하한 — 서버 검증과 같은 값. 극단 비율은 여기서 눌린다. */
export const RENDER_FRAME_MIN = 256

export interface CaptureFrame {
  width: number
  height: number
}

/** 원본 크기 → 캡처 프레임: 긴 변 1024 고정, 비율 유지, 정수·하한 256. */
export function captureFrameFor(width: number, height: number): CaptureFrame {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return { width: RENDER_FRAME_SIZE, height: RENDER_FRAME_SIZE }
  }

  const scale = RENDER_FRAME_SIZE / Math.max(width, height)
  return {
    width: Math.max(RENDER_FRAME_MIN, Math.round(width * scale)),
    height: Math.max(RENDER_FRAME_MIN, Math.round(height * scale)),
  }
}

/** 업로드 상한 (§8.1) — 서버도 같은 값을 다시 검사한다. */
export const MAX_RENDER_BYTES = 8 * 1024 * 1024

/**
 * WebGL 픽셀 읽기는 아래가 원점 — 세로로 뒤집어야 화면과 같은 방향이 된다.
 * 뒤집기가 틀리면 평가 모델이 뒤집힌 장면을 본다.
 */
export function flipPixelsVertically(
  pixels: Uint8Array,
  width: number,
  height: number,
): Uint8ClampedArray<ArrayBuffer> {
  const rowBytes = width * 4
  const flipped = new Uint8ClampedArray(pixels.length)

  for (let row = 0; row < height; row++) {
    const source = pixels.subarray(row * rowBytes, (row + 1) * rowBytes)
    flipped.set(source, (height - 1 - row) * rowBytes)
  }

  return flipped
}

/** 빈 캡처 판정 — 전부 같은 색이거나 alpha 뿐이면 유료 평가에 보낼 그림이 아니다 (§8.2). */
export function hasPixelVariance(pixels: Uint8Array | Uint8ClampedArray): boolean {
  if (pixels.length < 8) return false

  const [r, g, b] = [pixels[0]!, pixels[1]!, pixels[2]!]
  let sawColor = false

  for (let i = 0; i < pixels.length; i += 4) {
    if (pixels[i] !== 0 || pixels[i + 1] !== 0 || pixels[i + 2] !== 0) sawColor = true
    if (pixels[i] !== r || pixels[i + 1] !== g || pixels[i + 2] !== b) return sawColor
  }

  return false
}

/** 업로드 전 크기 검증 — 문제가 없으면 null. */
export function renderBlobProblem(sizeBytes: number): string | null {
  if (sizeBytes <= 0) return '캡처가 비어 있습니다'
  if (sizeBytes > MAX_RENDER_BYTES) return '캡처가 8 MiB 를 넘습니다'
  return null
}

/**
 * 지목 흐리기 상태를 잠시 걷고 그린다 — 캡처에 유령(반투명) 인스턴스가 남으면
 * 평가가 화면에 없는 것을 본다. 재질의 현재 값을 스냅숏해 finally 에서 되돌린다.
 */
function withBaseMaterials<T>(scene: Scene, render: () => T): T {
  const restores: (() => void)[] = []

  scene.traverse((child: Object3D) => {
    if (!(child as { isMesh?: boolean }).isMesh) return
    const mesh = child as unknown as Mesh
    const materials = (Array.isArray(mesh.material) ? mesh.material : [mesh.material]) as Material[]

    for (const material of materials) {
      if (typeof material.userData.baseOpacity !== 'number') continue
      const { opacity, transparent, depthWrite } = material
      restores.push(() => {
        material.opacity = opacity
        material.transparent = transparent
        material.depthWrite = depthWrite
      })
      material.opacity = material.userData.baseOpacity as number
      material.transparent = material.userData.baseTransparent as boolean
      material.depthWrite = true
    }
  })

  try {
    return render()
  } finally {
    for (const restore of restores) restore()
  }
}

/** 후보 revision 의 렌더 재료 (§9.2 4단계) — 화면의 현재 장면은 바꾸지 않는다. */
export interface CaptureOverrides {
  /** instanceKey(`partId-ordinal`) → 후보 변환. 없는 키는 그대로 둔다. */
  instances: Record<
    string,
    {
      position: { x: number; y: number; z: number }
      rotationY: number
      /**
       * 축별 배율 (background-surface-parts #20 §4.2).
       *
       * **단일 값이면 표면이 무너진다** — 균일 배율로 되돌리면 (18.17, 7.88, 29.91) 이
       * (7.88, 7.88, 7.88) 이 되어 평가 렌더가 화면과 달라진다.
       */
      scale: { x: number; y: number; z: number }
    }
  >
  light?: SceneNumericLight
}

/**
 * 오버라이드를 걸고 그린 뒤 originals 로 되돌린다 — 캡처가 화면을 바꾸면 안 된다 (§8.2).
 * 인스턴스 그룹은 userData.instanceKey 로 찾는다.
 */
export function withOverrides<T>(
  scene: Scene,
  overrides: CaptureOverrides | undefined,
  render: () => T,
): T {
  if (!overrides) return render()

  const restores: (() => void)[] = []

  scene.traverse((child) => {
    const key = (child.userData as { instanceKey?: string }).instanceKey
    if (key && overrides.instances[key]) {
      const target = overrides.instances[key]
      const { position, rotation, scale } = child
      const saved = {
        position: position.clone(),
        rotationY: rotation.y,
        scale: scale.clone(),
      }
      restores.push(() => {
        child.position.copy(saved.position)
        child.rotation.y = saved.rotationY
        child.scale.copy(saved.scale)
      })
      child.position.set(target.position.x, target.position.y, target.position.z)
      child.rotation.y = target.rotationY
      child.scale.set(target.scale.x, target.scale.y, target.scale.z)
    }

    if (overrides.light && child instanceof DirectionalLight) {
      const saved = {
        position: child.position.clone(),
        intensity: child.intensity,
        color: child.color.clone(),
      }
      restores.push(() => {
        child.position.copy(saved.position)
        child.intensity = saved.intensity
        child.color.copy(saved.color)
      })
      // 화면과 같은 배치 규칙 — 반지름 14, 장면 중심 z=-4
      const azimuth = (overrides.light.azimuthDegrees * Math.PI) / 180
      const elevation = (overrides.light.elevationDegrees * Math.PI) / 180
      child.position.set(
        14 * Math.cos(elevation) * Math.sin(azimuth),
        14 * Math.sin(elevation),
        14 * Math.cos(elevation) * Math.cos(azimuth) - 4,
      )
      child.intensity = overrides.light.keyIntensity
      child.color.set(overrides.light.keyColor)
    }
  })

  try {
    return render()
  } finally {
    for (const restore of restores) restore()
  }
}

/**
 * 활성 revision 의 수치 카메라로 한 프레임을 offscreen 렌더해 PNG Blob 을 만든다.
 *
 * label(drei Html)은 DOM 오버레이라 WebGL 장면에 없다 — 자연히 제외된다 (§8.1).
 * render target·카메라는 임시이고, 이전 target 은 finally 에서 복구한다 —
 * 대화형 화면이 깜빡이거나 Orbit 이 초기화되면 안 된다 (§8.2).
 */
export async function captureSceneRender(
  gl: WebGLRenderer,
  scene: Scene,
  camera: SceneNumericCamera,
  overrides?: CaptureOverrides,
  frame?: CaptureFrame,
): Promise<Blob> {
  const { width, height } = frame ?? { width: RENDER_FRAME_SIZE, height: RENDER_FRAME_SIZE }
  const target = new WebGLRenderTarget(width, height)
  const previousTarget = gl.getRenderTarget()

  // aspect 가 프레임을 따라야 화면 밖이 잘리는 방식이 원본 사진과 같아진다
  const evaluationCamera = new PerspectiveCamera(
    camera.fieldOfViewDegrees,
    width / height,
    0.1,
    200,
  )
  evaluationCamera.position.set(camera.position.x, camera.position.y, camera.position.z)
  evaluationCamera.lookAt(new Vector3(camera.target.x, camera.target.y, camera.target.z))
  evaluationCamera.updateProjectionMatrix()

  const pixels = new Uint8Array(width * height * 4)

  try {
    withBaseMaterials(scene, () =>
      withOverrides(scene, overrides, () => {
        gl.setRenderTarget(target)
        gl.render(scene, evaluationCamera)
      }),
    )
    gl.readRenderTargetPixels(target, 0, 0, width, height, pixels)
  } finally {
    gl.setRenderTarget(previousTarget)
    target.dispose()
  }

  if (!hasPixelVariance(pixels)) {
    throw new Error('캡처가 비어 있습니다 — 장면이 아직 그려지지 않았습니다')
  }

  // PNG 인코딩 — 2D 캔버스는 WebGL 컨텍스트를 쓰지 않는다 (§8.2 의 금지 대상이 아니다)
  const flipped = flipPixelsVertically(pixels, width, height)
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext('2d')
  if (!context) throw new Error('PNG 인코딩 캔버스를 만들 수 없습니다')
  context.putImageData(new ImageData(flipped, width, height), 0, 0)

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))
  if (!blob) throw new Error('PNG 인코딩에 실패했습니다')

  const problem = renderBlobProblem(blob.size)
  if (problem) throw new Error(problem)

  return blob
}
