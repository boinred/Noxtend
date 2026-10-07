# 분석 요약 공용 컴포넌트 설계

- 작성일: 2026-10-07
- 상태: 2026-10-07 대화 설계 승인, 문서 검토 대기
- 기준: `main`의 `32e6e56`
- 대상: Frontend의 3D 배경, 캐릭터, 2D 배경 작업 화면

## 목적

3D 배경 결과 화면의 모델 배지 줄과 장면 분석 카드를 작업 종류와 무관한 공용 표시로 만든다. 2D 배경 스튜디오도 같은 모양으로 "어떤 모델로 돌았고 분석 결과가 무엇인가"를 보여준다. 진행 상태 공용화(`JobProgressPanel`)와 같은 구조로, 공용 표시는 그리기만 하고 작업별 연결부가 표시 입력을 만든다.

3D 배경·캐릭터 화면의 현재 모양과 동작은 바꾸지 않는다.

## 범위

포함하는 변경:

- 공용 분석 카드 `AnalysisSummaryPanel` 추가, `ScenePanel`을 그 연결부로 축소
- `ModelSummary`를 `background/`에서 공용 위치로 이동
- 2D 분석 연결부 `SpriteAnalysisPanel` 추가, 2D 스튜디오에 모델 배지·분석 카드 배치
- `SpritePlanReview`의 캔버스 크기 안내를 카드로 이동
- 구현 완료 후 Frontend 지침·도메인 탐색 문서에 재사용 규칙 추가

포함하지 않는 변경: `StudioViewTabs`(분석 | 3D 배경 탭), 서버 계약·분석 프롬프트, 2D 분석 결과에 장면 정보(팔레트·광원 등) 추가, 3D 화면 외관 개선. 새 UI 라이브러리나 작업 종류별 등록 시스템도 추가하지 않는다.

## 확인한 현재 구현

- `apps/frontend/src/features/screens/background/ScenePanel.tsx`가 `SceneSpec`을 받아 머리줄(`장면` + `timeOfDay · mood`), 팔레트 칩, 항목 4개(시점·광원·스케일 기준·렌더링)를 그린다. 스타일은 `backgroundStyles`의 `scene*`·`palette`·`swatch*` 클래스다.
- `RunResult.tsx`가 `job.scene`이 있을 때 `ScenePanel`을 그린다. 3D 배경(`BackgroundStudioScreen`)과 캐릭터(`CharacterStudioScreen`)가 모두 `RunResult`를 쓰므로 두 화면은 이미 같은 카드를 공유한다.
- `background/ModelSummary.tsx`는 `modelBadges(models, providerNames)`로 텍스트·이미지 배지를 그린다. 두 3D 화면이 진행 패널 아래에서 상태 분기 밖에 둔다.
- 2D(`SpriteStudioScreen`)에는 모델 배지와 분석 카드가 없다. 분석 결과는 `SpritePlanReview`의 안내 문장으로만 보인다.
- 2D 분석 결과(`SpriteState`)에는 장면 정보가 없다. `settings`(`view`·`outputKind`·`tileWidth`·`repeat`), `sourceCanvas`·`generationCanvas`·`outputCanvas`, `assets[].plan`이 있다.
- 2D 작업의 `job.models.text`는 최초 비생성 공정(`AnalyzeSprites`)의 모델, `job.models.image`는 접수 이미지 모델이다(`JobModelsResponse.From`).
- 기존 E2E가 `scene-panel`·`scene-summary`·`scene-palette`·`scene-swatch`·`scene-swatch-chip`·`scene-camera`·`scene-light`·`scene-scale`·`scene-style`·`model-summary`·`model-text`·`model-image` 테스트 ID에 의존한다(`tests/e2e/scene-palette.spec.ts` 외 4개).

## 선택한 구조

검토한 대안:

1. 표시 전용 공용 카드와 작업별 변환 — 선택.
2. `ScenePanel`에 2D 분기 추가 — 공용 표시가 작업 종류를 알게 되고 작업이 늘 때마다 분기가 는다. 진행 패널 원칙(공용 표시 안에 작업 종류 분기 금지)과 충돌한다.
3. 2D 전용 카드에 스타일만 공유 — 같은 마크업이 두 벌이 되어 한쪽만 고쳐지는 어긋남이 생긴다.

### 표시 입력과 책임

`apps/frontend/src/features/screens/AnalysisSummaryPanel.tsx`:

```ts
type AnalysisPaletteEntry = { name: string; hex: string | null }
type AnalysisField = { id: string; label: string; value: string; testId?: string }
type AnalysisSummaryPanelProps = {
  label: string
  summary: string
  palette?: readonly AnalysisPaletteEntry[]
  fields: readonly AnalysisField[]
  testIds?: { root?: string; summary?: string; palette?: string }
}
```

- 머리줄·팔레트·항목 격자의 마크업과 클래스를 현재 `ScenePanel`에서 그대로 옮긴다. 팔레트 칩의 `title`·`data-resolved`·미확정 칩 처리도 같다.
- `palette`가 없거나 비면 팔레트 줄을 그리지 않는다.
- 조회·mutation·작업 종류 판단을 넣지 않는다.

`apps/frontend/src/features/screens/ModelSummary.tsx`: 현재 파일을 이동한다. props·`modelBadges` 사용·배지 없을 때 미표시·테스트 ID를 유지한다. 스타일 클래스는 공용 파일 안으로 옮기거나 기존 `backgroundStyles`를 계속 참조하며, 어느 쪽이든 렌더 결과 클래스는 같아야 한다.

### 화면 구조

```text
[작업 진행 패널]
(텍스트 공급자 모델) (이미지 공급자 모델)
┌ <label>  <summary> ─────────────────────────┐
│ ■ 팔레트 칩 …                     (있을 때만) │
│ 항목1      항목2      항목3      항목4         │
└──────────────────────────────────────────────┘
[작업별 후속 화면]
```

## 작업별 연결

### 3D 배경·캐릭터

`ScenePanel`은 `SceneSpec`을 다음 입력으로 바꿔 공용 카드에 넘긴다. 문구 형식은 현재와 같다.

| 입력 | 값 | 테스트 ID |
| --- | --- | --- |
| label | `장면` | — |
| summary | `timeOfDay · mood` | `scene-summary` |
| palette | `scene.palette` | `scene-palette` |
| 시점 | `type · eyeLevel · 수평선 horizonY(소수 2자리)` | `scene-camera` |
| 광원 | `direction · temperature · shadowHardness` | `scene-light` |
| 스케일 기준 | `formatScaleReference(scene.scale)` | `scene-scale` |
| 렌더링 | `renderingStyle · materialFeel` | `scene-style` |

루트 테스트 ID는 `scene-panel`이다. `RunResult`·두 스튜디오 화면의 호출은 바꾸지 않는다. `ModelSummary`의 import 경로만 공용 위치로 바꾼다.

### 2D 배경

`apps/frontend/src/features/screens/sprites/SpriteAnalysisPanel.tsx`가 `SpriteState`를 공용 카드 입력으로 바꾼다. 값은 서버에 저장된 상태 기준이다. 검수 패널의 미저장 편집은 반영하지 않는다.

| 입력 | 값 | 테스트 ID |
| --- | --- | --- |
| label | `분석` | — |
| summary | `<시점> · <출력> N개` (예: `횡스크롤 · 레이어 4개`, `아이소메트릭 · 타일 6개`) | `sprite-analysis-summary` |
| palette | 없음 | — |
| 시점 | `횡스크롤` / `탑다운` / `아이소메트릭` | `sprite-analysis-view` |
| 출력 | 레이어: `배경 레이어`. 타일: `반복 타일 · <tileWidth>px · <반복>` | `sprite-analysis-output` |
| 캔버스 | `원본 W×H → 요청 W×H → 최종 W×H` | `sprite-analysis-canvas` |
| 대상 | `N개 · 총 M프레임` | `sprite-analysis-assets` |

- 루트 테스트 ID는 `sprite-analysis-panel`이다.
- 반복 문구는 입력 화면과 같다: `x` → 아이소메트릭이면 `격자 X축`, 아니면 `가로`; `y` → `격자 Y축` / `세로`; `both` → `양쪽`. 시점·출력 이름도 `SpriteInput`의 선택지 문구와 같다. 문구는 `domain/sprites`의 라벨 함수로 두고 분석 카드가 사용한다. `SpriteInput`은 이번 범위에서 바꾸지 않는다.
- 프레임 합계 M은 `SpritePlanReview`와 같은 규칙(`loop ? frameCount : 1`)으로 저장된 계획에서 센다.

`SpriteStudioScreen` 배치:

- 진행 패널 섹션 바로 아래에 `ModelSummary`를 둔다. 분석 중에도 보인다. 공급자 이름은 기존 공급자 조회 훅(`useProviders`)으로 얻는다.
- 그 아래 `SpriteAnalysisPanel`을 `sprite.phase !== 'analyzing'`일 때 그린다. 취소된 작업도 분석이 끝났으면 그린다.
- 작업 조회 실패·작업 없음·2D 아님 분기에는 그리지 않는다(현재 분기 유지).

`SpritePlanReview`: 두 번째 안내 문장의 캔버스 크기 부분을 지우고 `순서는 뒤에서 앞으로 증가` 안내만 남긴다. 첫 번째 줄(편집 중 대상 수·프레임 수·모델·예상 비용)과 나머지 동작은 그대로 둔다.

### 오류와 빈 값

- 모델 배지가 없으면 `ModelSummary`는 아무것도 그리지 않는다(현재 동작).
- 2D 계획 대상이 0개인 상태는 서버 계약상 분석 성공 후 없다. 그래도 들어오면 `0개 · 총 0프레임`으로 그대로 표시하고 숨기지 않는다.
- 공급자 조회가 실패하면 공급자 이름 없이 모델만 표시한다(`modelBadges`의 기존 처리).

## 검증과 수용 기준

### 3D 보존

- `ScenePanel` 렌더 결과의 테스트 ID·문구·팔레트 칩 속성이 현재와 같다. 기존 E2E `scene-palette`·`background-studio-e2e`·`background-studio-actions`·`part-generation-e2e`·`scene-assembly`가 수정 없이 통과한다.
- 두 3D 화면에서 `ModelSummary`의 위치·문구가 같다.

### 공용 표시와 2D 연결

- 공용 카드 단위 테스트: 팔레트 있음/없음, 항목 순서·테스트 ID, 미확정 색 칩.
- 2D 변환 단위 테스트: 레이어·타일(반복 x/y/both, 아이소메트릭 축 문구), 캔버스 문구, 루프·정적 혼합의 프레임 합계.
- 2D E2E(Fake API): 분석 중에는 모델 배지만, 계획 검수 단계부터 분석 카드가 보인다. 계획 저장 후 대상 수가 갱신되고 미저장 편집은 카드에 반영되지 않는다. 390px·light/dark에서 가로 스크롤이 생기지 않는다.
- Frontend 전체 회귀: `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`. 유료 호출은 실행하지 않는다.

## 구현 완료 후 지침 반영

- `apps/frontend/AGENTS.md`: 분석 결과 요약 UI는 `AnalysisSummaryPanel`을, 모델 표시는 `ModelSummary`를 우선 재사용하고 공용 표시 안에 작업 종류 분기를 넣지 않는다는 규칙을 진행 표시 규칙 옆에 추가한다.
- `docs/xHuman/frontend.md`: 공용 카드·연결부(`ScenePanel`, `SpriteAnalysisPanel`)·배치 위치를 현재 사실로 기록한다.
