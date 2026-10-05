/**
 * Design Ref: §2.1 — 라우트 트리. 레이아웃 라우트가 모든 화면을 감싼다.
 *
 * `<Route element={<AppLayout/>}>` 안에 넣으면 화면이 바뀌어도 `AppLayout` 은
 * 언마운트되지 않는다. 사이드바 접힘 상태가 화면 이동으로 초기화되지 않는 이유이고
 * (§4.2 #4), 이동할 때마다 사이드바가 다시 그려지지 않는 이유이기도 하다.
 *
 * 리다이렉트는 레이아웃 밖에 둔다. 껍데기를 그렸다가 곧바로 버릴 이유가 없다.
 *
 * 3D 결과 모드는 여전히 라우트가 아니다 (app-shell Design §3.2).
 *
 * **관리자 화면은 지연 로드다** (사이클 #7 Check G-1). 라우트를 전부 정적으로
 * import 하면 관리자 화면 7개가 **첫 페인트 번들에 실린다** — 스튜디오만 쓰는
 * 사용자가 한 번도 열지 않을 코드를 매번 내려받는다. `check-bundle.mjs` 가
 * 초기 JS 예산을 지키는 이유이자, 그 스크립트 주석이 직접 말하는 바다:
 * *"Async chunks are excluded: that is the point of splitting them."*
 *
 * **스튜디오도 지연 로드로 내렸다.** 이전에는 "첫 페인트 경로라 정적으로 둔다" 였다.
 * Select 프리미티브를 Radix 로 바꾸면서 그 전제가 깨졌다 — 드롭다운 하나가
 * floating-ui 까지 끌고 와 초기 JS 를 119.64 → 148.51 kB 로 밀어올렸고, 예산
 * 122.55 kB 를 넘겼다. 스튜디오를 async 로 빼면 그 무게는 스튜디오를 여는
 * 사람만 낸다.
 *
 * 대가는 `/background` 직접 진입에서 청크를 기다리는 순간이다. 홈에서 들어오는
 * 흐름은 이미 레이아웃이 그려져 있어 티가 나지 않지만, 새로고침으로 작업을
 * 이어받는 경로(FR-13)에서는 본문이 잠깐 빈다.
 *
 * 홈은 그대로 정적이다. 랜딩이라 지연 로드할 대상이 없다.
 */
import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from '@/features/shell/layout/AppLayout'
import { ComingSoonScreen } from '@/features/screens/coming-soon/ComingSoonScreen'
import { HomeScreen } from '@/features/screens/home/HomeScreen'
import { importBackgroundStudio, importCharacterStudio } from '@/routes/prefetch'

// 스튜디오 — Radix Select 를 포함해 초기 예산을 넘기므로 여는 사람만 내려받는다.
// 지정자는 `prefetch.ts` 가 들고 있다 — 프리페치와 같은 청크를 가리켜야 한다
const BackgroundStudioScreen = lazy(() =>
  importBackgroundStudio().then((m) => ({ default: m.BackgroundStudioScreen })),
)

// 캐릭터 스튜디오 — 배경과 같은 이유로 지연 로드. 프리페치와 같은 지정자를 가리킨다
const CharacterStudioScreen = lazy(() =>
  importCharacterStudio().then((m) => ({ default: m.CharacterStudioScreen })),
)

// 관리자 묶음 — 사이드바를 눌러야 열린다. 첫 페인트에 실을 이유가 없다
const AdminScreen = lazy(() =>
  import('@/features/screens/admin/AdminScreen').then((m) => ({ default: m.AdminScreen })),
)
const PromptsScreen = lazy(() =>
  import('@/features/screens/admin/PromptsScreen').then((m) => ({ default: m.PromptsScreen })),
)
const PromptEditScreen = lazy(() =>
  import('@/features/screens/admin/PromptEditScreen').then((m) => ({
    default: m.PromptEditScreen,
  })),
)
const GoldenScreen = lazy(() =>
  import('@/features/screens/admin/GoldenScreen').then((m) => ({ default: m.GoldenScreen })),
)
const GoldenRunsScreen = lazy(() =>
  import('@/features/screens/admin/GoldenRunsScreen').then((m) => ({
    default: m.GoldenRunsScreen,
  })),
)
const PricesScreen = lazy(() =>
  import('@/features/screens/admin/PricesScreen').then((m) => ({ default: m.PricesScreen })),
)
const CallsScreen = lazy(() =>
  import('@/features/screens/admin/CallsScreen').then((m) => ({ default: m.CallsScreen })),
)
import { CATEGORY_META_LIST } from '@/features/screens/categoryLabels'
import { ROUTES } from '@/routes/paths'

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route path={ROUTES.home} element={<HomeScreen />} />
        {/*
          Design Ref: background-studio §5.2 — 두 경로가 같은 화면이다.
          `jobId` 유무가 입력/진행·결과를 가르며, 그래서 새로고침해도 이어진다 (FR-13).
        */}
        <Route
          path={ROUTES.background}
          element={
            <Deferred>
              <BackgroundStudioScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.backgroundJob}
          element={
            <Deferred>
              <BackgroundStudioScreen />
            </Deferred>
          }
        />

        {/*
          character-studio §6.3 — 배경과 같은 두 경로(입력 / 진행·결과).
          `jobId` 유무가 화면을 가르며 새로고침해도 이어진다 (FR-13).
        */}
        <Route
          path={ROUTES.character}
          element={
            <Deferred>
              <CharacterStudioScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.characterJob}
          element={
            <Deferred>
              <CharacterStudioScreen />
            </Deferred>
          }
        />

        {/*
          관리자 화면이 1개 → 4개. 섹션 탭으로 나눈다 (§2.3-6).
          `/admin` 은 리다이렉트라 기존 북마크가 깨지지 않는다
        */}
        <Route path={ROUTES.admin} element={<Navigate to={ROUTES.adminProviders} replace />} />
        <Route
          path={ROUTES.adminProviders}
          element={
            <Deferred>
              <AdminScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.adminPrompts}
          element={
            <Deferred>
              <PromptsScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.adminPromptEdit}
          element={
            <Deferred>
              <PromptEditScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.adminGolden}
          element={
            <Deferred>
              <GoldenScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.adminGoldenRuns}
          element={
            <Deferred>
              <GoldenRunsScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.adminPrices}
          element={
            <Deferred>
              <PricesScreen />
            </Deferred>
          }
        />
        <Route
          path={ROUTES.adminCalls}
          element={
            <Deferred>
              <CallsScreen />
            </Deferred>
          }
        />

        {/*
          준비 중 화면은 같은 컴포넌트를 카테고리만 바꿔 재사용한다.
          배경은 실제 화면이 생겨 `comingSoon: false` 이므로 여기서 빠진다 (§5.3)
        */}
        {CATEGORY_META_LIST.filter((meta) => meta.comingSoon).map((meta) => (
          <Route
            key={meta.category}
            path={meta.path}
            element={<ComingSoonScreen category={meta.category} />}
          />
        ))}
      </Route>

      {/* 알 수 없는 경로는 홈으로 (§4.1 #5) */}
      <Route path="*" element={<Navigate to={ROUTES.home} replace />} />
    </Routes>
  )
}

/**
 * 지연 로드된 화면의 경계.
 *
 * **폴백이 비어 있는 것이 의도적이다.** 청크는 같은 출처에서 수십 밀리초에 오므로
 * 스피너를 띄우면 그것이 깜빡이는 것만 보인다 — 레이아웃은 이미 그려져 있고
 * 본문만 잠깐 빈다. 로딩 표시가 필요한 것은 네트워크 요청이지 청크가 아니다.
 */
function Deferred({ children }: { children: React.ReactNode }) {
  return <Suspense fallback={null}>{children}</Suspense>
}
