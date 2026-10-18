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

구현을 시작하면 작업별 커밋, 리뷰 결과, 검증 명령과 결과, 미해결 사항을 여기에 남긴다.
