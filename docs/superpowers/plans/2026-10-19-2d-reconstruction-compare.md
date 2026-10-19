# 2D 재구성 슬라이드 비교·타일 캔버스 맞춤 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 계획 검수의 재구성을 비교/편집 전환형 슬라이드 비교로 바꾸고, isometric 타일이 캔버스 다이아몬드를 빈틈없이 채우도록 정규화하며, V5 분석을 실제 모델로 한 번 확인한다.

**Architecture:** Backend는 `SkiaImageTranscoder.NormalizeSpriteAsync`의 Diamond 경로에만 "알파 128 이상 경계 상자 → 캔버스 전체" 맞춤을 더한다. Frontend는 `SpritePlanReview`에 화면 상태(모드·경계)를 두고, 새 `SpriteCompareDivider`를 `PartsOverlay`의 `children`으로 얹으며, 재구성 층은 바깥 래퍼의 `clip-path`로 자른다. HTTP 계약·저장 형식·migration은 바뀌지 않는다.

**Tech Stack:** .NET 10·SkiaSharp·xUnit / React·TypeScript·Playwright

**Spec:** [2026-10-19-2d-reconstruction-compare-design.md](../specs/2026-10-19-2d-reconstruction-compare-design.md)

**실행 방식:** Subagent-driven(루트 `AGENTS.md` 기본값). Task 1과 Task 2는 서로 파일이 겹치지 않아 순서와 무관하다. Task 3은 사용자 승인 뒤에만, Task 4는 1·2 리뷰 뒤에 한다.

## Global Constraints

- 맞춤 대상은 `SpriteTileLayout.Diamond`뿐이다. Square·오브젝트·원경 배경·레이어 결과는 바뀌면 안 된다.
- 알파 기준은 128이다(Frontend `MIN_ALPHA`와 같음).
- 거부 판정은 맞춤 전 현재 규칙이다. `DiamondMask_RejectsContentEntirelyOutsideCell`은 그대로 통과해야 한다.
- 처음 모드는 비교, 처음 경계는 50%다. 모드·경계는 저장하지 않는다(서버·`localStorage` 모두).
- 경계 접근 가능한 이름은 `원본·재구성 경계`, 모드 묶음 이름은 `재구성 보기`, 버튼은 `비교`·`편집`이다.
- `재구성 투명도` range와 `opacity` 상태·prop은 남기지 않는다.
- 실제 AI 호출, 기존 작업 재생성, Docker 재배포, push·PR은 사용자 승인 범위에서만 한다.
- 지도(`docs/xHuman/frontend.md`, `docs/xHuman/providers-and-prompts.md`)는 해당 코드와 같은 커밋에서 고친다.

## Review Focus

- 비교 모드에서 끌기가 시작될 때 진행 중이던 ROI 끌기가 남아 늦게 확정되는 경우 → 모드 전환 즉시 취소되어야 한다(Task 2 Step 1 `entering compare cancels a box drag`).
- 재구성 계산이 다시 시작되는 동안(격자 칸 너비 변경) 모드 버튼이 사라졌다가 돌아올 때 → 사용자가 고른 모드로 복원되어야 한다(Task 2 Step 1 `recomputing keeps the chosen mode`).
- 알파 1~127만 있는 흐린 그림 → 거부도 맞춤도 하지 않고 지금 결과를 유지해야 한다(Task 1 `DiamondFit_KeepsFaintOnlyContentUnfitted`).
- 390px에서 경계 위 세로 스크롤 시작(`pointercancel`) → 경계가 원래 자리로 돌아와야 한다(Task 2 Step 1 `touch scroll restores the divider`).
- 계산 실패(이미지 500·Worker 실패)일 때 → 모드 버튼 없이 상자 편집이 그대로 되어야 한다(Task 2 Step 1 기존 실패 테스트에서 `편집` 클릭 없이 상자 조작 유지).

---

## Task 1: isometric 타일 캔버스 맞춤 (Backend)

**Files:**
- Modify: `apps/backend/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.Sprites.cs` (`NormalizeSpriteAsync`, 새 private helper)
- Test: `apps/backend/Noxtend.Tests/Infrastructure/SpritePixelTests.cs`
- Docs: `docs/xHuman/providers-and-prompts.md` (`NormalizeSpriteAsync` 문단과 그 문서의 확인 기준)

**Interfaces:**
- Consumes: 없음
- Produces: 공개 시그니처 변화 없음. 새 `private static SKRectI? OpaqueBounds(SKBitmap bitmap, CancellationToken ct)`(알파 ≥ 128 픽셀의 경계 상자, 없으면 null).

- [ ] **Step 1: 실패하는 테스트 작성** — 기존 `Image(...)` helper 사용. 공통 입력은 16×8, 캔버스 `new(16, 8)`, 변환 `new(1, 0, 0)`, `requireTransparency: true`, 안쪽 사각형은 `4 <= x < 12 && 2 <= y < 6`.

```csharp
[Fact] public async Task DiamondFit_StretchesInsetContentToCellEdges()
// 안쪽만 Red, 나머지 Transparent, Diamond
// 결과: (8,0) (1,4) (14,3) (8,7) 의 Alpha == 255, (0,0) (15,0) (0,7) (15,7) 의 Alpha == 0

[Fact] public async Task DiamondFit_IgnoresFaintHalo()
// 안쪽 Red, 나머지 new SKColor(0, 0, 255, 100), Diamond
// 결과: (8,0) == SKColors.Red   (흐린 테두리가 상자를 넓히면 파란 반투명이 남는다)

[Fact] public async Task DiamondFit_KeepsFaintOnlyContentUnfitted()
// 안쪽만 new SKColor(0, 0, 255, 100), 나머지 Transparent, Diamond — 거부되지 않는다
// 결과: (8,0).Alpha == 0, (8,4).Alpha == 100

[Fact] public async Task SquareLayout_DoesNotFitInsetContent()
// 안쪽만 Red, 나머지 Transparent, Square
// 결과: (1,4).Alpha == 0, (5,3) == SKColors.Red
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpritePixelTests'`
Expected: `DiamondFit_StretchesInsetContentToCellEdges`·`DiamondFit_IgnoresFaintHalo` FAIL(`(8,0)` 알파 0), 나머지는 PASS.

- [ ] **Step 3: 구현** — `NormalizeSpriteAsync`에서 기존 그리기와 `SpriteAlpha(output).HasVisiblePixels` 거부 검사를 그대로 둔다. 검사를 통과하고 `layout == Diamond`이며 `OpaqueBounds(oriented ?? decoded)`가 null이 아니고 원본 전체 사각형과 다를 때만, `output`을 투명으로 지우고 같은 다이아몬드 clip 아래에서 그 상자를 `SKRect(0, 0, canvas.Width, canvas.Height)`로 다시 그린다. 샘플링은 기존 `SKFilterMode.Nearest`·`SKMipmapMode.None`을 쓴다. 다이아몬드 경로·clip 코드는 두 번 쓰지 않게 지역 함수로 묶는다. `OpaqueBounds`는 `SpriteAlpha`처럼 행마다 `ct.ThrowIfCancellationRequested()`를 부른다.

- [ ] **Step 4: 통과 확인**

Run: `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpritePixelTests|FullyQualifiedName~SpriteGenerationTests'`
Expected: 전부 PASS(기존 `DiamondCell_HasTransparentCornersAndVisibleCenter`·`DiamondMask_RejectsContentEntirelyOutsideCell` 포함).

- [ ] **Step 5: 지도 갱신** — `providers-and-prompts.md`의 "프레임별 콘텐츠 경계를 자르거나 중심을 다시 계산하지 않는다"를 Diamond 예외(알파 128 이상 경계 상자를 캔버스 전체로 늘림, 거부 판정은 맞춤 전, 루프 프레임도 이미지마다)로 고치고, 검증 테스트 이름과 확인 기준(날짜·revision·범위)을 갱신한다.

- [ ] **Step 6: Backend 전체 회귀**

Run: `dotnet build apps/backend/Noxtend.slnx && dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`
Expected: 실패 0. Docker가 없어 건너뛴 테스트는 통과로 세지 않고 수를 기록한다.

- [ ] **Step 7: 커밋**

```bash
git add apps/backend/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.Sprites.cs apps/backend/Noxtend.Tests/Infrastructure/SpritePixelTests.cs docs/xHuman/providers-and-prompts.md
git commit -m "fix(sprites): stretch isometric tile content to fill the diamond cell"
```

## Task 2: 비교/편집 전환 (Frontend)

**Files:**
- Create: `apps/frontend/src/features/screens/sprites/SpriteCompareDivider.tsx`
- Modify: `apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx`, `apps/frontend/src/features/screens/sprites/SpriteReconstructionLayer.tsx`, `apps/frontend/src/features/screens/background/PartsOverlay.tsx`
- Test: `apps/frontend/tests/e2e/sprites-reconstruction.spec.ts`, `apps/frontend/tests/e2e/spriteFakeApi.ts`(helper), 계획 승인 뒤 상자를 다루는 기존 spec(`sprites-roi.spec.ts`, `sprites-placements.spec.ts` 등 Step 4에서 실패하는 것)
- Docs: `docs/xHuman/frontend.md` (`SpritePlanReview.tsx` 재구성 문단과 확인 기준)

**Interfaces:**
- Consumes: 없음(Task 1과 독립)
- Produces:
  - `PartsOverlay`의 선택 prop `hideBoxes?: boolean` — true면 프레임 안 상자와 `chipLayer`의 이름표·연결선을 그리지 않는다. `overlay-empty` 안내 조건(`placed.length === 0`)은 그대로다. 3D 화면은 이 prop을 넘기지 않는다.
  - `SpriteReconstructionLayer`에서 `opacity` prop 삭제. `<g data-testid="reconstruction-images">`는 `opacity` 속성 없이 남는다.
  - `SpriteCompareDivider({ position, onChange, interactive }: { position: number; onChange: (position: number) => void; interactive: boolean })` — `position`은 왼쪽 기준 0~100(%).
  - E2E helper `editBoxes(review: Locator): Promise<void>` (`spriteFakeApi.ts`) — `review.getByRole('group', { name: '재구성 보기' }).getByRole('button', { name: '편집' })`를 누르고 `aria-pressed="true"`를 기다린다.

- [ ] **Step 1: 실패하는 E2E 작성** (`sprites-reconstruction.spec.ts`, 시드 `baseReview`·isometric tiles 설정 재사용)
  - `plan review has no reconstruction before plan approval`: `재구성 투명도` 대신 `group '재구성 보기'`와 `slider '원본·재구성 경계'`가 0개.
  - `reconstruction follows opacity after plan approval`을 `reconstruction compares by dragging the divider`로 바꾼다. 기존 viewBox·타일 width 320·`image[data-asset-id]` 3개 단정은 유지한다. 추가 단정:
    - `비교` 버튼 `aria-pressed="true"`, `overlay-box` 0개.
    - 경계 slider `aria-valuenow="50"`, `aria-valuetext="50%"`.
    - `getByTestId('reconstruction-clip')`이 `toHaveCSS('clip-path', 'inset(0px 0px 0px 50%)')`.
    - 이미지 25% 지점에서 `mouse.down` → 10% 지점으로 `mouse.move` → `mouse.up` 하면 `aria-valuenow="10"`이고 clip이 `inset(0px 0px 0px 10%)`.
    - slider에 focus 후 `ArrowRight`를 누르면 `aria-valuenow="11"`·`aria-valuetext="11%"`.
  - `edit mode keeps the divider and edits boxes`: 경계를 30으로 옮긴 뒤 `editBoxes` → `overlay-box`가 보이고, slider 0개, `compare-line`이 보이며, clip은 30% 그대로. 이미지 10%·10% 지점을 누르면 `overlay-handle` 8개(기존 단정 이동).
  - `selecting a box from a card switches to edit mode`: 비교 상태에서 `생성 기준 선택 2`(오브젝트 카드)를 누르면 `편집` 버튼 `aria-pressed="true"`이고 `overlay-handle` 8개.
  - `entering compare cancels a box drag`: `editBoxes` → 오브젝트 상자를 `mouse.down`·`mouse.move`로 끄는 중에 `비교` 버튼에 키보드 포커스 후 `Enter` → `mouse.up` 뒤 `editBoxes`로 돌아가면 상자 좌표가 끌기 전과 같다(저장 요청도 없음).
  - `recomputing keeps the chosen mode`: `editBoxes` 뒤 격자 칸 너비 입력을 바꾸고 계산이 끝나면 `편집`이 여전히 `aria-pressed="true"`.
  - `touch scroll restores the divider`: viewport 390×844. `compare-surface`가 `toHaveCSS('touch-action', 'pan-y')`. surface에 `pointerdown`(clientX = 이미지 20%, `isPrimary: true`, `button: 0`, `pointerId: 1`, `bubbles: true` — React 루트 위임까지 올라가야 한다)을 dispatch하면 `aria-valuenow="20"`, 이어 document에 `pointercancel`(`pointerId: 1`)을 dispatch하면 `aria-valuenow="50"`. `document.documentElement.scrollWidth <= 390`.
  - `missing base images show dashed outlines regardless of opacity`를 `missing base images stay outlined in compare mode`로 바꾼다. 투명도 조작 단정을 지우고, 비교 모드 기본 상태에서 `rect[data-missing]`이 보이는지 단정한다.
  - 계획 승인 뒤 상자를 누르거나 끄는 기존 테스트(`saving only placements…`, `retrying a failed reconstruction keeps unsaved placement edits` 등)는 계산이 끝난 뒤 `editBoxes(review)`를 먼저 부른다. 계산이 실패하는 테스트(이미지 500·Worker 실패·표시 실패)는 모드 버튼이 없음을 단정하고 `editBoxes` 없이 상자 조작이 되는지 그대로 둔다.

- [ ] **Step 2: 실패 확인**

Run: `pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-reconstruction.spec.ts`
Expected: 새·바뀐 테스트가 `재구성 보기`·`원본·재구성 경계`·`reconstruction-clip`을 찾지 못해 FAIL.

- [ ] **Step 3: 구현**
  - `PartsOverlay.tsx`: `hideBoxes` prop을 위 Interfaces대로 추가한다.
  - `SpriteReconstructionLayer.tsx`: `opacity` prop과 `<g>`의 `opacity` 속성을 지운다. 누락·실패 윤곽 코드는 그대로 둔다.
  - `SpriteCompareDivider.tsx`: 프레임 전체를 덮는 `absolute inset-0` 층이다. 공통으로 경계선(`data-testid="compare-line"`, `left: position%`)과 아래쪽 "원본"(왼쪽)·"재구성"(오른쪽) 표시를 그린다. 표시는 `pointer-events-none`·`aria-hidden`이다.
    - `interactive`일 때: 끌기 면(`data-testid="compare-surface"`, `touch-pan-y`, `cursor-ew-resize`)을 둔다. 주 버튼 `pointerdown`에서 시작 위치를 기억하고, 누른 x로 `onChange`한다. 포인터 캡처 대신 `useSpriteRoiEditor`처럼 document에 `pointermove`(같은 `pointerId`면 `onChange`)·`pointerup`(정리)·`pointercancel`(시작 위치로 `onChange` 후 정리)을 붙이고, 언마운트 때 떼어낸다. 위치는 끌기 면의 `getBoundingClientRect()` 기준이고, 0~100으로 자른다.
    - 손잡이: `role="slider"`·`aria-label="원본·재구성 경계"`·`aria-valuemin={0}`·`aria-valuemax={100}`·`aria-valuenow={Math.round(position)}`·`aria-valuetext={`${Math.round(position)}%`}`·`tabIndex={0}`. `ArrowLeft`/`ArrowRight`는 ±1, 0~100으로 자르며 `preventDefault`한다. 포커스 표시는 기존 `focus-visible:outline-primary` 패턴을 쓴다.
    - `interactive`가 아닐 때: 경계선과 표시만 그리고 `pointer-events-none`이다. slider 역할은 없다.
  - `SpritePlanReview.tsx`:
    - `opacity` 상태와 range를 지운다. `mode`(`useState<'compare' | 'edit'>('compare')`)와 `divider`(`useState(50)`)를 둔다.
    - `ready = reconstructing && tileFill.status === 'ready'`, `comparing = ready && mode === 'compare'`로 둔다. `ready`일 때만 기존 range 자리에 `role="group" aria-label="재구성 보기"` 묶음을 그리고, 그 안에 `Button variant="outline" size="sm"`과 기존 `PRESSED` 클래스, `aria-pressed`로 `비교`·`편집`을 둔다.
    - `useSpriteRoiEditor`의 `disabled`는 `busy || comparing`이다. 모드 전환 때 기존 `useLayoutEffect`가 진행 중 끌기를 취소한다.
    - `PartsOverlay`에 `hideBoxes={comparing}`와 `children={ready ? <SpriteCompareDivider position={divider} onChange={setDivider} interactive={comparing} /> : null}`를 넘긴다. `underlay`의 `SpriteReconstructionLayer`는 `<div data-testid="reconstruction-clip" className="h-full w-full" style={{ clipPath: `inset(0 0 0 ${divider}%)` }}>`로 감싼다.
    - 카드의 `대상 선택`·`생성 기준 선택`·`자리 N 선택` 버튼은 `setMode('edit')` 뒤 `roi.select(...)`를 부른다. `자리 추가`·`선택한 자리 삭제`는 모드를 바꾸지 않는다.
    - 상자 사용법 안내(`영역이나 이름을 선택하고…`)는 `comparing`일 때 `이미지를 좌우로 끌어 원본과 재구성을 비교하세요. 영역을 옮기려면 편집을 누르세요.`로 바꿔 보인다.

- [ ] **Step 4: 통과 확인과 기존 spec 보정**

Run: `pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-reconstruction.spec.ts`
Expected: PASS.
Run: `pnpm --filter @nextend/frontend test:e2e tests/e2e/sprites-roi.spec.ts tests/e2e/sprites-placements.spec.ts tests/e2e/sprites-roles.spec.ts tests/e2e/sprites-static.spec.ts tests/e2e/sprites-animation.spec.ts tests/e2e/home-active-job.spec.ts tests/e2e/parts-overlay.spec.ts`
Expected: 계획 승인 뒤 상자를 다루다 실패한 테스트에만 `editBoxes(review)`를 더해 PASS. 단정을 지우거나 약하게 바꾸지 않는다. `parts-overlay.spec.ts`(3D)는 수정 없이 PASS해야 한다.

- [ ] **Step 5: 지도 갱신** — `frontend.md`의 `재구성 투명도` 서술을 비교/편집 전환(처음 비교·50%, `hideBoxes`, `SpriteCompareDivider`, `reconstruction-clip`, 카드 선택 시 편집 전환, ROI 편집기 `disabled`로 끌기 취소, 누락·실패 윤곽은 재구성 쪽)으로 고치고 확인 기준을 갱신한다.

- [ ] **Step 6: Frontend 전체 회귀**

Run: `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`
Expected: 모두 통과. E2E는 포트 4173이 비어 있는지 먼저 확인하고, 실패·건너뜀·flaky 수를 기록한다.

- [ ] **Step 7: 커밋**

```bash
git add apps/frontend/src/features/screens/sprites/SpriteCompareDivider.tsx apps/frontend/src/features/screens/sprites/SpritePlanReview.tsx apps/frontend/src/features/screens/sprites/SpriteReconstructionLayer.tsx apps/frontend/src/features/screens/background/PartsOverlay.tsx apps/frontend/tests/e2e/ docs/xHuman/frontend.md
git commit -m "feat(frontend): compare source and reconstruction with a draggable divider"
```

## Task 3: V5 실제 분석 확인 (유료, 사용자 승인 후)

**Files:** 이 계획의 실행 기록만 고친다.

- [ ] **Step 1: 승인 요청** — 사용자에게 "로컬 Docker API, 업로드 `bdd66320-f5f9-49e9-a952-9f02a14458e6`, 텍스트 모델 `gpt-5.6-luna`(ChatGPT), 분석 1회와 자동 재시도까지, 이미지 생성 없음"을 알리고 명시적 승인을 받는다. 승인 전에는 아래 단계를 하지 않는다.
- [ ] **Step 2: 같은 설정 읽기** — `GET http://localhost:18080/api/jobs/a9f77a56-3dfc-40f3-819d-62e40b5feaa1`에서 분석 task의 `providerConfigId`·`model`과 작업의 이미지 공급자·모델, `settings`(isometric·tiles·128·both)를 읽는다. 로컬 DB의 AnalyzeSprites/Background 활성 버전이 V5(`c7000000-0000-4000-8000-000000000005`)인지 확인한다.
- [ ] **Step 3: 작업 시작** — `POST http://localhost:18080/api/jobs/sprites`(`StartSpriteJobRequest`: 새 `requestId`, `uploadId`, Step 2의 공급자·모델·설정). 분석이 끝나 `phase`가 `planReview`가 될 때까지 조회만 한다. 계획을 승인하지 않는다(이미지 생성 방지).
- [ ] **Step 4: 결과 확인** — 응답의 에셋마다 `name`·`role`·`placements` 개수를 표로 만들고 다음을 기록한다.
  - 파싱 성공 여부와 재시도 횟수
  - 담벼락·담장·외벽 계열이 `object`인지
  - 여러 번 나오는 건물·나무·가로수의 `placements`가 2개 이상인지
  - `placements`가 오브젝트에만 있는지
  - 호출 내역에서 확인한 토큰·비용(확인할 수 없으면 미확인)
  - 화면 주소를 사용자에게 알려 의미 품질을 판단받는다.
- [ ] **Step 5: 기록** — 이 문서 실행 기록에 [프롬프트 평가 절차](../../xHuman/prompt-evaluation.md) 형식으로 남긴다. `Verdict` 기록·프롬프트 활성화는 하지 않는다. 부족한 점이 있으면 프롬프트 개정 여부를 사용자에게 묻는다(이 계획 범위 밖).

## Task 4: 전체 확인과 전달

**Files:** 이 계획의 실행 기록, 리뷰 지적에 따른 Task 1·2 파일

- [ ] **Step 1: 수동 확인** — 로컬 dev 화면(`http://localhost:5173/2d/background/a9f77a56-3dfc-40f3-819d-62e40b5feaa1`)을 조회만 해서 1440px·390px, light·dark에서 비교/편집 전환·경계 끌기·이름표 숨김을 확인한다. API는 조회만 하고 저장하지 않는다.
- [ ] **Step 2: 독립 전체 리뷰** — 구현에 참여하지 않은 리뷰 에이전트가 `main...HEAD` 전체 diff를 설계 문서 기준으로 검토하고, 지적을 고친 뒤 영향받은 스택 회귀를 다시 돌린다.
- [ ] **Step 3: 문서 검사** — `pnpm docs:check`와 `git diff --check`.
- [ ] **Step 4: 실행 기록** — 아래 실행 기록에 실행 방식, 작업별 커밋, 회귀 명령·결과(통과·실패·건너뜀 수), Task 3 결과, 남은 제약을 적고 커밋한다.
- [ ] **Step 5: 전달** — push·PR 생성은 사용자에게 확인받은 뒤에 한다. 병합은 사용자가 직접 한다.

## 실행 기록

(실행 시 작성)
