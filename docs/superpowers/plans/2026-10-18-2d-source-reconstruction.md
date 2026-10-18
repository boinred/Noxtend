# 2D 원본 재구성 비교 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 계획 검수 화면의 원본 위에 바닥 타일 반복 채우기와 오브젝트 여러 자리 배치로 만든 재구성 결과를 투명도로 겹쳐, 생성 에셋으로 원본 구도를 얼마나 똑같이 만들 수 있는지 판단하게 한다.

**Architecture:** 재구성은 Frontend에서 매번 계산한다. 픽셀 비교와 격자 계산은 `src/domain/sprites/reconstruction.ts`의 순수 함수에 두고, 이미지 로딩은 화면 계층의 훅이 맡는다. 오브젝트 자리는 `SpriteAssetPlan.Placements`로 `PlanJson`에 저장한다. 자리만 바뀐 저장은 승인과 ZIP을 건드리지 않는 별도 분기로 처리한다. 분석 프롬프트 V5가 자리를 제안한다.

**Tech Stack:** .NET 10 ASP.NET Core·EF Core·xUnit, React 19·TypeScript·Vite·Vitest·Playwright. 새 의존성은 없다.

**Spec:** [2D 원본 재구성 비교 설계](../specs/2026-10-16-2d-source-reconstruction-design.md)

## 실행 방식

저장소 기본인 작업별 구현·독립 리뷰(Subagent-driven, `superpowers:subagent-driven-development`)로 진행한다. 작업 1~3은 Frontend만 바꾸며, 기존 ROI 한 자리 기준으로 먼저 동작한다. 작업 4~7은 오브젝트 자리, 작업 8은 프롬프트, 작업 9는 문서와 전체 검증이다.

## Global Constraints

- 재구성 투명도: 라벨이 있는 native range 0~100%, 기본 50%, `aria-valuetext`로 현재 값을 알린다.
- 타일 선택: 다이아몬드 마스크와 타일 알파 128 이상인 점만 쓰고, 4×2 구역 평균 RGB 거리가 가장 작은 후보를 고른다. 표본 간격은 4px이다. 격자 기준점은 칸 크기 안에서 8px 간격으로 탐색한다.
- 칸 크기: `cellWidthOf` × 원본 너비. 높이는 isometric이면 그 절반, 아니면 같은 값이다.
- 재구성 미리보기 보호 기준: 오프셋당 최대 4,096칸, Worker 계산 제한 5초. 초과 시 부분 결과 없이 이유를 표시하고 원본·ROI 편집·계획 저장을 유지한다. 성능 보장은 실측 후 판단한다.
- 오브젝트 자리는 1~20개다. 각 자리는 원본 내부의 유한하고 양수인 0~1 영역이며, 오브젝트 역할만 가질 수 있다.
- 자리만 바꾼 저장은 `ReviewRevision`만 올린다. 이미지·승인·현재 ZIP·작업 상태·단계·`CompletedAt`은 유지한다.
- 재구성 결과와 타일 선택은 저장하지 않는다. SQL Server가 작업 상태의 정본이다.
- 계층: 순수 계산은 `src/domain/sprites/`, 이미지 로딩·canvas는 `src/features/screens/sprites/`, URL은 `app/queries/media.ts`를 거친다.
- 실제 AI 호출, 유료 smoke, Docker 재배포, PR 병합은 사용자 승인 범위에서만 한다.
- 주석은 비자명한 이유만, 한국어 짧은 명사구로 쓴다(`karpathy-guidelines`).

## Review Focus

자동 테스트가 직접 다루지 않는 실패 모드 중 사용자에게 가장 먼저 드러날 것 순서다. 각 항목의 테스트는 해당 작업에 넣었다.

1. 자리 정보가 생성 프롬프트의 `asset` JSON에 섞여 이미지 생성 입력이 바뀌는 경우 → 작업 4 `GeneratePrompt_AssetJsonExcludesPlacements`
2. 생성 공정이 실행 중일 때 자리만 저장해서 그 공정이 취소되거나 현재가 아닌 것으로 처리되는 경우 → 작업 4 `ReplaceSpritePlan_PlacementsOnly_KeepsRunningTaskCurrent`
3. 역할을 오브젝트에서 타일로 바꾼 draft가 `placements`를 실은 채 저장되어 400이 나는 경우 → 작업 7 E2E `role change clears placements in the saved body`
4. 격자 칸 너비 draft가 비었거나 잘못된 값일 때 재구성 계산이 예외로 화면을 깨는 경우 → 작업 1 `reconstructionCell rejects invalid cell width`
5. 자리만 편집할 때마다 타일 채우기를 다시 계산해 화면이 멈추는 경우 → 작업 1 `tileFillKey ignores placements`

## Task 1: 재구성 순수 계산

**Files:**
- Create: `apps/frontend/src/domain/sprites/reconstruction.ts`
- Test: `apps/frontend/src/domain/sprites/reconstruction.test.ts`

**Interfaces:**
- Consumes: `cellWidthOf`, `assetRole`(`rules.ts`), `SpriteState`·`SpriteAssetPlan`·`SpriteSettings`·`SpriteCanvas`(`types.ts`), `Bounds`(`domain/job/types.ts`)
- Produces:
  - `interface Pixels { width: number; height: number; data: Uint8ClampedArray }`
  - `interface CellSize { width: number; height: number }`
  - `interface TileCell { x: number; y: number; tileId: string | null }` (x·y는 칸 외접 사각형의 왼쪽 위, 원본 px. `tileId`가 null이면 원경 배경을 보인다)
  - `interface TileFill { offset: { x: number; y: number }; cells: TileCell[] }`
  - `reconstructionCell(settings: SpriteSettings, source: SpriteCanvas, cellWidth: number | null): CellSize | null`
  - `latticeCells(source: SpriteCanvas, cell: CellSize, diamond: boolean, offset: { x: number; y: number }): { x: number; y: number }[]`. 4,097번째 칸에서 `RangeError`로 즉시 중단하고 `fillTiles`도 실패한다.
  - `fillTiles(input: { source: Pixels; tiles: { id: string; pixels: Pixels }[]; backdrop: Pixels | null; cell: CellSize; diamond: boolean }): TileFill` (`tiles[].pixels`는 칸 크기, `source`·`backdrop`는 원본 크기)
  - `placementsOf(plan: SpriteAssetPlan): Bounds[]` (작업 6 전까지는 `[plan.sourceBounds]`. 작업 6에서 `plan.placements ?? [plan.sourceBounds]`로 바꾼다)
  - `placementRects(plan: SpriteAssetPlan, source: SpriteCanvas): { x: number; y: number; width: number; height: number }[]`
  - `reconstructionVisible(sprite: SpriteState): boolean`
  - `tileFillKey(plans: SpriteAssetPlan[], sprite: SpriteState, cell: CellSize | null): string`

- [ ] **Step 1: 실패하는 테스트 작성**

| 테스트 | 입력·필수 단언 |
| --- | --- |
| `reconstructionCell uses half height for isometric` | isometric·1536×1024·128/1536 → 128×64, topDown·800×600·0.1 → 80×80 |
| `reconstructionCell rejects invalid cell width` | null·0·-1·NaN·1.5는 모두 null |
| `latticeCells keeps diamond cells overlapping the source` | 원본/칸 128×64, offset 0 → 왼쪽 위 좌표 (-64,-32), (0,0), (64,-32), (-64,32), (64,32)의 정확히 5칸 |
| `fillTiles picks the closest tile per square cell` | 64×32 원본의 좌/우 빨강/파랑, 32×32 단색 후보 → offset (0,0), (0,0)칸 red, (32,0)칸 blue |
| `fillTiles leaves cells empty where the backdrop matches better` | 64×64 원본 상/하 초록/빨강, 32×32 빨강 타일과 초록 backdrop → (0,0)칸 null, (0,32)칸 red |

추가 테스트(단언은 이름 그대로):
- `fillTiles ignores transparent tile pixels`: 가운데만 빨강이고 나머지가 투명한 타일도 빨강 칸에 선택된다
- `fillTiles finds a nonzero offset`: 8px 이동한 비대칭 색 패턴으로 만든 사각 격자 원본에서 미리 정한 `(8, 8)` 선택. 기대값은 `fillTiles`로 만들지 않고 0 고정 구현이 실패하는지 확인한다. 다이아몬드는 동등 격자 오프셋 중 탐색 순서상 첫 정답을 fixture에 명시한다.
- `fillTiles masks diamond corners and clips edge cells`: 마스크 밖의 방해색은 결과를 바꾸지 않고, 가장자리 부분 칸은 원본 내부 표본만 비교한다(원본 밖을 검정으로 채우지 않음).
- `fillTiles rejects candidates without valid samples`: 알파 128 미만뿐인 후보·표본 없는 구역은 0점으로 선택하지 않는다. 유효 구역이 없는 후보는 무한대이고, 모든 오프셋에서 비교 가능한 칸이 없을 때만 오류다. 후보 목록 자체가 빈 경우만 `cells: []`다.
- `fillTiles skips incomparable edge cells without dropping the offset`: 다이아몬드 칸·알파 여백 타일로 원본 가장자리 칸이 비교 불가해도 미리 정한 오프셋이 선택되고, 그 칸은 `cells`에 없다. 칸 수가 적은 오프셋이 합계 때문에 이기지 않도록 평균 점수를 확인한다.
- `latticeCells stops above the preview limit`: 1px 사각 칸의 64×64 원본은 4,096개, 65×64는 `RangeError`. 최대 원본 4096×4096·매우 작은 양수 칸에서도 4,097번째에 중단하고 큰 배열을 만들지 않는다.
- `placementRects maps placements to source pixels`: 자리 `{x:.5,y:.25,w:.25,h:.5}`, 원본 800×600 → `[{ x: 400, y: 150, width: 200, height: 300 }]`
- `reconstructionVisible requires an approved plan and a base image`: `planReview`·`analyzing`·`sourceDraft`는 false, `baseReview`에서 index 0 이미지가 하나라도 있으면 true
- `tileFillKey ignores placements`: 이름·자리만 바꾼 plans는 같은 키이고, tile 역할 이미지 ID·역할·칸 크기·원경 배경 이미지가 바뀌면 다른 키다

`halves`·`halvesY`·`solid`·`objectPlan`은 테스트 파일 안의 작은 helper다.

- [ ] **Step 2: 실패 확인**

Run: `pnpm --filter @nextend/frontend exec vitest run src/domain/sprites/reconstruction.test.ts`
Expected: FAIL, `reconstruction` 모듈 없음

- [ ] **Step 3: `reconstruction.ts` 구현**

- `latticeCells`: 다이아몬드는 중심 `(ox + i·W/2, oy + j·H/2)`이고 `(i + j)`가 짝수인 점이다. 외접 사각형이 원본과 겹치는 것만 남긴다(`x + W > 0 && y + H > 0 && x < SW && y < SH`). 사각 격자는 `(ox + i·W, oy + j·H)`이다.
- `fillTiles`: 오프셋 `(ox, oy)`를 `0 ≤ ox < W`, `0 ≤ oy < H`, 8px 간격으로 돌며 비교 가능한 칸의 최소 거리 평균이 가장 작은 오프셋을 고른다(합계 아님). 동률이면 먼저 찾은 것을 쓴다. 특징은 시안과 같은 4×2 구역 평균 RGB이고, 거리는 구역 평균 제곱오차의 제곱근이다. 원경 배경 후보는 같은 칸 위치의 `backdrop` 픽셀로 만든 특징이며, 가장 가까우면 `tileId: null`이다. 타일 후보가 없으면 `cells: []`를 돌려준다.
- 거리의 표본·분모는 설계 2절의 교집합 규칙을 적용한다. 표본이 없는 구역은 제외한다. 어떤 후보와도 비교할 수 없는 칸은 그 오프셋의 점수와 `cells`에서 빼고 오프셋은 유지한다. 모든 오프셋에서 비교 가능한 칸이 없을 때만 `Error('재구성에 비교할 수 있는 픽셀이 없습니다')`다. 칸 상한 `RangeError`는 잡아서 빈 결과로 바꾸지 않는다.
- `reconstructionVisible`: `phase`가 `sourceDraft`·`analyzing`·`planReview`가 아니고, index 0의 `currentImageId`를 가진 asset이 하나 이상이면 true다.
- `tileFillKey`: tile 역할 plan의 `id`·역할, 그 asset의 index 0 이미지 ID, backdrop 이미지 ID, 칸 크기만 직렬화한다.

- [ ] **Step 4: 통과 확인**

Run: `pnpm --filter @nextend/frontend exec vitest run src/domain/sprites/reconstruction.test.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/frontend/src/domain/sprites/reconstruction.ts apps/frontend/src/domain/sprites/reconstruction.test.ts
git commit -m "feat(frontend): compute sprite source reconstruction"
```

## Task 2: 계획 검수 화면의 재구성 층

**Files:**
- Create: `apps/frontend/src/features/screens/sprites/useTileFill.ts`, `apps/frontend/src/features/screens/sprites/tileFill.worker.ts`, `apps/frontend/src/features/screens/sprites/SpriteReconstructionLayer.tsx`, `apps/frontend/tests/e2e/sprites-reconstruction.spec.ts`
- Modify: `apps/frontend/src/features/screens/background/PartsOverlay.tsx`, `apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx`, `apps/frontend/tests/e2e/spriteFakeApi.ts`

**Interfaces:**
- Consumes: 작업 1의 전부, `spriteImageUrl`·`sourceImageUrl`(`app/queries/media.ts`)
- Produces:
  - `PartsOverlayProps.underlay?: ReactNode`. `<img>` 바로 다음에 `<div className="pointer-events-none absolute inset-0" data-testid="overlay-underlay">`로 감싸 그린다. 상자보다 아래층이다.
  - `useTileFill(args: { jobId: string; sourceImageId: string; sprite: SpriteState; plans: SpriteAssetPlan[]; cell: CellSize | null; enabled: boolean }): { status: 'idle' | 'loading' | 'ready' | 'error'; fill: TileFill | null; error: string | null }`. `tileFillKey`가 같으면 다시 계산하지 않는다.
  - `tileFill.worker.ts`: `fillTiles` 입력을 받아 `{ fill: TileFill } | { error: string }`을 반환한다. 훅이 요청별 Worker·5초 타이머를 소유하고 타일 계산 키 변경·unmount·시간 초과에 종료한다(자리만 변경하면 재시작하지 않음). 이전 결과는 적용하지 않는다. 이미지 로딩과 Worker 계산의 실패를 모두 훅의 `error`로 전달한다.
  - `SpriteReconstructionLayer(props: { jobId: string; sprite: SpriteState; plans: SpriteAssetPlan[]; fill: TileFill | null; cell: CellSize | null; opacity: number })`. `role="img"`, `aria-label="원본 재구성"`인 SVG이며 viewBox는 원본 크기, `preserveAspectRatio="none"`이다.
  - Fake: `SpriteFakeOptions.seed`에 `'planReview'`를 추가한다(`make()` 결과를 그대로 쓴다).

- [ ] **Step 1: E2E 실패 테스트 작성** (`sprites-reconstruction.spec.ts`, roles 설정은 `sprites-roles.spec.ts`와 같은 isometric tiles)

```ts
test('plan review has no reconstruction before plan approval', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'planReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  await expect(review.getByRole('img', { name: '원본 재구성' })).toHaveCount(0)
  await expect(review.getByRole('slider', { name: '재구성 투명도' })).toHaveCount(0)
})
test('reconstruction follows opacity after plan approval', async ({ page }) => {
  await installFakeApi(page, { sprites: { seed: 'baseReview', settings } })
  await page.goto(`/2d/background/${SPRITE_IDS.job}`)
  const review = page.getByRole('region', { name: '제작 계획 검수' })
  const layer = review.getByRole('img', { name: '원본 재구성' })
  const images = layer.getByTestId('reconstruction-images')
  const opacity = review.getByRole('slider', { name: '재구성 투명도' })
  await expect(opacity).toHaveValue('50')
  await expect(images).toHaveAttribute('opacity', '0.5')
  await opacity.fill('80')
  await expect(opacity).toHaveAttribute('aria-valuetext', '80%')
  await expect(images).toHaveAttribute('opacity', '0.8')
  await expect(images.locator('image[data-tile-id]').first()).toHaveAttribute('width', '320')
  await expect(images.locator('image[data-asset-id]')).toHaveCount(2) // 원경 배경 1·오브젝트 자리 1
})
test('missing base images show dashed outlines regardless of opacity', async ({ page }) => {
  // partialBases: 타일 기준 이미지가 없으므로 rect[data-missing] 1개, 슬라이더 0에서도 보임
})
test('unreadable images show a reason instead of a partial reconstruction', async ({ page }) => {
  // installFakeApi 뒤 page.route('**/sprites/images/**', r => r.fulfill({ status: 500 }))
  // 계획 검수 영역 role=alert에 '재구성에 필요한 이미지를 불러오지 못했습니다', 재구성 img 없음
})
```

E2E에 `small cells keep editing usable without excessive SVG nodes`와 `timed out and superseded workers cannot publish results`를 추가한다. Fake의 작은 칸·4096×4096 원본에서 오류/입력 유지·타일 노드 0개를 확인한다. Worker 응답 지연은 빌드된 Worker 파일(`**/tileFill.worker-*.js`)을 `page.route`로 메시지를 보내지 않는 스크립트로 바꿔 만들고, 가짜 시계로 5초 경계를 검증하며 입력 변경·unmount 뒤 늦은 결과가 나타나지 않는지도 확인한다.

- [ ] **Step 2: 실패 확인**

Run: `pnpm --filter @nextend/frontend build && pnpm --filter @nextend/frontend exec playwright test tests/e2e/sprites-reconstruction.spec.ts`
Expected: FAIL, `원본 재구성` 없음

- [ ] **Step 3: `useTileFill`·`SpriteReconstructionLayer`·`underlay` 구현과 `SpritePlanReview` 연결**

- 이미지는 `crossOrigin = 'anonymous'`인 `Image`로 불러와 canvas `getImageData`로 `Pixels`를 만든다. 타일은 칸 크기로, 원본·원경 배경은 원본 크기로 그린다. 로딩이나 `getImageData` 실패는 `error: '재구성에 필요한 이미지를 불러오지 못했습니다'`로 처리하고, 실패한 상태로 일부만 그리지 않는다.
- 층 그리는 순서는 설계 1절을 따른다. 원경 배경과 레이어는 원본 전체(`data-asset-id`), 타일 칸은 `data-tile-id`, 오브젝트는 `placementRects`(`data-asset-id`)를 쓴다. 기준 이미지가 없는 asset은 `placementRects`(원경 배경·레이어는 원본 전체) 위치에 `rect[data-missing]` 점선을 그린다. 이 점선은 투명도 `g` 밖에 둔다.
- `SpritePlanReview`는 `reconstructionVisible(sprite)`일 때만 슬라이더(라벨 `재구성 투명도 {n}%`, `aria-label="재구성 투명도"`)와 `underlay`를 둔다. 입력은 draft plans와 draft grid에서 계산한 `reconstructionCell`이다. `status === 'loading'`이면 `role="status"` "재구성 계산 중…"을, `error`면 `role="alert"`를 슬라이더 아래에 표시한다.
- `useTileFill`은 처음부터 module Worker에서 계산한다. `RangeError`는 "재구성 칸이 너무 많습니다. 격자 칸 너비를 늘려 주세요", 5초 초과는 "재구성 계산 시간이 초과되었습니다. 격자 칸 너비를 조정해 주세요"로 표시하고 Worker와 타이머를 정리한다. 현재 결과·재구성 층을 비우되 원본·ROI와 계획 저장은 유지한다.
- 투명도 `opacity` 속성은 SVG 루트가 아니라 재구성 이미지 `g`(`data-testid="reconstruction-images"`)에 둔다.

- [ ] **Step 4: 실제 크기 계산 시간 측정**

실제 작업(1536×1024·타일 8종)은 Docker API에서 조회만 하고, 작은 칸·4,096칸 경계·최대 원본은 합성 입력으로 측정한다. 계산 시간, SVG 노드 수, 렌더링 시간, 슬라이더·ROI 입력 응답성을 각각 기록한다. 4,096개·5초는 초기 보호 기준이며 실측 보장이 아니다. 허용 범위에서도 화면이 멈추면 설계 2절의 canvas 전환 또는 제한 축소를 적용한 뒤 검증한다.

- [ ] **Step 5: 통과 확인**

Run: `pnpm --filter @nextend/frontend build && pnpm --filter @nextend/frontend exec playwright test tests/e2e/sprites-reconstruction.spec.ts tests/e2e/sprites-roi.spec.ts tests/e2e/review-gate.spec.ts`
Expected: PASS (기존 ROI·3D 검수 회귀 포함)

- [ ] **Step 6: Commit**

```bash
git add apps/frontend/src/features/screens/sprites/useTileFill.ts apps/frontend/src/features/screens/sprites/tileFill.worker.ts apps/frontend/src/features/screens/sprites/SpriteReconstructionLayer.tsx apps/frontend/src/features/screens/background/PartsOverlay.tsx apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx apps/frontend/tests/e2e/sprites-reconstruction.spec.ts apps/frontend/tests/e2e/spriteFakeApi.ts
git commit -m "feat(frontend): overlay source reconstruction on plan review"
```

## Task 3: 별도 원본 비교 패널 제거

**Files:**
- Delete: `apps/frontend/src/features/screens/sprites/SpriteSourceCompare.tsx`
- Modify: `apps/frontend/src/features/screens/sprites/SpriteStudioScreen.tsx`, `apps/frontend/src/domain/sprites/rules.ts`(`assetSourceRect` 삭제), `apps/frontend/src/domain/sprites/rules.test.ts`(`places each role back on the source canvas` 삭제), `apps/frontend/tests/e2e/sprites-roles.spec.ts`(`source comparison overlays …` 삭제), `docs/xHuman/frontend.md`

**Interfaces:**
- Consumes: 작업 2의 재구성 화면이 같은 비교를 대신한다.
- Produces: 없음

- [ ] **Step 1: 제거 후 참조가 남지 않는지 확인**

Run: `rg -n "SpriteSourceCompare|assetSourceRect|원본 위 재배치" apps/frontend docs/xHuman`
Expected: 출력 없음

- [ ] **Step 2: `frontend.md` 갱신**

2D 배경 스튜디오 절의 `SpriteSourceCompare.tsx` 항목을 `reconstruction.ts`·`useTileFill.ts`·`SpriteReconstructionLayer.tsx`·`PartsOverlay` `underlay` 설명으로 바꾼다. 확인 기준의 "2D 원본 비교 확인" 줄은 측정 당시 기록이므로 그대로 두고, 그 아래에 이 계획 링크와 함께 "패널은 계획 검수 재구성으로 대체" 한 줄을 덧붙인다.

- [ ] **Step 3: Frontend 단위·lint·typecheck 확인**

Run: `pnpm test && pnpm lint && pnpm typecheck`
Expected: 모두 통과

- [ ] **Step 4: Commit**

```bash
git add -A apps/frontend/src/features/screens/sprites/SpriteSourceCompare.tsx
git add apps/frontend/src/features/screens/sprites/SpriteStudioScreen.tsx apps/frontend/src/domain/sprites/rules.ts apps/frontend/src/domain/sprites/rules.test.ts apps/frontend/tests/e2e/sprites-roles.spec.ts docs/xHuman/frontend.md
git commit -m "refactor(frontend): replace source compare panel with plan review reconstruction"
```

## Task 4: Backend 도메인 `Placements`

**Files:**
- Modify: `apps/backend/Noxtend.Domain/Sprites/SpriteTypes.cs`, `SpriteRules.cs`, `SpritePipelineState.cs`, `apps/backend/Noxtend.Domain/Job/PipelineJob.Sprites.cs`, `apps/backend/Noxtend.Application/Sprites/RunSpriteGenerationTaskHandler.cs`, `apps/backend/Noxtend.Application/Sprites/SpriteCommandsHandler.cs`
- Test: `apps/backend/Noxtend.Tests/Domain/SpriteRulesTests.cs`, `Domain/SpriteLifecycleTests.cs`, `Application/SpriteGenerationTests.cs`, `Application/SpriteCommandTests.cs`, `Infrastructure/SpritePersistenceTests.cs`

**Interfaces:**
- Produces:
  - `SpriteAssetPlan(..., SpriteAssetRole? Role = null, IReadOnlyList<Bounds>? Placements = null)`. 레코드에 `Equals(SpriteAssetPlan?)`·`GetHashCode()`를 직접 정의해 `Placements`를 순서 있는 값으로 비교한다(null과 빈 목록은 다르다).
  - `SpriteAsset.ReplacePlacements(IReadOnlyList<Bounds>? placements)` (internal): `Plan = Plan with { Placements = placements }`. 승인·프레임은 건드리지 않는다.
  - `SpriteRules.Validate` 오류 메시지: `"오브젝트만 자리를 가질 수 있습니다"`, `"오브젝트 자리는 원본 내부 영역 1~20개여야 합니다"` (둘 다 `ErrorCode.SpritePlanInvalid`)

- [ ] **Step 1: 실패하는 테스트 작성**

- `SpriteRulesTests.Validate_ObjectPlacements_AcceptsOneToTwentyInsideSource`: 1개·20개 통과, 0개·21개·NaN·원본 밖·w≤0은 두 번째 메시지로 실패
- `SpriteRulesTests.Validate_RejectsPlacementsOnNonObjectRoles`: tile·backdrop·layer에 값이 있으면 첫 번째 메시지로 실패
- `SpriteLifecycleTests.SpriteAssetPlan_EqualityComparesPlacementsByValue`: 같은 좌표의 새 목록끼리는 같고, 순서나 값이 다르면 다르다
- `SpriteLifecycleTests.ReplaceSpritePlan_PlacementsOnly_KeepsApprovalExportsAndCompletion`: 완료(`Succeeded`, 현재 ZIP 있음) 작업에서 자리만 바꾸면 `ReviewRevision`이 1 오르고, `Approval`·`ApprovedBaseImageId`·프레임·`Exports.Single().IsCurrent == true`·`Status`·`Phase`·`CompletedAt`이 그대로다
- `SpriteLifecycleTests.ReplaceSpritePlan_PlacementsWithRename_UsesExistingRules`: 자리와 이름을 함께 바꾸면 기존처럼 `Approval == null`이고 ZIP 현재 표시가 내려간다
- `SpriteLifecycleTests.ReplaceSpritePlan_PlacementsOnly_KeepsRunningTaskCurrent`: 실행 중인 기준 생성 공정이 있을 때 자리만 저장해도 `IsCurrentTask(task)`가 true이고 취소되지 않는다
- `SpriteGenerationTests.GeneratePrompt_AssetJsonExcludesPlacements`: 자리가 있는 오브젝트의 생성 요청 `asset` 변수 JSON에 `placements` 키가 없다
- `SpritePersistenceTests.PlanJson_WithoutPlacements_ReadsNullAndRoundTrips`: `placements` 키가 없는 기존 `PlanJson`은 `Placements == null`로 읽히고, 값이 있는 계획은 저장 후 같은 값으로 읽힌다
- `SpriteCommandTests.UpdatePlan_ReplaysPrePlacementsReceipt`: 새 필드가 없는 변경 전 직렬화 형식으로 만든 fingerprint·receipt fixture를 저장한 뒤 `Placements = null`인 동일 요청 재전송이 기존 receipt를 반환하고 revision을 올리지 않는다. 새 serializer로 fixture를 생성하지 않는다.
- `SpriteCommandTests.UpdatePlan_FingerprintIncludesPlacementValuesAndOrder`: 같은 값의 새 목록은 같은 requestId로 재전송 가능하고, 좌표·목록 순서 변경은 `SpriteRequestConflict`다. 기존 ID 정렬·Role 기본값·route/revision 테스트를 유지한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test apps/backend/Noxtend.slnx --filter "FullyQualifiedName~SpriteRulesTests|FullyQualifiedName~SpriteLifecycleTests|FullyQualifiedName~SpriteGenerationTests|FullyQualifiedName~SpriteCommandTests|FullyQualifiedName~SpritePersistenceTests"`
Expected: 새 테스트 FAIL(컴파일 오류 포함)

- [ ] **Step 3: 구현**

- `ReplaceSpritePlan`은 검증과 `NormalizeRole` 직후, 캔버스 계산 전에 자리 전용 분기를 둔다.

```csharp
var placementsOnly = state.Grid == grid && state.Assets.Count == plans.Count
    && plans.All(p => state.Assets.Any(a => a.Plan == p with { Placements = a.Plan.Placements }))
    && plans.Any(p => !state.Assets.Any(a => a.Plan == p));
if (placementsOnly)
{
    foreach (var plan in plans) state.Assets.Single(a => a.Id == plan.Id).ReplacePlacements(plan.Placements);
    state.Touch([]);
    return Result<bool>.Ok(true);
}
```

- `GenerationChanged`는 바꾸지 않는다(`Placements` 미포함).
- 생성 프롬프트는 `plan with { Role = role, Placements = null }`을 직렬화하고, null `placements` 키가 출력되지 않게 한다.
- `SpriteCommandsHandler.CreateFingerprintJson`의 기존 속성별 modifier에 `Placements` null 생략만 추가한다. 값이 있으면 좌표·배열 순서를 직렬화하며 전역 null 옵션·기존 fingerprint·receipt는 바꾸지 않는다.

- [ ] **Step 4: 통과 확인**

Run: 2단계와 같은 명령
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/backend/Noxtend.Domain/Sprites/SpriteTypes.cs apps/backend/Noxtend.Domain/Sprites/SpriteRules.cs apps/backend/Noxtend.Domain/Sprites/SpritePipelineState.cs apps/backend/Noxtend.Domain/Job/PipelineJob.Sprites.cs apps/backend/Noxtend.Application/Sprites/RunSpriteGenerationTaskHandler.cs apps/backend/Noxtend.Application/Sprites/SpriteCommandsHandler.cs apps/backend/Noxtend.Tests/Domain/SpriteRulesTests.cs apps/backend/Noxtend.Tests/Domain/SpriteLifecycleTests.cs apps/backend/Noxtend.Tests/Application/SpriteGenerationTests.cs apps/backend/Noxtend.Tests/Application/SpriteCommandTests.cs apps/backend/Noxtend.Tests/Infrastructure/SpritePersistenceTests.cs
git commit -m "feat(sprites): store object placements without regeneration"
```

## Task 5: HTTP 계약 `placements`

**Files:**
- Modify: `apps/backend/Noxtend.Api/Contracts/SpriteContracts.cs`, `apps/backend/Noxtend.Tests/Api/SpriteApiTests.cs`, `apps/frontend/src/infra/api/fixtures/sprite-roles.request.json`, `apps/frontend/src/infra/api/fixtures/sprite-roles.server.json`

**Interfaces:**
- Consumes: 작업 4의 `SpriteAssetPlan.Placements`
- Produces: `SpriteAssetPlanRequest(..., string? Role = null, IReadOnlyList<SpriteBoundsRequest>? Placements = null)`. 요청과 응답(`SpriteAssetResponse.Plan`)이 같은 레코드를 쓴다. 응답의 `placements`는 저장값 그대로(null 포함)다.

- [ ] **Step 1: 실패하는 테스트 작성**

- `SpriteApiTests.JsonBinding_PlanRequestPlacements_MapsToDomain`: 유한한 자리 2개는 도메인 `Placements` 2개가 된다. NaN이 섞이거나 원소가 null이면 400 `SpritePlanInvalid`다.
- `sprite-roles.request.json`의 `tiles` 시나리오 오브젝트에 `placements` 2개를 추가한다. 기존 `JsonBinding_RequestFixtureStoresRolesAndGrid`가 저장값까지 확인하도록 단언 1줄을 더한다.
- 서버 fixture 테스트 시나리오에 자리 2개짜리 오브젝트를 넣는다.
- `SpriteApiTests.JsonBinding_OmittedAndNullPlacementsReplayLegacyReceipt`: 작업 4와 같은 변경 전 receipt를 준비하고 `placements` 누락/명시적 null 요청 모두 같은 receipt·revision을 반환하는지 실제 JSON binding 경로로 확인한다. 값이 있는 새 목록도 동일 값 재전송은 성공하고 좌표·순서 변경은 409 `SpriteRequestConflict`인지 검증한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test apps/backend/Noxtend.slnx --filter "FullyQualifiedName~SpriteApiTests"`
Expected: FAIL

- [ ] **Step 3: `ToDomain`·`From` 구현과 서버 fixture 재생성**

Run: `NOXTEND_REGENERATE_SPRITE_ROLES_FIXTURE=1 dotnet test apps/backend/Noxtend.slnx --filter "FullyQualifiedName~SpriteApiTests"`
Expected: `sprite-roles.server.json`에 `placements` 필드가 생긴다(다른 asset은 `null`). `git diff`로 의도한 변경만 있는지 확인한다.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test apps/backend/Noxtend.slnx --filter "FullyQualifiedName~SpriteApiTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/backend/Noxtend.Api/Contracts/SpriteContracts.cs apps/backend/Noxtend.Tests/Api/SpriteApiTests.cs apps/frontend/src/infra/api/fixtures/sprite-roles.request.json apps/frontend/src/infra/api/fixtures/sprite-roles.server.json
git commit -m "feat(api): expose sprite object placements"
```

## Task 6: Frontend 계약·Fake

**Files:**
- Modify: `apps/frontend/src/domain/sprites/types.ts`, `apps/frontend/src/domain/sprites/reconstruction.ts`(`placementsOf`), `apps/frontend/src/infra/api/spriteApi.ts`, `apps/frontend/tests/e2e/spriteFakeApi.ts`
- Test: `apps/frontend/src/infra/api/spriteContract.test.ts`, `apps/frontend/src/domain/sprites/rules.test.ts`(reader), `apps/frontend/src/domain/sprites/reconstruction.test.ts`

**Interfaces:**
- Consumes: 작업 5의 fixture
- Produces:
  - `SpriteAssetPlan.placements: Bounds[] | null`. `readPlan`은 누락(`undefined`)을 null로 정규화하고 명시적 `null`도 그대로 허용한다. 배열이면 각 원소의 유한한 x·y·w·h를 요구한다. 그 밖의 값은 계약 오류이며 reader에서 `[sourceBounds]`로 바꾸지 않는다.
  - `updateSpritePlan` 본문의 각 asset에 `placements`(null 또는 `{x,y,w,h}[]`)를 명시적으로 직렬화한다.
  - `placementsOf(plan) = plan.placements ?? [plan.sourceBounds]`
  - Fake: 역할 시드의 `오브젝트 바위`는 `placements` 2개를 가진다. 계획 저장에서 `placements`만 다르면 `approval`·이미지·단계를 유지하고 revision만 올린다(서버 작업 4와 같은 규칙). 작업 2 E2E의 `image[data-asset-id]` 개수 단언을 3(원경 배경 1·오브젝트 자리 2)으로 바꾼다.

- [ ] **Step 1: 실패하는 테스트 작성**

- `spriteContract.test.ts`: 기존 서버 fixture 읽기 테스트에 오브젝트 `placements` 2개와 다른 asset `null` 단언을 더한다. 요청 fixture 비교 테스트는 `placements`까지 같아야 한다.
- `rules.test.ts`(reader): 누락→null, 명시적 null 유지, 유효한 배열 유지의 세 경우를 각각 검증한다. `[{x:0,y:0,w:1}]`·`[null]`·`'x'`·비유한 좌표는 `toThrow()`다.
- `reconstruction.test.ts`: `placementsOf`가 null이면 `[sourceBounds]`를, 값이 있으면 그 목록을 돌려준다.

- [ ] **Step 2: 실패 확인**

Run: `pnpm --filter @nextend/frontend exec vitest run src/infra/api/spriteContract.test.ts src/domain/sprites`
Expected: FAIL

- [ ] **Step 3: 구현**

- [ ] **Step 4: 통과 확인**

Run: `pnpm test && pnpm typecheck`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/frontend/src/domain/sprites apps/frontend/src/infra/api/spriteApi.ts apps/frontend/src/infra/api/spriteContract.test.ts apps/frontend/tests/e2e/spriteFakeApi.ts apps/frontend/tests/e2e/sprites-reconstruction.spec.ts
git commit -m "feat(frontend): read and write sprite object placements"
```

## Task 7: 계획 검수 자리 편집

**Files:**
- Modify: `apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx`, `apps/frontend/src/features/screens/sprites/useSpriteRoiEditor.ts`, `apps/frontend/src/features/screens/background/PartsOverlay.tsx`, `apps/frontend/src/domain/sprites/reconstruction.ts`
- Test: `apps/frontend/tests/e2e/sprites-placements.spec.ts` (신규), `apps/frontend/src/domain/sprites/reconstruction.test.ts`

**Interfaces:**
- Consumes: 작업 6의 `placements`·`placementsOf`, 작업 2의 재구성 층
- Produces:
  - `useSpriteRoiEditor`의 `onCommit(partId: string, ordinal: number, bounds: Bounds)`, `selected: { partId: string; ordinal: number } | null`, `select(partId: string, ordinal: number)`. 선택은 좌표 변경 없는 pointerup 뒤에도 유지하며 드래그 preview와 분리한다.
  - `PartsOverlay.selection`은 기존 `selectedId`·`onSelect(id)`를 유지하며 선택적인 `selectedOrdinal`·`onSelectPlacement(id, ordinal)`을 더한다. 지정된 경우 클릭한 자리 번호를 전달하고 그 자리에만 손잡이를 그린다. 기존 3D 호출자의 다중 상자 표시·마우스 처리는 유지한다. `OverlayPart`에 선택적인 `boxKind: 'source' | 'placement'`를 추가해 실선/점선을 구분하며 미지정 시 기존 외관을 유지한다.
  - 자리 상자는 `OverlayPart`를 따로 둔다. `id = \`${plan.id}:placements\``, `label = \`${n}. ${name} · 자리\``, `placements = placementsOf(draftPlan)`이다. 생성 기준 상자는 기존 `id = plan.id` 그대로다. `onCommit`은 id 접미사로 `sourceBounds` 수정과 `placements[ordinal]` 수정을 나눈다. `lockedIds`에는 실행 중 asset의 두 id를 모두 넣는다.
  - 오브젝트 카드의 버튼: `자리 추가 {n}`(해당 에셋에서 선택한 자리 또는 마지막 자리를 오른쪽 아래로 0.02 옮겨 원본 안에 맞춘 복사본, 최대 20개에서 비활성). 추가 후 새 자리를 선택한다. `선택한 자리 삭제 {n}`은 해당 에셋의 자리를 선택하지 않았거나 1개 남으면 비활성이다. 삭제 후 `min(삭제한 ordinal, 남은 개수 - 1)`을 선택한다.
  - 카드에 키보드 접근 가능한 `생성 기준 선택 {n}`·`자리 {k} 선택 {n}` 버튼과 `aria-pressed`를 둔다. 겹친 상자도 각각 선택할 수 있고 정밀 입력은 선택한 종류·번호의 좌표를 편집한다. 생성 기준은 기존 ROI 입력 이름을 유지하고 자리는 `자리 {k} ROI x/y/w/h {n}`으로 구분한다.
  - 역할 변경·대상 삭제·최신 계획 불러오기 시 유효하지 않은 선택을 해제한다. 드래그 중에는 기존 취소 규칙을 유지하고, 실제 plans 교체·자리 추가/삭제 시 드래그를 취소한다. 미저장 draft를 유지하는 polling/409에서는 그 draft의 유효한 선택도 유지한다.
  - `projection`에 `placements`를 포함한다. 역할이 object가 아니게 바뀌면 draft의 `placements`를 null로 만든다.
  - `moveGenerationBox(plan: SpriteAssetPlan, bounds: Bounds, settings: SpriteSettings): SpriteAssetPlan`(`reconstruction.ts`): `sourceBounds`를 바꾸고, 오브젝트이며 `placements`가 null이면 먼저 `[이전 sourceBounds]`로 채워 자리가 따라 움직이지 않게 한다. 생성 기준 상자의 드래그와 정밀 입력이 모두 이 함수를 쓴다.

- [ ] **Step 1: 실패하는 테스트 작성**

`reconstruction.test.ts`의 `moveGenerationBox keeps null placements at the previous bounds`: 자리 null 오브젝트를 옮기면 `placements`가 `[이전 sourceBounds]`가 된다. 자리가 있거나 tile 역할이면 `sourceBounds`만 바뀐다.

추가 E2E: `clicking the second placement deletes only that placement`는 이동 없이 두 번째 클릭→삭제→다음/마지막 선택 보정과 다른 좌표 보존을 확인한다. `overlapping source and placement boxes are keyboard selectable`는 완전히 겹친 상자를 버튼으로 각각 선택·정밀 수정한다. 역할 변경·최신 계획 불러오기·드래그 중 삭제 후 유효하지 않은 선택/늦은 commit이 없는지도 검증한다.

```ts
test('placement boxes are edited separately from the generation box', async ({ page }) => {
  // seed baseReview, records 수집
  // '오브젝트 바위 · 자리' 칩의 상자 2개, 생성 기준 상자 1개
  // 자리 상자 body를 드래그 → '계획 저장' → records 마지막 plan 본문의 object.placements[0]만 바뀌고 sourceBounds 동일
  // 저장 후 기준 이미지 카드의 '승인됨' 표시와 재구성 오브젝트 이미지 수(2)가 유지됨
})
test('add and delete placements stay within one to twenty', async ({ page }) => {
  // '자리 추가 3' 클릭 → 자리 상자 3개·새 자리 선택, '선택한 자리 삭제 3' 두 번 → 1개·삭제 비활성
  // 20개까지 추가 → 추가 비활성, 21번째 추가 불가
})
test('role change clears placements in the saved body', async ({ page }) => {
  // 오브젝트 역할을 '반복 타일'로 바꾸고 저장 → 그 asset의 placements === null
})
test('unsaved placement edits survive refresh and conflict', async ({ page }) => {
  // conflictOnce: 자리 드래그 후 새로고침·409에서도 draft 자리가 유지됨
})
```

- [ ] **Step 2: 실패 확인**

Run: `pnpm --filter @nextend/frontend exec vitest run src/domain/sprites/reconstruction.test.ts && pnpm --filter @nextend/frontend build && pnpm --filter @nextend/frontend exec playwright test tests/e2e/sprites-placements.spec.ts`
Expected: FAIL

- [ ] **Step 3: 구현**

- [ ] **Step 4: 통과 확인**

Run: `pnpm --filter @nextend/frontend build && pnpm --filter @nextend/frontend exec playwright test tests/e2e/sprites-placements.spec.ts tests/e2e/sprites-roi.spec.ts tests/e2e/sprites-roles.spec.ts tests/e2e/sprites-reconstruction.spec.ts tests/e2e/review-gate.spec.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx apps/frontend/src/features/screens/sprites/useSpriteRoiEditor.ts apps/frontend/src/features/screens/background/PartsOverlay.tsx apps/frontend/src/domain/sprites/reconstruction.ts apps/frontend/src/domain/sprites/reconstruction.test.ts apps/frontend/tests/e2e/sprites-placements.spec.ts
git commit -m "feat(frontend): edit object placements in sprite plan review"
```

## Task 8: 분석 프롬프트 V5

**Files:**
- Create: `apps/backend/Noxtend.Infrastructure/Persistence/Migrations/20261018080000_ReviseSpriteAnalyzePromptV5.cs`, `apps/backend/Noxtend.Tests/Infrastructure/SpriteAnalyzePromptV5MigrationTests.cs`
- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs`, `apps/backend/Noxtend.Application/Sprites/SpritePlanParser.cs`, `apps/backend/Noxtend.Infrastructure/Llm/FakeLlmProvider.cs`, `apps/backend/Noxtend.Tests/Infrastructure/SeedPromptVariableTests.cs`, `apps/backend/Noxtend.Tests/Application/SpriteAnalysisTests.cs`

**Interfaces:**
- Consumes: 작업 4의 `Placements`·`SpriteRules.Validate`
- Produces:
  - `SeedPrompts.AnalyzeSpritesV5()`: V4 schema의 asset 항목에 `placements`를 추가한다. 형식은 `anyOf: [{type: null}, {type: array, minItems: 1, maxItems: 20, items: bounds}]`이고 `required`에도 넣는다. 시스템 문장은 V4에서 설계 4절의 세 가지(벽 제외 타일 정의, 서 있는 구조물은 오브젝트, 모든 자리와 대표 `sourceBounds`, 긴 벽은 구간 오브젝트와 여러 자리)를 바꾼다. Note: `"이미지 기반 2D 배경 역할·격자·자리 분석 v5"`
  - Migration ID: `PreviousId = c7000000-0000-4000-8000-000000000004`, `AnalyzeId = c7000000-0000-4000-8000-000000000005`, `SeededAt = 2026-10-18T08:00:00Z`. V4 migration과 같은 보존 규칙이다.
  - `SpritePlanParser`: asset 속성 집합은 V4 집합 또는 V4 집합 + `placements`다. 값이 null이면 `Placements = null`, 배열이면 각 bounds의 `x,y,w,h`를 요구한다. 검증은 `SpriteRules.Validate`에 맡긴다.
  - `FakeLlmProvider` 타일 분석: 오브젝트 하나에 자리 2개, 나머지는 `placements: null`

- [ ] **Step 1: 실패하는 테스트 작성**

- `SpriteAnalyzePromptV5MigrationTests`: `SpriteAnalyzePromptV4MigrationTests`의 다섯 테스트(수정 안 된 V4 활성 → V5 활성, 운영자 버전 활성 → 무변경, 시드 직접 수정 → 무변경, Down 복구, 다른 슬롯 보존)를 V4→V5로 옮긴다.
- `SeedPromptVariableTests`: V5 변수가 V4와 같고, schema의 asset `required`에 `placements`가 있다.
- `SpriteAnalysisTests.Parse_AcceptsResponsesWithAndWithoutPlacements`: V4 형식 응답은 `Placements == null`, V5 형식 오브젝트 자리 2개는 그대로 들어간다. tile에 자리가 있거나 21개면 실패한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test apps/backend/Noxtend.slnx --filter "FullyQualifiedName~SpriteAnalyzePromptV5MigrationTests|FullyQualifiedName~SeedPromptVariableTests|FullyQualifiedName~SpriteAnalysisTests"`
Expected: FAIL (migration 테스트는 Docker SQL 필요)

- [ ] **Step 3: 구현**

- [ ] **Step 4: 통과 확인**

Run: 2단계와 같은 명령
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs apps/backend/Noxtend.Infrastructure/Llm/FakeLlmProvider.cs apps/backend/Noxtend.Infrastructure/Persistence/Migrations/20261018080000_ReviseSpriteAnalyzePromptV5.cs apps/backend/Noxtend.Application/Sprites/SpritePlanParser.cs apps/backend/Noxtend.Tests/Infrastructure/SpriteAnalyzePromptV5MigrationTests.cs apps/backend/Noxtend.Tests/Infrastructure/SeedPromptVariableTests.cs apps/backend/Noxtend.Tests/Application/SpriteAnalysisTests.cs
git commit -m "feat(sprites): analyze object placements with prompt v5"
```

## Task 9: 문서·전체 검증

**Files:**
- Modify: `docs/xHuman/backend.md`, `docs/xHuman/frontend.md`, `docs/xHuman/providers-and-prompts.md`, `docs/xHuman/CODEMAP.md`(2D 에셋 역할 행에 자리·재구성 추가), 이 계획의 `## 실행 기록`

**Interfaces:**
- Consumes: 작업 1~8 전부
- Produces: 없음

- [ ] **Step 1: 지도 갱신**

`backend.md`에는 `Placements` 저장·검증·자리 전용 저장 분기, `frontend.md`에는 자리 편집과 재구성 층, `providers-and-prompts.md`에는 `AnalyzeSpritesV5`와 migration 보존 규칙을 쓴다. 각 지도의 확인 기준 날짜와 revision도 갱신한다.

- [ ] **Step 2: 계약 리뷰**

`api-breaking-change-detector`로 `SpriteContracts.cs`와 `spriteApi.ts`·`types.ts`를 대조한다. [연결·경계 검증](../../../.agents/skills/noxtend-workflow/references/verification.md#연결경계-검증)을 수행한다.

- [ ] **Step 3: 전체 회귀**

Run: `dotnet build apps/backend/Noxtend.slnx && dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`
Expected: 실패 0, 건너뜀 0

Run: `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`
Expected: 모두 통과, E2E 건너뜀 0

- [ ] **Step 4: 화면 관찰**

Fake E2E 화면을 1440px·390px, light·dark로 캡처한다. 로컬 Docker API의 기존 작업 `300f237d`는 조회만 해서 재구성 층과 계산 시간을 확인한다. Azure 운영에서 이미지 응답에 CORS가 적용되는지는 `deploy/azure/` 정의(App Service CORS)를 읽어 확인하고, 실행하지 않은 확인은 미확인으로 기록한다. 실제 V5 분석은 사용자 승인이 있을 때만 [평가 절차](../../xHuman/prompt-evaluation.md)로 진행한다.

- [ ] **Step 5: 실행 기록 작성과 Commit**

```bash
git add docs/xHuman/backend.md docs/xHuman/frontend.md docs/xHuman/providers-and-prompts.md docs/xHuman/CODEMAP.md docs/superpowers/plans/2026-10-18-2d-source-reconstruction.md
git commit -m "docs: record 2D source reconstruction implementation"
```

## 실행 기록

### 2026-10-18 구현과 검증 결과

실행 방식은 작업별 구현·독립 리뷰(Subagent-driven)다. 브랜치 `claude/2d-source-reconstruction`, 시작 `c033cd6`(main과의 merge-base `c780385`). 커밋 트레일러는 실제로 작성한 모델을 적었다. Tasks 1–8 구현·작업별 리뷰를 마쳤고 Task 9가 이 기록의 커밋이다.

| Task | 커밋 범위 | 리뷰 결과 |
| --- | --- | --- |
| 1 | `c033cd6..61fa391` | 수정 1회: 표본 간격 4px 누락(전 픽셀 표본이라 5초 예산 초과) → 반영 후 통과 |
| 2 | `61fa391..a560a1c` | 수정 1회: no-cors `<img>`가 남긴 HTTP 캐시 때문에 CORS 픽셀 읽기 실패 → `fetchImageBlob`(`mode: 'cors'`·`cache: 'reload'`), 표시 이미지 로딩 실패가 빈칸으로 숨겨짐 → 점선 윤곽과 `role="alert"` |
| 3 | `a560a1c..f2972e2` | 수정 2회: 지도의 "클릭 비통과" 문구 정정과 삭제한 테스트의 배경·오브젝트 기하 단언 복원, 배경 ROI가 전체 캔버스와 같아 단언이 못 잡던 문제 → 응답 재작성 테스트 |
| 4 | `f2972e2..3f8a7e9` | 승인 |
| 5 | `3f8a7e9..b3e5b79` | 승인 |
| 6 | `b3e5b79..20bdfb4` | 승인 |
| 7 | `20bdfb4..d46bf13` | 수정 1회: 생성 기준·자리 상자가 정확히 겹치면 zIndex 동률로 자리가 항상 위 → 선택한 상자를 위로 |
| 8 | `d46bf13..516a39d` | 승인 |

결정(Ruling, 원장의 전부):

- P1: 워커 오류는 `{ error: { name, message } }`이고 `RangeError` 이름으로 칸 상한 문구를 고른다(명시적·안정적인 전달 형식을 위해서다. `RangeError` 같은 내장 Error는 structured clone으로 전달되지만 사용자 정의 하위 클래스는 보존되지 않는다. 최종 수정에서 정정).
- P2: Task 6이 `tests/e2e/sprites-reconstruction.spec.ts`를 고칠 수 있다(계획의 커밋 목록에는 있고 Files 머리글에서 빠진 것은 오기).
- P3: T5~T6 사이 Frontend 계약 테스트 1건 적색을 허용했다(T5는 Backend만 검증, T6이 녹색으로 되돌림).
- P4: 모든 작업은 경로를 지정해 stage한다(삭제는 `git rm`, 일괄 stage는 에이전트 훅이 막는다).
- 서브에이전트 커밋 트레일러는 실제로 작성한 모델(Sonnet)을 적고 `conventions.md`도 고쳤다.
- T2: 리뷰 Minor 중 오브젝트·레이어 `<image>` 로딩 실패의 무음 빈칸은 스펙의 "빠진 에셋을 숨기지 않는다"에 따라 Important로 올렸다. 원경 배경은 `SpriteRules.Validate`가 최대 1개로 보장하고 파서가 `Validate`를 부르므로 다중 배경 처리는 하지 않는다. 스펙의 8px 오프셋 탐색은 유지했다(큰 칸·최대 원본은 5초 시간 초과 문구로 드러나고 편집은 계속 가능하며, 고치려면 coarse-to-fine 탐색이 필요해 사용자 판단 사항이다).
- T3: Step 1의 `rg` "출력 없음"은 `apps/frontend`에만 적용한다(`docs/xHuman`에는 계획이 유지하라고 한 옛 "2D 원본 비교 확인" 줄과 대체 안내가 남는다). 계획 지시 테스트 삭제는 유지하되 배경·오브젝트 `<image>`의 기하 단언을 기존 E2E에 추가한다. 옛 비교 패널 기록의 대체 안내 한 줄은 Task 9 문서 작업에서 추가했다.
- T5: `sprite-roles.server.md`의 자리 설명은 fixture를 읽는 Task 6이 맡았다.

남은 minor(이월, 수정하지 않음). 원장의 `minor (deferred)` 전부이며 수정 라운드에서 해소된 항목은 뺐다.

- T1: 칸 상한 `RangeError`가 뒤쪽 오프셋에서 나면 앞 오프셋의 거리 계산을 끝낸 뒤에야 드러난다(모든 오프셋 격자를 먼저 검사하는 안). 테스트 주석 "4px 표본이면 grid4부터 거리 0"은 부정확하다(grid2도 0점이고 먼저 나열된 grid4가 동률 규칙으로 이긴다).
- T2 재구성 훅·층: 같은 key는 A→B→A·`enabled` 토글에서 다시 계산하고 일시 오류도 재시도하지 않는다. 밀려난 effect가 `done` 확인 전에 이미지를 decode·`getImageData`한다. 타일 `<image>` 그룹은 메모이즈되지 않아 미리보기·슬라이더 프레임마다 최대 4,096노드를 재조정하고 `onError`도 없다. 기준 이미지 조회가 훅·층에 중복이고 도메인에는 비공개 `baseImageId`가 있다. `ImageBitmap.close()`가 `getContext`가 null이거나 `drawImage`가 던지면 건너뛴다. `cache: 'reload'`는 key가 바뀔 때마다 다시 받고 밀려난 요청을 중단하지 않는다. `fetchImageBlob`은 `ApiError`가 아닌 일반 `Error`를 던지고, `mode: 'cors'`·`cache: 'reload'`·`!ok` 던짐을 고정하는 단위 테스트가 없다(`client.test.ts` 하니스가 있어 가장 권할 후속). 4096² 원본 래스터가 메인 스레드 long task 70~82ms를 만들고 타이머가 워커 전송 시점에 시작해 이미지 요청이 멈추면 "계산 중"으로 남는다.
- T2 사용자에게 보일 수 있는 것: `useTileFill.ts`의 `worker.onerror`가 워커의 원문 영문 오류 message를 그대로 표시할 수 있다. `tileFill.worker.ts` 3행 주석의 전제도 부정확하다(내장 Error는 structured clone으로 전달된다).
- T2 테스트: `outputKind: 'layers'` 전체 캔버스 분기(T3 리뷰에서도 지적)와 같은 오브젝트 아래쪽 y 정렬(시드 저장 순서가 이미 정렬 순서)을 가르는 테스트가 없다. planReview 테스트는 phase 조건과 이미지 조건을 구분하지 못한다.
- `SpritePlanReview.tsx`는 T2에서 529 → 583줄, T7에서 737줄이 되었다(오브젝트 자리 컨트롤 약 60줄 JSX가 추출 후보).
- T4 Backend: `SpriteJsonSerializer`의 읽기·쓰기 검증은 `Placements`를 확인하지 않는다(`[null]`·0/21개·범위 밖 값이 DB에서 읽힘, 쓰기는 `SpriteRules`가 막음). `RunSpriteGenerationTaskHandler`가 `"placements"` 문자열과 `with { Placements = null }` 이중 장치를 쓴다(`ShouldSerialize` 하나가 더 명확). 직접 정의한 `SpriteAssetPlan.Equals`에 "새 필드는 Equals에 추가" 주석이 없다. 신규 행의 `"placements":null`은 이전 이미지가 읽지 못한다(아래 롤백·배포 주의).
- T5: 변경 전 fingerprint 원문 JSON이 `SpriteApiTests`·`SpriteCommandTests`에 중복이다. `Assert.Equal(first.TaskIds, replay.TaskIds)`는 빈 목록 둘의 비교다.
- T6: E2E `saving only placements…`가 `exports[].isCurrent`·작업 `status`를 직접 단언하지 않는다. 그리기 순서 E2E는 아래쪽 y 정렬 누락을 감지하지 못한다(시드 저장 순서가 정렬 순서). reader가 `[]`를 받아 `placementsOf([])`가 아무것도 그리지 않는다(서버는 금지, 정책 테스트 없음). Fake의 자리 전용 저장 판정이 `JSON.stringify` 키 순서에 의존한다.
- T7: 자리 상자가 `BOX_COLORS` 슬롯을 하나 더 써서 같은 대상의 생성 기준과 자리 색이 다르고 뒤 대상 색이 밀린다. 상자 zIndex를 두 배로 한 뒤 칩(`z-[2]`)이 정확히 겹친 선택 상자에 가려져 반대편 칩 클릭이 가로채인다(카드 버튼·키보드는 가능, 칩 z를 올릴 수 있음). 선택이 없으면 정확히 겹친 상자는 DOM 순서로 정해져 첫 클릭이 자리 1을 고른다.
- T8: 분할 지시가 반복되지 않는 큰 구조물에 모호해 조용히 잘못된 구간 계획을 낼 수 있다(반복 벽·울타리로 좁히고 출력 크기 계산식을 쓰는 안, 실모델 평가 필요, 사용자 판단 사항). 파서가 asset별 `placements` 혼재를 받는다(무해). V5 schema의 OpenAI payload 회귀가 없다(`OpenAiSpriteSchemaTests`는 V4만). V5 migration "수정된 시드" 테스트가 System만 바꾼다. 테스트 이름 "…OnlyOnTheTileObject"가 모호하고 V3의 "A roof is not a tile"이 V5 규칙과 중복이다.
- T9: `SpriteStudioScreen.tsx`의 기존 React 중복 key 경고(`SpriteFrameReview`·`SpriteExport`가 `key={job.id}` 공유, `c780385`에도 있음). `sprite-roles.server.md` 기준 revision 줄이 두 revision(`c2dc3f9` + `b3e5b79` 재생성)을 함께 보인다.

수정 라운드에서 이미 해소: T1 int32 누적·반 칸 생략 단순화(제거), T3 지도 문구 3건, T7 Fake `inside()`의 null 원소 TypeError(400으로 정리).

#### 재구성 계산 측정(Task 1·2)

Node 26 순수 `fillTiles`(타일 8종, 원경 포함, 4px 표본) 1536×1024 isometric 128×64는 0.65초, 사각 128×128은 1.05~1.16초다. 브라우저(dev 모듈 Worker, 로컬 API 실제 작업 `300f237d`, 1536×1024·타일 8종·413칸)는 이미지 읽기·래스터 172~251ms, Worker 약 460ms, 결과→페인트 약 50ms, SVG 414노드였고 입력 응답은 대부분 한 프레임(최대 64ms)이었다. production build + Fake 합성(타일 3종)은 아래와 같다.

| 입력 | Worker | SVG 노드 | 결과 |
| --- | --- | --- | --- |
| 1536×1024, 칸 128 | 244ms | 430 | 표시 |
| 1536×1024, 칸 42(상한 근처) | 81ms | 3,713 | 표시, 입력 응답 16ms |
| 4096×4096, 칸 1 | RangeError 19ms | 0 | 칸 상한 문구 |
| 4096×4096, 칸 132(상한 근처) | 2,781ms | 4,000 | 표시, long task 69ms 1건 |
| 4096×4096, 칸 256 | 5초 | 0 | 시간 초과 문구 |

상한 안에서 화면은 멈추지 않았다. 계산 시간은 오프셋 수 × 원본 표본 수 × 후보 수에 비례해 큰 칸·최대 원본에서 5초를 넘는다(Node 순수 계산, 타일 8종 + 원경 배경: 1536×1024 칸 512px 8.5초, 4096×4096 칸 132px 9.1초. 브라우저 합성의 2,781ms는 타일 3종·원경 없음이라 후보 수가 달라 같은 입력끼리의 모순이 아니다). 큰 칸 작업은 coarse-to-fine 탐색 등 별도 판단이 필요하다.

#### Task 9 검증 (기준 `516a39d`, 작업 트리 변경은 문서뿐)

| 검사 | 명령 | 결과 |
| --- | --- | --- |
| Backend 빌드 | `dotnet build apps/backend/Noxtend.slnx` | 오류 0, 경고 8(NU1903·CS0618·xUnit2031, 기존) |
| Backend 전체 | `dotnet test apps/backend/Noxtend.slnx --no-build --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`(위 빌드 직후, Docker 사용) | 통과 2063·실패 0·건너뜀 0, 13분 35초 |
| Frontend | `pnpm test` / `pnpm lint` / `pnpm typecheck` | 61 파일 572 통과 / eslint·oxlint·prettier 통과 / 통과 |
| Frontend build | `pnpm build` | 통과, 초기 JS 115.09 kB / 예산 122.55 kB, `tileFill.worker` 1.51 kB(gzip) |
| Frontend E2E | `pnpm test:e2e`(포트 4173 비어 있음, 새 build·preview) | 410 통과·실패 0·건너뜀 0·flaky 0, 7.9분 |
| 문서 | `pnpm docs:check`, `git diff --check` | 통과(500행 초과 경고는 기존 계획 문서와 이 계획) |

유료 smoke와 실제 AI 공급자는 호출하지 않았다.

계약 리뷰(`api-breaking-change-detector`, 읽기 전용): `SpriteContracts.cs`의 `SpriteAssetPlanRequest.Placements`(요청과 응답 공용, 선택·nullable)와 클라이언트 `types.ts`의 `placements: Bounds[] | null`·`spriteApi.ts` 직렬화를 양방향으로 대조했다. 확인된 결함은 없다. 서버는 응답에 `placements`를 항상 쓰고(`null` 또는 배열) reader는 키 누락을 null로 읽어 변경 전 서버 응답도 받는다. 클라이언트는 항상 키를 보내고 서버는 생략·`null`을 같은 fingerprint로 본다. 도메인 범위 규칙(`InsideSource`: 유한·양수 크기·원본 안)은 클라이언트 `inside`와 같고 1~20개·오브젝트 한정은 서버만 판정하며 위반은 400 `SPRITE_PLAN_INVALID`다. 클라이언트는 이 코드로 분기하지 않고 서버 메시지를 표시한다. 정적 탐색으로 본 위험은 둘이다. 첫째, 배포 순서(변경 전 서버는 `Disallow`라 새 SWA가 먼저 나가면 계획 저장이 바인딩 오류 400, 아래 롤백·배포 주의). 둘째, `placements` 키를 모르는 이전 클라이언트가 저장하면 오브젝트 자리가 null로 돌아간다(자리 전용 저장이라 재생성은 없다).

연결 검증: 서버가 실제 `JobResponse.From`·`SpriteResponse.From`과 `ApiJson.Configure`로 만든 `sprite-roles.server.json`(`SpriteApiTests.ServerRolesFixture_MatchesActualResponseMapperAndJsonOptions`, `b3e5b79`에서 재생성, 이번 전체 회귀에서 `516a39d` 기준 일치 확인)을 `spriteContract.test.ts`가 실제 `getJob` → `apiRequest` → reader로 읽는다(20개 통과, 포함 항목: object 자리 배열 2개·backdrop·tile·구형 job의 `placements: null`, 키 생략 시 null, 잘못된 값 거부, `updateSpritePlan`이 요청 fixture와 같은 본문을 쓰고 `placements: null`을 명시). 서버 쪽은 `ServerRolesFixture_*`와 `JsonBinding_*` 필터 32개가 통과했고(요청 fixture 바인딩·null/비유한 원소 400·생략/null 재생·값 재전송 충돌) 요청 경로를 덮는다.

화면 관찰(스크래치 `t9/*.png`, 저장소 밖): Fake E2E(`completed`, isometric 타일) 1440px·390px, light·dark 4조합에서 재구성 50%·100%, 자리 선택·추가, 오브젝트 카드를 확인했다. 가로 넘침 없음(`scrollWidth` 1440/390), 자리 버튼·`자리 추가`·`선택한 자리 삭제`는 390px에서 줄바꿈되어 겹치지 않고, 선택한 자리에만 손잡이가 보이며 점선/실선 구분이 light·dark 모두 읽힌다. 선택하지 않은 상자·칩은 불투명도 0.32로 흐려져 대비가 낮고 같은 앵커의 칩이 겹쳐 쌓인다(둘 다 기존 오버레이 동작). 390px 지도는 작아 자리 손잡이가 서로 닿는다. 로컬 실제 작업 `300f237d`는 읽기 전용으로 관찰했다(비 GET 요청 0건, 클릭·입력 없이 페이지만 열었고 작업 상세 폴링 GET만 발생). 변경 전 Docker API라 응답에 `placements` 키가 없고 reader가 null로 읽었으며 타일 8종 413칸이 표시됐고(계산 중 표시→층 571~946ms, dev), 자리 UI는 타일 대상에 나오지 않았고 ROI 박스 8개, 가로 넘침 없음이었다.

Azure(정의 확인 vs 미확인): `deploy/azure/main.bicep`의 API는 `siteConfig.cors.allowedOrigins`에 SWA 기본 호스트명만 넣고 `supportCredentials: false`이며 `app.UseCors`는 Development에서만 돌기 때문에 플랫폼 CORS에 의존한다는 주석이 있다(정의상 확인). 이미지 응답(`SpriteJobsController.DownloadAsync`)은 `Cache-Control: private, max-age=31536000, immutable`이고 `Vary`를 쓰지 않는다. 플랫폼 CORS가 이미지 응답에도 `Access-Control-Allow-Origin`·`Vary: Origin`을 붙이는지, 커스텀 도메인 사용 시 허용 목록이 맞는지는 실행하지 않았으므로 미확인이다. 재구성은 `mode: 'cors'`·`cache: 'reload'`로 읽으므로 `Vary` 부재가 캐시 오염을 일으키지는 않지만 ACAO 부재면 픽셀 읽기가 실패하고 "이미지를 불러오지 못했습니다"로 드러난다.

미확인: 실제 모델의 V5 분석 품질(자리 정확도·벽 분류·1024px 분할), OpenAI strict 모드의 중첩 `anyOf`·`minItems`/`maxItems`와 Gemini `type: null` 수락, 배포 DB(Azure·운영 SQL Server)에서의 V5 migration 적용(격리 SQL 컨테이너 테스트는 통과), Azure 운영 CORS, Docker 재배포(하지 않음, 로컬 API는 변경 전 이미지), 실제 Backend와 자리 저장 end-to-end.

롤백·배포 주의: 변경 후 API가 새로 쓰는 `PlanJson`·`SpriteInputJson`에는 `"placements":null`이 들어간다. 변경 전 이미지는 `UnmappedMemberHandling.Disallow`라 이 행을 읽지 못한다. API 이미지를 되돌리려면 새 행의 `placements` 키를 먼저 처리해야 하고, 배포는 API → SWA 순서로 한다. (최종 수정에서 null 키 생략으로 바꿨다. 현재 절차는 아래 [롤백 절차](#롤백-절차))

### 2026-10-18 최종 수정

전체 브랜치 리뷰(기준 `7f2b44e`) 뒤 수정이다.

#### 롤백 절차

이 절차는 문서로만 정했고 실제 DB·배포에서 실행하지 않았다. 이미지 rollback은 DB migration을 되돌리지 않는다([배포 지침](../../../deploy/AGENTS.md)).

전제: 이 수정 이후 이미지는 `Placements`가 null이면 `SpriteAssets.PlanJson`·`Tasks.SpriteInputJson`(`value.plan`)에 `placements` 키를 쓰지 않으므로 자리 없는 행은 변경 전 이미지도 읽는다. 값이 있는 `placements`(V5 분석의 오브젝트 자리·사용자가 저장한 자리)는 변경 전 이미지가 `UnmappedMemberHandling.Disallow`로 읽지 못해 그 작업 조회·처리가 JSON 오류로 실패한다. V5 프롬프트가 활성인 채 이미지를 되돌리면 변경 전 `SpritePlanParser`가 V5 응답(필수 `placements`)을 모두 거부해 분석이 유료 재호출 끝에 실패한다.

1. 변경 후 이미지가 떠 있는 동안 2D 분석 프롬프트를 V4로 되돌린다. 경로는 기존 프롬프트 활성화 API `POST /api/prompts/versions/c7000000-0000-4000-8000-000000000004/activate`(`PromptsController.ActivateAsync` → `ActivatePromptVersionHandler`, 같은 단계·카테고리의 활성 V5를 내리고 V4를 켬)이며, 화면은 `/admin/prompts` 격자의 2D 배경 분석(`AnalyzeSprites`) × Background 칸 → 버전 이력의 V4 행 `이 버전 켜기`다. `GET /api/prompts/AnalyzeSprites/versions?category=Background`로 V4 ID가 활성인지 확인한다. 분석은 실행 시점의 활성 프롬프트를 읽으므로(`RunSpriteAnalysisTaskHandler`) 이후 호출은 V4를 쓴다. V4가 수정돼 있어 V5가 추가되지 않았거나 운영자가 다른 버전을 켰다면 이 단계는 필요 없다. V5 migration Down은 쓰지 않는다. 저장소에 `dotnet-ef` 도구 manifest·migration bundle이 없고, Down은 V5 행을 지워 그 행을 가리키는 `LlmCalls.PromptVersionId`(FK 없음)의 프롬프트 원문을 잃는다.
2. 값이 있는 자리 행을 센다.

   ```sql
   SELECT COUNT(*) FROM SpriteAssets WHERE PlanJson LIKE N'%"placements"%';
   SELECT COUNT(*) FROM Tasks WHERE SpriteInputJson LIKE N'%"placements"%';
   ```

3. 둘 다 0이면 이미지를 되돌린다. Frontend도 되돌린다면 SWA를 먼저 되돌린다(새 SWA는 `placements` 키를 항상 보내 변경 전 API에서 계획 저장이 400).
4. 0이 아니면 기본은 roll-forward(변경 후 이미지 유지)다. 변경 전 이미지가 꼭 필요하면 DB 백업과 사용자 승인 뒤 명시적 정리를 한다. 정리는 저장된 자리를 버리고(오브젝트는 `sourceBounds` 한 자리로 돌아간다) 키만 지운 뒤 3단계로 간다.

   ```sql
   UPDATE SpriteAssets SET PlanJson = JSON_MODIFY(PlanJson, '$.value.placements', NULL)
   WHERE PlanJson LIKE N'%"placements"%';
   UPDATE Tasks SET SpriteInputJson = JSON_MODIFY(SpriteInputJson, '$.value.plan.placements', NULL)
   WHERE SpriteInputJson LIKE N'%"placements"%';
   ```

   lax 모드 `JSON_MODIFY`의 NULL은 키를 지운다. 일회용 SQL Server 2022 컨테이너(`mcr.microsoft.com/mssql/server:2022-latest`, 로컬 `deploy/k8s/mssql.yaml`과 같은 이미지)에서 자리 배열·명시적 null·키 없음 행과 NULL 열로 확인했고 실제 DB에는 실행하지 않았다.

#### 수정 내용

각 동작 변경은 테스트를 먼저 쓰고 수정 전 코드에서 기대한 이유로 실패하는지 확인했다. 이미 구현된 동작을 고정하는 테스트(5·6)는 구현을 잠시 바꾼 변이에서 실패를 확인한 뒤 원복했다.

| 항목 | 커밋 | 변경 | 검증(적색 → 녹색) |
| --- | --- | --- | --- |
| 1a 롤백 호환 | `17877c4` | `SpriteJsonSerializer`에 resolver modifier를 두어 `SpriteAssetPlan.Placements`가 null이면 `placements` 키를 생략한다(전역 null 생략 아님, `PlanJson`·`SpriteInputJson`의 `value.plan` 모두) | `SpritePersistenceTests.PlanJson_OmitsNullPlacementsAndRoundTripsBoth`(기존 `PlanJson_WithoutPlacements_ReadsNullAndRoundTrips` 대체): null은 키 없이 저장·왕복, 값은 저장·왕복, `SpriteFrameInput` 직렬화에도 키 없음, 명시적 `null` 행 읽기. 수정 전 `ContainsKey("placements")`가 참이라 실패, 수정 후 SpritePersistence·SpriteCommand 83개 통과. `RoleGridCanvas_RoundTripAndLegacyNullColumns`(구형 role 없는 행) 통과 |
| 1b 롤백 절차 | `17877c4` | 위 [롤백 절차](#롤백-절차), `docs/xHuman/backend.md`는 링크만 | 문서. 정리 SQL만 일회용 컨테이너에서 확인 |
| 2 재시도 | `0aac583` | `useTileFill`이 `retryable`·`retry()`를 반환하고 시도 번호를 계산 key에 더한다. `SpritePlanReview`는 표시 실패나 `retryable` 오류에 `재구성 다시 계산` 버튼을 보이고 누르면 표시 실패 목록을 비우고 다시 계산한다(draft 유지). 칸 상한·시간 초과는 버튼 없이 기존 안내 | E2E `retrying a failed reconstruction keeps unsaved placement edits`(계산의 첫 이미지 fetch만 500 → 알림·버튼 → `자리 1 ROI x 3`을 0.4로 편집 → 다시 계산 → 재구성 표시·알림과 버튼 사라짐·편집값 유지·저장 활성·그려진 자리 x 128). 수정 전 버튼이 없어 실패. 칸 상한·시간 초과 기존 E2E에 버튼 없음 단언 추가 |
| 3 타일 표시 실패 | `0aac583` | 타일 `<image>`의 `onError`를 같은 표시 실패 경로로 보낸다. 실패한 타일이 놓인 칸마다 투명도 묶음 밖 점선 윤곽(`polygon[data-failed-tile]`, isometric은 마름모, 빠진 에셋 윤곽과 같은 선) | E2E `failed tile images outline their cells and offer a retry`(계산 fetch는 성공, `resourceType() === 'image'` 표시 요청만 500). 수정 전 알림이 없어 실패 |
| 4 칩 가로채기 | `0aac583` | `PartsOverlay`는 `selection`이 있을 때만 칩 묶음에 inline `zIndex: 2 * placed.length + 3`(상자 최대값 이상, 손잡이 `+4` 아래, 동률은 DOM 순서상 칩이 위). 3D 호출부는 `selection`이 없어 그대로 | E2E `a chip lying over a smaller box selects its own target`(응답에서 오브젝트 ROI를 원본 중앙으로 옮겨 원경 배경 칩이 오브젝트 상자 위에 놓임). 수정 전 칩 클릭이 오브젝트 상자에 가로채져 `aria-pressed` false |
| 5 `fetchImageBlob` | `457af83` | `client.test.ts`에 `mode: 'cors'`·`cache: 'reload'` 호출과 `!ok` 던짐 테스트 2개 | 구현에서 옵션과 `!ok` 검사를 뺀 변이에서 2개 실패, 원복 후 10/10 |
| 6 아래쪽 y 순서 | `0aac583` | 기존 E2E를 `objects are drawn at each stored placement in ascending bottom y`로 바꿔 GET 응답의 자리를 역순으로 넣는다 | 정렬을 뺀 변이에서 실패(x 160 기대, 80), 원복 후 통과 |
| 7 문구 | `0aac583` | `tileFill.worker.ts` 주석과 위 P1 정정(내장 Error는 structured clone으로 전달, 명시적 형식이 이유). `worker.onerror`와 Worker 생성·전송 `catch`는 브라우저 원문 대신 `재구성 계산을 시작하지 못했습니다` | E2E `a worker that fails to start shows a fixed reason and a retry`(Worker 스크립트가 시작 시 throw). `event.message`를 쓰던 변이에서 원문 오류가 보여 실패 |
| 8 `Equals` 주석 | `17877c4` | `SpriteAssetPlan.Equals` 위에 `// 새 필드 추가 시 Equals·GetHashCode 동시 갱신` | 해당 없음 |

이로써 위 "남은 minor"의 T2 재시도·타일 `onError`·`worker.onerror` 원문·worker 주석, `fetchImageBlob` 단위 테스트, 같은 오브젝트 아래쪽 y 정렬 테스트, T4 `Equals` 주석과 `"placements":null` 롤백 문제, T7 칩 가로채기를 해소했다. 나머지 minor는 그대로 이월한다.

#### 검증 (기준 `0aac583` + 이 문서)

| 검사 | 명령 | 결과 |
| --- | --- | --- |
| Backend focused | `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpritePersistenceTests\|FullyQualifiedName~SpriteCommandTests'` | 통과 83·실패 0·건너뜀 0 |
| Backend 빌드 | `dotnet build apps/backend/Noxtend.slnx` | 오류 0, 경고 8(NU1903·CS0618·xUnit2031, 기존) |
| Backend 전체 | `dotnet test apps/backend/Noxtend.slnx --no-build --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`(위 빌드 직후, Docker 사용) | 통과 2063·실패 0·건너뜀 0, 10분 51초(테스트 하나는 이름을 바꿔 대체해 개수 같음) |
| Frontend | `pnpm test` / `pnpm lint` / `pnpm typecheck` | 61 파일 574 통과 / eslint·oxlint·prettier 통과 / 통과 |
| Frontend build | `pnpm build` | 통과, 초기 JS 115.09 kB / 예산 122.55 kB, `tileFill.worker` 1.51 kB(gzip) |
| Frontend E2E | `pnpm test:e2e`(포트 4173 비어 있음, 새 build·preview, Backend 회귀 종료 뒤 단독 실행) | 414 통과·실패 0·건너뜀 0·flaky 0, 8.1분(신규 4·변경 1) |
| 문서 | `pnpm docs:check`, `git diff --check` | 통과(500행 초과 경고는 기존 계획 문서와 이 계획) |

유료 smoke·실제 AI 공급자·실제 DB·Docker 재배포·push·merge는 하지 않았다.

#### 사용자 결정 필요

- 큰 칸·4096² 원본은 4,096칸 상한 안에서도 스펙의 8px 오프셋 탐색 때문에 5초 시간 초과가 난다(위 측정). 선택지: coarse-to-fine 탐색, 칸 크기에 비례한 탐색 간격, isometric 오프셋 중복 제거(약 2배).
- V5는 수정되지 않은 V4 시드가 있는 모든 환경에서 배포 즉시 자동 활성화되며, 실제 모델 평가와 OpenAI·Gemini의 V5 schema 수락은 확인 전이다. 선택지: 승인을 받아 배포 전 평가, 또는 배포 후 확인 절차 문서화(되돌리기는 위 롤백 절차 1단계).
- V5 파싱이 엄격하다. 모델이 타일·원경 배경에 자리를 주거나 `[]`를 주면 분석 전체가 무효가 되어 유료 재시도로 간다. 선택지: 오브젝트가 아닌 자리 삭제·`[]`→null 관대한 정규화(스펙 변경 필요).
