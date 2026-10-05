/**
 * Design Ref: §8.2 · §5.3 · §2.3-7 — 원본 이미지 위에 파츠 영역을 겹친다.
 *
 * **값만 보여주면 사람이 검증할 수 없다.** `x=0.33 y=0.13 w=0.25 h=0.74` 를 읽고
 * 맞는지 판단할 수 있는 사람은 없다. 이 화면이 R-1(좌표 신뢰도) 측정의 도구다.
 *
 * 사이클 #9 에서 두 가지가 바뀌었다.
 *
 * **① 상자를 평소에 그리지 않는다.** 파츠 9개의 상자가 늘 겹쳐 있어 화면이 어지럽고 클릭이
 * 어려웠다. 이제 칩과 앵커 점만 서 있고, 지목했을 때만 상자가 뜬다.
 *
 * **② 파츠 하나가 배치를 여럿 갖는다.** 나무 네 그루면 상자가 넷이다. 지목하면 그 파츠의
 * 배치가 **전부** 켜진다 — 검증 단위가 파츠이기 때문이다 (D-08).
 */
import { useMemo, useState } from 'react'
import { sourceImageUrl } from '@/app/queries/media'
import { backgroundStyles as styles } from './backgroundStyles'
import type { HTMLAttributes, MouseEvent as ReactMouseEvent, ReactNode, Ref } from 'react'
import type { Bounds, SceneSpec } from '@/domain/job/types'

/**
 * 이 오버레이가 그리는 데 필요한 최소한.
 *
 * `AssetPart` 를 그대로 받지 않는 이유는 검수 화면의 `ReviewPart` 도 같은 UI 를 쓰기
 * 때문이다 — 둘은 필드가 다르고, 공통분모만 받으면 양쪽이 그대로 들어온다.
 */
export interface OverlayPart {
  id: string
  name: string
  placements: Bounds[]
  /**
   * 칩에 쓸 내용. 없으면 이름만 쓴다 (배경은 "1. 등대", 검수는 "P01 · 벨트").
   *
   * 여러 줄이 필요하면 요소로 넘긴다 — 가림 관계까지 한 줄에 이어 붙이면 칩이 가로로
   * 길어져 서로 덮는다.
   */
  label?: ReactNode
}

export interface PartsOverlayProps {
  sourceImageId: string
  parts: OverlayPart[]
  scene?: SceneSpec | null
  /** 칩을 고정했을 때 옆에 뜨는 버튼. 배경은 "이동", 검수는 "삭제". */
  action?: { label: string; onClick: (partId: string) => void }
  /** 프레임 위에 얹을 것 — 검수 화면이 그리는 중인 사각형을 여기 넣는다. */
  children?: ReactNode
  /** 프레임에 얹는 마우스 처리 — 검수 화면의 드래그. */
  frameProps?: HTMLAttributes<HTMLDivElement> & { 'data-testid'?: string }
  /** 드래그 좌표를 픽셀→0~1 로 바꾸려면 프레임의 실제 크기를 재야 한다. */
  frameRef?: Ref<HTMLDivElement>
  /** 상자를 늘 보이게 한다. 검수 화면은 사람이 전체를 훑어야 해서 켠다. */
  alwaysShowBoxes?: boolean
  /**
   * 상자 편집. 넘기면 지목한 파츠의 상자에 손잡이가 붙는다.
   *
   * **드래그 계산은 호출부가 한다.** 프레임 픽셀→0~1 변환에 필요한 크기를 이미 검수
   * 화면이 재고 있어(`frameRef`), 여기서 또 재면 같은 값을 두 곳에서 관리하게 된다.
   *
   * `preview` 가 있으면 그 배치만 이 좌표로 그린다 — 손을 놓기 전에도 끌리는 것이 보인다.
   */
  editing?: {
    onHandleDown: (
      partId: string,
      ordinal: number,
      box: Bounds,
      handle: OverlayHandle,
      event: ReactMouseEvent,
    ) => void
    preview: { partId: string; ordinal: number; bounds: Bounds } | null
  }
  /** 바깥에서 지목한 파츠 — 서술 확인 목록의 행 호버. 칩 호버·고정보다 우선. */
  highlightedPartId?: string | null
}

/**
 * 상자를 끄는 손잡이 자리. `body` 는 안쪽을 잡아 통째로 옮기는 것이다.
 *
 * 변 손잡이(n·s·e·w)를 두는 이유는 한 축만 잠그기 위해서다. 모서리만 있으면 위쪽 면만
 * 올리려 해도 좌우가 같이 움직여, 1픽셀도 안 흔들리게 끄는 손재주를 요구한다.
 */
export type OverlayHandle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'body'

/** 손잡이의 상자 안 상대 위치(0~1)와 커서. `body` 는 따로 그린다. */
const HANDLES: { id: OverlayHandle; x: number; y: number; cursor: string }[] = [
  { id: 'nw', x: 0, y: 0, cursor: 'nwse-resize' },
  { id: 'n', x: 0.5, y: 0, cursor: 'ns-resize' },
  { id: 'ne', x: 1, y: 0, cursor: 'nesw-resize' },
  { id: 'e', x: 1, y: 0.5, cursor: 'ew-resize' },
  { id: 'se', x: 1, y: 1, cursor: 'nwse-resize' },
  { id: 's', x: 0.5, y: 1, cursor: 'ns-resize' },
  { id: 'sw', x: 0, y: 1, cursor: 'nesw-resize' },
  { id: 'w', x: 0, y: 0.5, cursor: 'ew-resize' },
]

/** 깊이 순서대로 색을 돌린다 — 앞뒤 관계가 색으로도 읽힌다. */
const BOX_COLORS = [
  '#ef4444',
  '#f59e0b',
  '#10b981',
  '#3b82f6',
  '#8b5cf6',
  '#ec4899',
  '#14b8a6',
  '#f97316',
]

/** 칩이 앵커를 덮지 않도록 띄우는 거리 (%). */
const CHIP_GAP_Y = 5.6

/** 칩이 글자로 읽히는 최소 간격 (%). 이보다 좁히면 서로 겹쳐 못 읽는다. */
const CHIP_GAP_MIN = 2.4

/** 칩을 밀어낼 수 있는 세로 범위 (%). 프레임 밖으로 나가면 영영 안 보인다. */
const CHIP_BAND = 94

/** 이 지점을 넘으면 칩이 왼쪽으로 뒤집힌다 — 안 그러면 화면 밖으로 나간다. */
const FLIP_AT = 72

interface Placed {
  part: OverlayPart
  color: string
  /** 칩이 붙는 자리 — 면적이 가장 큰 배치의 중심 (D-07) */
  anchor: { x: number; y: number }
  /** 겹침을 피해 밀어낸 뒤의 세로 위치 */
  top: number
}

/**
 * 칩이 겹치면 아래로 민다.
 *
 * 배치 중심이 몰려도 글자는 읽혀야 한다. 밀린 만큼은 연결선이 되묶는다.
 */
function deoverlap(items: Placed[]): Placed[] {
  const byHeight = [...items].sort((a, b) => a.top - b.top)

  // **간격을 파츠 수에 맞춘다.** 고정 5.6% 로 밀면 파츠가 스물다섯일 때 필요한 높이가
  // 140% 라 아래쪽 칩이 프레임을 넘어간다. 넘어간 것을 끝에 가둬 봐야 거기서 서로
  // 겹쳐 못 읽는다 — 애초에 들어갈 만큼만 벌린다
  const gap = Math.max(CHIP_GAP_MIN, Math.min(CHIP_GAP_Y, CHIP_BAND / Math.max(1, byHeight.length)))

  for (let i = 1; i < byHeight.length; i++) {
    const previous = byHeight[i - 1]!
    const current = byHeight[i]!

    if (current.top - previous.top < gap) {
      current.top = previous.top + gap
    }
  }

  // 프레임 밖으로 나가지 않게 여기서 가둔다. **밖에서 가두면 실선과 어긋난다** —
  // 실선은 원래 좌표로 그려지는데 칩만 옮겨 앉아 둘이 따로 논다
  for (const item of byHeight) {
    item.top = Math.min(96, Math.max(2, item.top))
  }

  return items
}

/** 면적이 가장 큰 배치 — 가장 눈에 띄는 개체 옆에 이름이 선다 (D-07). */
function largest(placements: Bounds[]): Bounds {
  return placements.reduce((a, b) => (a.w * a.h >= b.w * b.h ? a : b))
}

export function PartsOverlay({
  sourceImageId,
  parts,
  scene = null,
  action,
  children,
  frameProps,
  frameRef,
  alwaysShowBoxes = false,
  editing,
  highlightedPartId = null,
}: PartsOverlayProps) {
  const [hovered, setHovered] = useState<string | null>(null)
  const [stuck, setStuck] = useState<string | null>(null)

  const placed = useMemo(() => {
    const withPlacements = parts.filter((part) => part.placements.length > 0)

    return deoverlap(
      withPlacements.map((part, index) => {
        const box = largest(part.placements)

        return {
          part,
          color: BOX_COLORS[index % BOX_COLORS.length]!,
          anchor: { x: (box.x + box.w / 2) * 100, y: (box.y + box.h / 2) * 100 },
          top: (box.y + box.h / 2) * 100,
        }
      }),
    )
  }, [parts])

  // 손을 얹은 것이 우선이고, 없으면 고정된 것이 남는다
  const shown = highlightedPartId ?? hovered ?? stuck

  return (
    <div className={styles.overlay} data-testid="parts-overlay">
      <div ref={frameRef} className={styles.overlayFrame} {...frameProps}>
        {/*
          브라우저 기본 이미지 드래그를 끈다. 켜져 있으면 사각형을 그리려고 끄는 순간
          네이티브 드래그가 시작돼 mouseup 이 문서까지 오지 않는다 — 검수 화면에서
          그린 상자는 남는데 겹침 조회가 영영 안 나갔다
        */}
        <img
          className={styles.overlayImage}
          src={sourceImageUrl(sourceImageId)}
          alt=""
          draggable={false}
          data-testid="overlay-image"
        />

        {/* 수평선 — 지면에 물체를 놓는 기준이다. 좌표가 맞는지 볼 때 가장 먼저 보는 선 */}
        {scene ? (
          <div
            className={styles.horizon}
            style={{ top: `${scene.camera.horizonY * 100}%` }}
            data-testid="overlay-horizon"
          >
            <span>수평선 {scene.camera.horizonY.toFixed(2)}</span>
          </div>
        ) : null}

        {placed.map(({ part, color, anchor, top }) => {
          const isShown = shown === part.id
          const isDimmed = shown !== null && !isShown
          const flip = anchor.x > FLIP_AT

          return (
            <div key={part.id}>
              {/* 배치마다 상자 하나. 지목했을 때만 보인다 — 평소에 다 그리면 원래 문제로 돌아간다 */}
              {part.placements.map((raw, index) => {
                // 끄는 중인 배치는 서버 좌표 대신 미리보기로 그린다
                const box =
                  editing?.preview &&
                  editing.preview.partId === part.id &&
                  editing.preview.ordinal === index
                    ? editing.preview.bounds
                    : raw

                return (
                  <div
                    key={index}
                    className={styles.box}
                    data-shown={isShown || alwaysShowBoxes}
                    data-testid="overlay-box"
                    data-part={part.name}
                    style={{
                      left: `${box.x * 100}%`,
                      top: `${box.y * 100}%`,
                      width: `${box.w * 100}%`,
                      height: `${box.h * 100}%`,
                      borderColor: color,
                      // 지목한 것만 제 색으로. 스물다섯 개가 전부 진하면 어느 것을 보고
                      // 있는지 알 수 없다 — 나머지는 옅게 깔아 위치만 남긴다
                      opacity: alwaysShowBoxes && !isShown ? 0.32 : 1,
                    }}
                  />
                )
              })}

              {/*
                손잡이는 **지목한 파츠에만** 붙인다. 스물다섯 개 상자에 전부 달면 화면이
                점으로 덮이고, 어느 상자를 고치는 중인지도 알 수 없다.
              */}
              {editing && isShown
                ? part.placements.map((raw, index) => {
                    const box =
                      editing.preview &&
                      editing.preview.partId === part.id &&
                      editing.preview.ordinal === index
                        ? editing.preview.bounds
                        : raw
                    const down = (handle: OverlayHandle) => (event: ReactMouseEvent) => {
                      // 프레임의 "새 사각형 그리기" 가 같은 mousedown 을 먹으면 안 된다
                      event.stopPropagation()
                      event.preventDefault()
                      editing.onHandleDown(part.id, index, box, handle, event)
                    }

                    return (
                      <div key={index}>
                        {/* 안쪽을 잡으면 통째로 이동 */}
                        <div
                          className={styles.handleBody}
                          data-testid="overlay-handle-body"
                          style={{
                            left: `${box.x * 100}%`,
                            top: `${box.y * 100}%`,
                            width: `${box.w * 100}%`,
                            height: `${box.h * 100}%`,
                          }}
                          onMouseDown={down('body')}
                        />
                        {HANDLES.map((handle) => (
                          <div
                            key={handle.id}
                            className={styles.handle}
                            data-testid="overlay-handle"
                            data-handle={handle.id}
                            style={{
                              left: `${(box.x + box.w * handle.x) * 100}%`,
                              top: `${(box.y + box.h * handle.y) * 100}%`,
                              cursor: handle.cursor,
                            }}
                            onMouseDown={down(handle.id)}
                          />
                        ))}
                      </div>
                    )
                  })
                : null}

              {/* 앵커 점 — 점의 개수가 곧 "몇 개 있는가" 라는 정보다 */}
              {part.placements.map((box, index) => (
                <div
                  key={index}
                  className={styles.anchor}
                  data-dimmed={isDimmed}
                  data-testid="overlay-anchor"
                  style={{
                    left: `${(box.x + box.w / 2) * 100}%`,
                    top: `${(box.y + box.h / 2) * 100}%`,
                    background: color,
                    // 표시용 점이다. 이벤트를 받으면 상자 한가운데를 덮어 클릭을 가로채고,
                    // 검수 화면에서는 그 자리에서 새 사각형을 그리기 시작할 수도 없다
                    pointerEvents: 'none',
                  }}
                />
              ))}

              {/* 밀려난 만큼만 선을 긋는다 — 안 밀렸으면 없는 편이 조용하다 */}
              {Math.abs(top - anchor.y) > 0.3 ? (
                <div
                  className={styles.tether}
                  data-dimmed={isDimmed}
                  data-testid="overlay-tether"
                  style={{
                    left: `${Math.min(96, Math.max(4, anchor.x))}%`,
                    top: `${Math.min(anchor.y, top)}%`,
                    // 칩 위까지 확실히 닿게 몇 px 더 그린다. 남는 부분은 칩 뒤로 들어간다 —
                    // %로만 계산하면 반올림·여백 때문에 선과 칩 사이가 벌어져 보인다
                    height: `calc(${Math.abs(top - anchor.y)}% + 10px)`,
                    background: color,
                  }}
                />
              ) : null}

              {/*
                칩과 "이동" 을 한 자리에 묶는다 — `<button>` 안에 `<button>` 을 넣을 수 없다.
                오른쪽 끝 파츠는 그룹째 뒤집혀 화면 밖으로 나가지 않는다
              */}
              <div
                className={styles.chipGroup}
                style={{
                  // 프레임 밖으로 나가지 않게 가둔다. 겹침 회피가 세로로만 밀기 때문에
                  // 파츠가 많으면 아래쪽 칩이 프레임을 넘어가 영영 안 보인다
                  left: `${Math.min(96, Math.max(4, anchor.x))}%`,
                  top: `${top}%`,
                  transform: flip ? 'translate(-100%, 0)' : undefined,
                  // 선이 끝난 자리에서 칩이 바로 시작하게 한다 — 여백이 남으면 선과 칩이
                  // 따로 떠 있는 것으로 보인다
                  marginTop: 0,
                  marginLeft: flip ? -2 : 2,
                  flexDirection: flip ? 'row-reverse' : 'row',
                }}
              >
                <button
                  type="button"
                  className={styles.chip}
                  data-dimmed={isDimmed}
                  data-stuck={stuck === part.id}
                  data-testid="overlay-chip"
                  style={{
                    background: color,
                    // 검수 화면은 칩에 가림 관계까지 실어 여러 줄이 된다. 배경 결과
                    // 화면은 한 줄짜리라 기존대로 둔다
                    ...(alwaysShowBoxes
                      ? { whiteSpace: 'normal' as const, maxWidth: 260, textAlign: 'left' as const }
                      : {}),
                  }}
                  onMouseEnter={() => setHovered(part.id)}
                  onMouseLeave={() => setHovered(null)}
                  onFocus={() => setHovered(part.id)}
                  onBlur={() => setHovered(null)}
                  onClick={() => setStuck(stuck === part.id ? null : part.id)}
                >
                  {part.label ?? part.name}
                </button>

                {/* 고정했을 때만 나타난다 — 평소에 붙어 있으면 칩이 두 배로 넓어진다 */}
                {stuck === part.id && action ? (
                  <button
                    type="button"
                    className={styles.chipJump}
                    data-testid="overlay-jump"
                    aria-label={`${part.label ?? part.name} ${action.label}`}
                    onClick={() => action.onClick(part.id)}
                  >
                    {action.label}
                  </button>
                ) : null}
              </div>
            </div>
          )
        })}

        {children}
      </div>

      {placed.length === 0 ? (
        <p className={styles.overlayEmpty} data-testid="overlay-empty">
          좌표 정보가 없습니다. 분해 공정이 끝나면 여기에 표시됩니다.
        </p>
      ) : null}
    </div>
  )
}
