/**
 * 3D 배경 조립 뷰어. Design Ref: scene-assembly §5.2 · Plan D-01/D-02
 *
 * **이 파일은 비동기 청크에만 있어야 한다.** three · fiber · drei 를 여기서 정적으로
 * import 하므로, 정적 경로에 새면 `check-bundle.mjs` 가 빌드를 실패시킨다 (SC-07).
 *
 * **브라우저는 계산하지 않고 그린다.** 위치·스케일·회전의 정본은 서버가 저장한
 * 조립 명세다 — 여기서 다시 유도하면 진실이 둘이 된다.
 */
import { Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { Canvas, useFrame, useThree } from '@react-three/fiber'
import { Html, OrbitControls, useGLTF } from '@react-three/drei'
import { Box3, MathUtils, Mesh as ThreeMesh, PCFShadowMap, Vector3 } from 'three'
import type { Material, Mesh } from 'three'
import { Icon } from '@/features/shell/Icon'
import { meshDownloadUrl } from '@/app/queries/media'
import { backgroundStyles as styles } from './backgroundStyles'
import type { SceneInstanceData, SceneLayoutData } from '@/domain/job/types'
import type { CaptureFrame, CaptureOverrides } from './sceneCapture'
import type { Group } from 'three'

/** 사용자가 움직임을 줄여 달라고 했는가 (NFR-04) — 카메라 감속을 끈다. */
function prefersReducedMotion(): boolean {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
}

/** 물러난 유령용 no-op 판정 — 참조가 하나여야 프레임마다 할당해도 싸다. */
const noRaycast: ThreeMesh['raycast'] = () => {}

/**
 * 언마운트 시 WebGL 컨텍스트를 즉시 반납한다.
 *
 * 반납 없이 떠나면 컨텍스트가 GC 를 기다리는 좀비로 남고, 탭을 오갈 때마다 쌓여
 * 브라우저 상한(~16)에서 가장 오래된 것이 강제로 죽는다 — 그게 지금 보고 있는
 * 캔버스면 화면이 죽는다. "Too many active WebGL contexts" 실측이 이것이다 (F-09).
 */
function ReleaseContextOnUnmount() {
  const gl = useThree((state) => state.gl)

  useEffect(
    () => () => {
      gl.forceContextLoss()
    },
    [gl],
  )

  return null
}

export interface SceneAssemblyViewProps {
  layout: SceneLayoutData
  /**
   * 결정적 캡처 손잡이 (background-similarity-tuning §8.2).
   *
   * 모든 GLTF 로드 + 최소 한 프레임 렌더가 끝나야 함수가 넘어오고, 언마운트나
   * layout 교체 시 null 로 되돌린다 — 준비 전 캡처 버튼은 이 값으로 비활성이다.
   */
  onCaptureHandle?: (capture: SceneCaptureHandle | null) => void
}

/** 캡처 손잡이 — 오버라이드를 주면 후보 revision 을, 없으면 현재 장면을 그린다 (§8.2).
 * frame 은 원본 이미지 비율의 캡처 프레임(긴 변 1024) — 없으면 정사각이다. */
export type SceneCaptureHandle = (
  overrides?: CaptureOverrides,
  camera?: SceneLayoutData['numericCamera'],
  frame?: CaptureFrame,
) => Promise<Blob>

/**
 * 캡처 다리 — Canvas 안에서 renderer·scene 을 붙잡아 손잡이를 올린다.
 *
 * **Suspense 경계 안에 있는 것이 준비 판정이다**: 형제 인스턴스의 GLTF 가 하나라도
 * 미해결이면 경계 전체가 대기라 이 컴포넌트도 mount 되지 않는다. mount 후 첫 프레임을
 * 기다렸다가 손잡이를 올린다 (§8.2 — 첫 렌더 전 캡처 금지).
 */
function CaptureBridge({
  camera,
  onHandle,
}: {
  camera: SceneLayoutData['numericCamera']
  onHandle: (capture: SceneCaptureHandle | null) => void
}) {
  const gl = useThree((state) => state.gl)
  const scene = useThree((state) => state.scene)
  const raised = useRef(false)

  // 최신 콜백을 ref 로 든다 — 부모가 인라인 함수를 넘겨도 등록이 풀렸다 붙었다 하지 않는다
  const handleRef = useRef(onHandle)
  handleRef.current = onHandle

  useFrame(() => {
    if (raised.current) return
    raised.current = true
    handleRef.current((overrides, cameraOverride, frame) =>
      import('./sceneCapture').then((m) =>
        m.captureSceneRender(gl, scene, cameraOverride ?? camera, overrides, frame),
      ),
    )
  })

  // 해제는 언마운트에만 — 탭 이탈·layout 교체 후 죽은 renderer 를 캡처하면 빈 그림이 나온다.
  // **raised 도 함께 리셋한다**: StrictMode(dev)는 mount→cleanup→재mount 를 돌리는데,
  // 리셋이 없으면 cleanup 이 null 로 되돌린 뒤 ref 가 true 로 남아 손잡이가 영영 안 올라온다
  // (실측 — production 빌드의 E2E 는 통과하고 dev 화면만 버튼이 죽어 있었다)
  useEffect(
    () => () => {
      raised.current = false
      handleRef.current(null)
    },
    [],
  )

  return null
}

export function SceneAssemblyView({ layout, onCaptureHandle }: SceneAssemblyViewProps) {
  // 이름표는 기본 꺼짐 (FR-07) — 인스턴스가 많으면 켠 채로는 장면이 안 보인다
  const [labelsOn, setLabelsOn] = useState(false)

  // 지목된 인스턴스 — 이름표 모드에서 이름표나 오브젝트를 **클릭**하면 나머지가 물러난다.
  // 호버 즉발은 마우스가 지나갈 때마다 번쩍여 눈이 아프다 (실측 피드백) — 호버는 이름표
  // 색만 바꾸고, 효과는 클릭으로 확정한다. PartsOverlay 의 클릭 고정과 같은 문법이다
  const [focusKey, setFocusKey] = useState<string | null>(null)

  // 같은 것을 다시 클릭하면 푼다 — 토글이 해제 수단을 따로 만들지 않는 가장 싼 길이다
  const toggleFocus = (key: string) => setFocusKey((prev) => (prev === key ? null : key))

  // 원본 시점 재현 — **수치 카메라가 정본이다** (background-similarity-tuning D-03).
  // 문자열 재해석을 화면에서 반복하면 평가 렌더와 표시가 어긋난다 (실측: 12m 부감이
  // 화면·평가 서로 다른 각으로 그려졌다). 서버 revision 의 값을 그대로 쓴다
  const camera = layout.numericCamera
  const rig = layout.numericLight
  const azimuth = (rig.azimuthDegrees * Math.PI) / 180
  const elevation = (rig.elevationDegrees * Math.PI) / 180
  const lightPosition: [number, number, number] = [
    14 * Math.cos(elevation) * Math.sin(azimuth),
    14 * Math.sin(elevation),
    14 * Math.cos(elevation) * Math.cos(azimuth) - 4, // 장면 중심(z=-4) 기준
  ]

  const partCount = new Set(layout.instances.map((instance) => instance.partId)).size

  // **서버 카메라보다 가깝게 당기면 안 된다** — OrbitControls 의 상한이 초기 위치보다
  // 짧으면 시작하자마자 카메라를 끌어당겨 화면과 평가 렌더가 어긋난다 (D-03 위반).
  // 장면을 담는 거리는 내용 크기에 따라 달라지므로(#18 후속 카메라 수정) 상한도 그에 맞춘다
  const composedDistance = Math.hypot(
    camera.position.x - camera.target.x,
    camera.position.y - camera.target.y,
    camera.position.z - camera.target.z,
  )
  const maxOrbitDistance = Math.max(40, composedDistance * 1.5)

  return (
    <div className={styles.sceneWrap} data-testid="scene-assembly">
      <Canvas
        // 명시적 PCF — 기본값(PCFSoft)은 three r185 에서 폐기돼 어차피 PCF 로
        // 바뀌어 그려진다. 명시하면 경고 없이 같은 그림이다
        shadows={{ type: PCFShadowMap }}
        camera={{
          position: [camera.position.x, camera.position.y, camera.position.z],
          fov: camera.fieldOfViewDegrees,
        }}
        // 실패해도 검은 화면이 아니라 화면 밖 오류 경계가 말하게 둔다
        gl={{ antialias: true }}
        // 빈 곳 클릭 = 지목 해제 — 어디를 눌러야 풀리는지 설명이 필요 없다
        onPointerMissed={() => setFocusKey(null)}
      >
        <ReleaseContextOnUnmount />

        {/* 에셋 뷰어와 같은 문법 — 밝은 바탕에서 색이 제대로 보인다 (사용자 결정) */}
        <color attach="background" args={['#f5f5f4']} />

        <hemisphereLight
          intensity={rig.ambientIntensity}
          color={rig.ambientColor}
          groundColor="#d9d7d2"
        />
        <ambientLight intensity={0.3} />
        {/* 주광이 그림자를 만든다 — 방향·온도가 원본 서술을 따른다.
            ContactShadows 는 위에서 내리찍는 방식이라 크게 세운 원경 파츠가
            오브젝트와 동떨어진 거대한 얼룩을 만들었다 (실측) */}
        <directionalLight
          position={lightPosition}
          intensity={rig.keyIntensity}
          color={rig.keyColor}
          castShadow
          shadow-mapSize-width={2048}
          shadow-mapSize-height={2048}
          shadow-camera-left={-20}
          shadow-camera-right={20}
          shadow-camera-top={20}
          shadow-camera-bottom={-20}
          shadow-camera-near={0.5}
          shadow-camera-far={60}
          shadow-bias={-0.0004}
        />

        {/* 그림자만 받는 투명 바닥 — 밝은 바탕을 더럽히지 않는다 */}
        <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, 0, -2]} receiveShadow>
          <planeGeometry args={[70, 70]} />
          <shadowMaterial transparent opacity={0.25} />
        </mesh>

        <Suspense fallback={null}>
          {layout.instances.map((instance) => {
            const instanceKey = `${instance.partId}-${instance.ordinal}`

            return (
              <SceneInstanceMesh
                key={instanceKey}
                instance={instance}
                showLabel={labelsOn}
                // 지목 흐리기는 이름표 모드에서만 — 평소 회전 중에 번쩍이면 산만하다
                dimmed={labelsOn && focusKey !== null && focusKey !== instanceKey}
                focused={focusKey === instanceKey}
                onToggle={labelsOn ? toggleFocus : undefined}
                instanceKey={instanceKey}
              />
            )
          })}
          {onCaptureHandle ? (
            <CaptureBridge camera={layout.numericCamera} onHandle={onCaptureHandle} />
          ) : null}
        </Suspense>

        <OrbitControls
          target={[camera.target.x, camera.target.y, camera.target.z]}
          enableDamping={!prefersReducedMotion()}
          maxDistance={maxOrbitDistance}
          minDistance={2}
        />
      </Canvas>

      {/* 우상단 — 이름표 토글 (FR-07) */}
      <div className={styles.sceneControls}>
        <button
          type="button"
          className={styles.sceneToggle}
          data-testid="scene-labels-toggle"
          aria-pressed={labelsOn}
          onClick={() =>
            setLabelsOn((on) => {
              if (on) setFocusKey(null) // 끄면서 지목도 푼다 — 남으면 다음 켬에서 흐린 채 시작한다
              return !on
            })
          }
        >
          <Icon name="eye" size={13} />
          이름표
        </button>
      </div>

      {/* 좌하단 — 조립 요약. Canvas 내부는 DOM 이 아니라서 E2E 가 이 줄로 센다 (§8.3) */}
      <div className={styles.sceneMeta}>
        <span className={styles.sceneMetaLine} data-testid="scene-summary">
          파츠 {partCount} · 인스턴스 {layout.instances.length}
        </span>
        {layout.missingPartNames.length > 0 ? (
          <span className={styles.sceneMetaWarn} data-testid="scene-missing">
            3D 없음 {layout.missingPartNames.length} — {layout.missingPartNames.join(', ')}
          </span>
        ) : null}

        {/*
          유도가 내린 판단 (background-scale-calibration #18 FR-07). 접지로 못 본 배치가
          있으면 크기를 대표 깊이로 잡았다는 뜻이라, "왜 이 크기인가" 를 되짚을 근거가 된다
        */}
        {/* 면을 덮도록 눕거나 선 배치 — 낱개와 규칙이 다르다는 사실을 드러낸다 */}
        {layout.composition && layout.composition.surfaceCount > 0 ? (
          <span className={styles.sceneMetaLine} data-testid="scene-surface">
            표면 {layout.composition.surfaceCount} 배치
          </span>
        ) : null}

        {layout.composition && layout.composition.elevatedCount > 0 ? (
          <span className={styles.sceneMetaLine} data-testid="scene-composition">
            고도 미상 {layout.composition.elevatedCount} /{' '}
            {layout.composition.elevatedCount + layout.composition.groundedCount} 배치
          </span>
        ) : null}

        {/* 기준 물체 배치가 서로 다른 크기로 잡혔다 — 보정 계수를 그만큼 덜 믿어야 한다 */}
        {layout.composition && layout.composition.anchorSpread >= ANCHOR_SPREAD_WARN ? (
          <span className={styles.sceneMetaWarn} data-testid="scene-anchor-spread">
            크기 기준 편차 {layout.composition.anchorSpread.toFixed(1)}배 — 보정이 흔들릴 수
            있습니다
          </span>
        ) : null}

        {/*
          기준 물체가 표면으로 표시됐다 (#20 §6). 표면의 bounds.H 는 높이가 아니라 누운
          면의 세로 범위라, 그것으로 보정 계수를 뽑으면 장면 전체 크기가 어긋난다 —
          막지 않고 알린다. 운영자가 표시를 고치면 다음 합성에서 사라진다
        */}
        {layout.composition?.anchorIsSurface ? (
          <span className={styles.sceneMetaWarn} data-testid="scene-anchor-surface">
            크기 기준 물체가 표면으로 표시됐습니다 — 보정 계수를 믿을 수 없습니다
          </span>
        ) : null}

        {/* 상한에 걸려 버린 배치 — 인스턴스가 배치보다 적은 이유를 설명한다 (#20 FR-06) */}
        {layout.composition?.droppedCount ? (
          <span className={styles.sceneMetaWarn} data-testid="scene-dropped">
            배치 {layout.composition.droppedCount}개가 상한에 걸려 빠졌습니다
          </span>
        ) : null}

        {/*
          표시는 바닥인데 GLB 는 서 있는 형태다 (설계 §4.1 부차 신호). 표시를 뒤집지
          않는다 — 분해가 준 한 비트가 정본이다. 의심할 근거만 준다
        */}
        {layout.composition?.surfaceShapeMismatch ? (
          <span className={styles.sceneMetaLine} data-testid="scene-shape-mismatch">
            바닥 표시 {layout.composition.surfaceShapeMismatch}개가 서 있는 형태입니다
          </span>
        ) : null}
      </div>
    </div>
  )
}

/**
 * 기준 물체 배치들의 크기 편차 경고선.
 *
 * 같은 물체를 두 자리에서 재는데 2배 넘게 갈리면 어느 쪽을 믿을지 정할 근거가 없다는
 * 뜻이다 — 실측 유적 광장에서 3.9배가 나왔다 (background-scale-calibration #18 D-05).
 */
const ANCHOR_SPREAD_WARN = 2

/**
 * 인스턴스 하나 — GLB 를 정규화해 서버가 준 자리에 놓는다.
 *
 * **정규화는 뷰어 몫이다** (§3.2 계약). 서버는 GLB 를 열지 않으므로 `scale` 은
 * "높이 1 · 바닥 중심 원점" 으로 맞춘 모델에 곱하는 값으로 정의돼 있다.
 */
function SceneInstanceMesh({
  instance,
  instanceKey,
  showLabel,
  dimmed,
  focused,
  onToggle,
}: {
  instance: SceneInstanceData
  instanceKey: string
  showLabel: boolean
  /** 다른 인스턴스가 지목됐다 — 유령처럼 물러난다 */
  dimmed: boolean
  /** 이 인스턴스가 지목됐다 — 이름표가 점등된다 */
  focused: boolean
  onToggle?: (key: string) => void
}) {
  const gltf = useGLTF(meshDownloadUrl(instance.meshId))

  const { object, normalizedScale } = useMemo(() => {
    // 같은 파츠의 인스턴스끼리 지오메트리를 공유한다 (NFR-03) — clone 은 참조 복사다
    const cloned: Group = gltf.scene.clone(true)

    // 그림자는 메시 단위 스위치다 — 켜지 않으면 광원을 아무리 설정해도 안 생긴다.
    // **머티리얼은 인스턴스별로 복제한다** — 공유한 채 흐리면 같은 파츠가 전부 흐려진다.
    // 지오메트리는 여전히 공유라 NFR-03 은 지켜진다
    cloned.traverse((child) => {
      if ((child as { isMesh?: boolean }).isMesh) {
        child.castShadow = true
        child.receiveShadow = true

        const mesh = child as unknown as Mesh
        const materials = Array.isArray(mesh.material) ? mesh.material : [mesh.material]
        const clones = materials.map((material) => {
          const copy = material.clone()
          // 복원값을 재질에 실어 둔다 — 흐리기 전 상태가 재질마다 다르다
          copy.userData.baseOpacity = copy.opacity
          copy.userData.baseTransparent = copy.transparent
          return copy
        })
        mesh.material = Array.isArray(mesh.material) ? clones : clones[0]!
      }
    })

    const box = new Box3().setFromObject(cloned)
    const size = box.getSize(new Vector3())
    const center = box.getCenter(new Vector3())

    // 높이 1 로 정규화 — 0 높이(평면 GLB)는 1 로 간주해 나눗셈 폭주를 막는다
    const height = size.y > 1e-6 ? size.y : 1
    const factor = 1 / height

    // 바닥 중심을 원점으로 — 발끝이 서버가 준 자리(Y=0)에 닿는다
    cloned.position.set(-center.x * factor, -box.min.y * factor, -center.z * factor)
    cloned.scale.setScalar(factor)

    // 축별 배율 (#20 §4.2) — 표면은 폭·두께·깊이가 다르다. three.js 는 배열을 받는다
    const vector = instance.scaleVector
    return {
      object: cloned,
      normalizedScale: [vector.x, vector.y, vector.z] as [number, number, number],
    }
  }, [gltf.scene, instance.scaleVector])

  // 오브젝트 클릭 판정용 — 누른 지점 (아래 onPointerDown 주석 참조)
  const press = useRef<{ x: number; y: number } | null>(null)

  // 지목 흐리기 — **서서히**. 즉발 전환은 깜빡임으로 읽혀 눈이 아프다 (실측 피드백).
  // 재질 값은 렌더 트리 밖이라 프레임 루프에서 감쇠로 민다
  const fade = useRef(1)
  const settled = useRef(false)

  useFrame((_, delta) => {
    const target = dimmed ? 0.06 : 1

    if (Math.abs(fade.current - target) < 0.002) {
      if (settled.current) return // 다 왔으면 매 프레임 순회하지 않는다
      fade.current = target
      settled.current = true
    } else {
      // 감쇠 상수 9 ≈ 0.3초에 수렴. 움직임 줄임 설정이면 즉시 적용 (NFR-04)
      fade.current = prefersReducedMotion()
        ? target
        : MathUtils.damp(fade.current, target, 9, delta)
      settled.current = false
    }

    object.traverse((child) => {
      if (!(child as { isMesh?: boolean }).isMesh) return

      const mesh = child as unknown as Mesh
      const materials = (
        Array.isArray(mesh.material) ? mesh.material : [mesh.material]
      ) as Material[]

      for (const material of materials) {
        const base = material.userData.baseOpacity as number
        material.opacity = base * fade.current
        material.transparent =
          fade.current < 0.999 || (material.userData.baseTransparent as boolean)
        // 반쯤 사라진 뒤에야 그림자·깊이를 거둔다 — 시작부터 거두면 그 순간이 또 깜빡임이다
        material.depthWrite = fade.current > 0.5
      }
      mesh.castShadow = fade.current > 0.5

      // **안 보이는 것은 클릭도 안 된다.** 레이캐스트는 불투명도를 모르므로 유령이
      // 클릭을 가로챈다 — 뒤의 보이는 오브젝트를 눌러도 반응이 없는 것으로 읽힌다 (실측).
      // 반투명 이하로 물러나면 판정에서 뺀다: 보이는 대로 클릭된다
      mesh.raycast = fade.current > 0.35 ? ThreeMesh.prototype.raycast : noRaycast
    })
  })

  return (
    <group
      position={[instance.position.x, instance.position.y, instance.position.z]}
      rotation={[0, instance.rotationY, 0]}
      scale={normalizedScale}
      // 후보 캡처가 이 그룹을 찾아 변환을 잠시 바꾼다 (§9.2 4단계)
      userData={{ instanceKey }}
      /*
        오브젝트 클릭 지목 — onClick 을 쓰지 않는다.

        R3F 의 click 은 누를 때와 뗄 때 **같은 오브젝트에 맞아야** 발화한다. 실제
        마우스는 클릭 중 몇 픽셀 움직이고, 그 사이 OrbitControls 가 카메라를 돌려
        작은 오브젝트가 커서 밑에서 빠져나간다 — 클릭이 조용히 사라진다 (실측).
        누를 때 포인터를 붙잡아 두고, 뗄 때 이동량이 작으면 클릭으로 판정한다.
      */
      onPointerDown={
        onToggle
          ? (event) => {
              event.stopPropagation()
              press.current = { x: event.nativeEvent.clientX, y: event.nativeEvent.clientY }
              ;(event.target as Element).setPointerCapture(event.pointerId)
            }
          : undefined
      }
      onPointerUp={
        onToggle
          ? (event) => {
              if (press.current === null) return
              event.stopPropagation()
              const moved = Math.hypot(
                event.nativeEvent.clientX - press.current.x,
                event.nativeEvent.clientY - press.current.y,
              )
              press.current = null
              // 8px — 보통의 클릭 떨림은 삼키고, 카메라 회전 드래그는 거른다
              if (moved <= 8) onToggle(instanceKey)
            }
          : undefined
      }
    >
      <primitive object={object} />
      {showLabel ? (
        <Html position={[0, 1.15, 0]} center zIndexRange={[10, 0]}>
          <button
            type="button"
            className={styles.assemblyLabel}
            data-testid="scene-label"
            data-dimmed={dimmed}
            data-focused={focused}
            onClick={onToggle ? () => onToggle(instanceKey) : undefined}
          >
            {instance.partName}
          </button>
        </Html>
      ) : null}
    </group>
  )
}
