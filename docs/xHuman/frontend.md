# Frontend 코드 안내

Frontend의 상시 규칙은 [apps/frontend/AGENTS.md](../../apps/frontend/AGENTS.md), 작업 절차·UI 스킬 선택·검증 명령은 [Frontend 작업 절차](../../.agents/skills/noxtend-workflow/references/frontend.md)가 정본이다. 이 문서는 화면 코드를 찾기 위한 경로 지도다. 경로는 `apps/frontend/` 기준이며, 현재 구현과 다르면 코드·테스트를 확인하고 이 안내를 고친다.

## 앱 구조

React 19·Vite·React Router·TanStack Query·Tailwind·shadcn/ui(Radix) 구성이다. 계층 경계는 `eslint.config.js`와 `src/routes/layerRules.test.ts`로 확인한다.

| 책임 | 시작점 |
| --- | --- |
| 진입점·전역 Provider | `src/main.tsx`, `src/app/App.tsx`, `src/app/providers.tsx`(`QueryClient`·`BrowserRouter`·레이아웃 설정) |
| 서버 상태 타입·순수 규칙 | `src/domain/job`, `src/domain/sprites`, `src/domain/provider`, `src/domain/similarity`, `src/domain/tuning`, `src/domain/layout` |
| HTTP 호출 | `src/infra/api/client.ts`(봉투·`ApiError`·`VITE_API_BASE_URL`), 도메인별 `*Api.ts` |
| 조회·mutation·폴링·캐시 | `src/app/queries/`, 쿼리 키는 `keys.ts` |
| 경로·내비게이션·지연 로드 | `src/routes/paths.ts`, `navItems.ts`, `index.tsx`, `prefetch.ts` |
| 화면·앱 셸 | `src/features/screens/`, `src/features/shell/` |
| 공통 UI·유틸·테마 | `src/components/ui/`, `src/lib/`, `src/index.css`, `src/styles/`, `src/infra/theme` |

## 화면과 경로

경로 문자열과 경로 생성 함수는 `src/routes/paths.ts`의 `ROUTES`와 `*Path` 함수에만 둔다. 작업 화면은 URL이 작업의 주소이고, 입력 화면은 이미지·모델 선택을 쿼리로 이어받는다(`backgroundWithImagePath`, `characterWithImagePath`).

| 화면 | 경로 | 코드 |
| --- | --- | --- |
| 홈·진행 중 작업 | `/` | `features/screens/home/` |
| 배경 스튜디오 | `/background`, `/background/:jobId` | `features/screens/background/BackgroundStudioScreen.tsx` |
| 2D 배경 스튜디오 | `/2d/background`, `/2d/background/:jobId` | `features/screens/sprites/SpriteStudioScreen.tsx` |
| 캐릭터 스튜디오 | `/character`, `/character/:jobId` | `features/screens/character/CharacterStudioScreen.tsx` |
| 2D 캐릭터·오브젝트(준비 중) | `/2d/character`, `/2d/object` | 기존 `features/screens/coming-soon/ComingSoonScreen.tsx` |
| 오브젝트(준비 중) | `/object` | `features/screens/coming-soon/`, `navItems.ts`의 `comingSoon` |
| 관리자 | `/admin/*`(공급자·프롬프트·골든·단가·호출 내역) | `features/screens/admin/` |

홈·설정은 `NAV_ITEMS`·`NAV_ITEMS_FOOTER`, 제작 그룹은 `navItems.ts`의 `NAV_GROUPS: readonly NavGroup[]`가 정의한다. `Sidebar.tsx`는 desktop 그룹과 접힌 레일을 표시하며 720px 이하에서는 `MobileStudioMenu.tsx`만 lazy import해 기존 Radix 메뉴를 연다. 상세/관리자 하위 URL도 상위 항목을 활성화한다. `prefetchForNav`는 3D 배경·캐릭터와 2D 배경 키를 공유 import에 연결한다.

스튜디오와 관리자 화면은 `lazy()`로 지연 로드한다. 스튜디오의 `import()` 지정자는 `routes/prefetch.ts`에만 두고 라우트와 사이드바 프리페치가 공유한다.

## 작업 흐름별 진입점

| 변경 대상 | 코드 흐름 | 관련 테스트 시작점 |
| --- | --- | --- |
| 작업 생성·조회·폴링 | 화면 → `useJob`·`useStartJob`·`useJobList` → `jobApi.ts` → `client.ts` | `src/infra/api/jobApi.test.ts`, `src/domain/job/progress.test.ts`, `tests/e2e/home-active-job.spec.ts` |
| 2D 계약·접수·검수·내보내기 | `domain/sprites/types.ts`의 wire 타입·reader, `rules.ts`의 `productionModeOf`·square/diamond `tileOffsets`·`playback.ts`의 `frameAt` → `useSprites.ts` → `spriteApi.ts`; 상세는 기존 `useJob` 공유 | `src/domain/sprites/rules.test.ts`, `src/infra/api/spriteApi.test.ts`, `src/app/queries/useSprites.test.ts`, `tests/e2e/sprites-static.spec.ts`, `src/domain/sprites/playback.test.ts`, `tests/e2e/sprites-animation.spec.ts` |
| 이미지 업로드 | 스튜디오 화면의 `ImageDropzone` → 화면의 `useUpload` → `uploadApi.ts`, 검증은 `domain/job/rules.ts` | `src/domain/job/rules.test.ts` |
| 검수 게이트·설명 확인 | `ReviewGate`·`DescriptionsReview` → `useReview.ts` → `reviewApi.ts` | `tests/e2e/review-gate.spec.ts`, `features/screens/background/descriptionReview.test.ts` |
| 파츠 이미지 생성·재시도 | 스튜디오 화면·`PartGallery` → `useJob.ts`의 mutation(`useRetryTask`·`useGenerateSelectedViews` 등) → `jobApi.ts` | `tests/e2e/part-generation-*.spec.ts`, `src/domain/job/generation.test.ts` |
| 3D 메시 결과·다운로드 | `MeshTile`·`MeshViewerDialog`(`@google/model-viewer`), `MeshDownloadMenu` | `src/domain/job/mesh*.test.ts`, `tests/e2e/mesh*.spec.ts` |
| 장면 조립·유사도 | `BackgroundStudioScreen`의 `useSceneLayout` → `SceneAssemblyView`·`ModelViewerCanvas`(`@react-three/fiber`), `SimilarityInspector` → `useSimilarity` | `src/domain/job/scene*.test.ts`, `tests/e2e/scene-*.spec.ts`, `tests/e2e/similarity.spec.ts` |
| 공급자 관리 | `ProviderList`·`ProviderForm` → `useProviders.ts` → `providerApi.ts` | `src/infra/api/providerApi.test.ts` |
| 프롬프트·골든·단가·호출 내역 | `features/screens/admin/*Screen.tsx` → `useTuning.ts` → `tuningApi.ts` | `src/domain/tuning/*.test.ts`, `tests/e2e/decomposition-admin.spec.ts`, `tests/e2e/prompts-category.spec.ts`, `tests/e2e/prices-admin.spec.ts` |

## 서버 상태와 계약

- `Job`·`JobSummary`의 선택 필드 `productionMode`·nullable `sprite`는 구형 응답 호환용이다. `getJob`·`listJobs`에서 `readSpriteJob`·`readSpriteSummary`로 신규 enum·필수 필드·배열·수치 형태를 검사하고, 구형 신규 필드 누락만 `threeD`·`null`로 읽는다. 메시 선택 여부로 모드를 추측하지 않는다.
- 작업·공정 상태 문자열은 `src/domain/job/types.ts`에 있고, 종료 판정(`isTerminal`)과 폴링 간격(`nextPollDelayMs`)도 이곳에서 정한다. `useJob`은 검수 대기와 실행 중 공정의 폴링을 유지하고 취소 또는 실행 공정 없는 종료 상태에서 멈춘다.
- `JobStatus`·`TaskKind`·제작 모드·sprite enum은 Backend 열거형과 `src/domain/job/backendParity.test.ts`로 대조한다. Backend 상태·공정이 바뀌면 이 테스트와 타입을 함께 고친다.
- 오류 메시지·코드·HTTP status 해석(`apiErrorMessage`·`apiErrorCode`·`apiErrorStatus`)은 `src/app/queries/errors.ts`, 공정 실패 문구는 `features/screens/background/failureMessages.ts`에서 확인한다.
- 이미지·메시·sprite PNG/ZIP 파일 URL은 `jobApi.ts`·`uploadApi.ts`·`spriteApi.ts`의 `*Url` 함수로 만들고 `app/queries/media.ts`에서 화면에 내보낸다.
- `queryKeys.jobList(filter, limit=10, productionMode?)`는 실제 조회 조건별 캐시를 구분한다. 상세는 모드와 무관하게 기존 `queryKeys.job(jobId)`를 공유한다.
- 홈의 `ActiveJobSpotlight`·`WorkStatusSection`은 모든 `jobPath(category, jobId, productionMode)` 호출에 모드를 전달한다. `categoryLabels.ts`의 `jobCategoryLabel`·`jobSummaryCount`·`spritePhaseLabel`은 mode/category·에셋/승인/누적 이미지 또는 legacy 파츠·서버 phase 라벨을 표시한다. spotlight는 2D 현재 frame.currentTaskId/currentImageId 기준 공정/슬롯 수를 사용하고 분석 완료를 전체 완료율로 계산하지 않는다.
- `HomeScreen`의 각 목록·spotlight 상세, 기존 배경/캐릭터 입력·상세, `CallsScreen`의 종료 작업 선택은 로딩·조회 실패·정상 빈 결과·실제 404를 구분하고 재조회 행동을 제공한다. 배경/캐릭터 상세의 캐시된 재조회 오류는 같은 검수 컴포넌트와 미저장 draft를 유지한다. 기존 `useJobCalls` 정책은 유지한다.
- `useJob`·`useJobList`·`useProviders`는 `error`·`isError`·`refetch`를 노출한다. 목록·공급자 HTTP 실패를 빈 성공으로 바꾸지 않고 `useJob.isNotFound`는 실제 HTTP 404만 가리킨다. 배열 기본값은 표시용이며 호출자는 오류 상태를 먼저 확인해야 한다.
- `useSprites.ts`의 7개 mutation 훅은 입력의 `requestId`·`expectedRevision`을 유지하고 자동 retry를 끈다. 새 사용자 동작에서 UUID를 만들고 응답 유실 재시도는 반환된 mutation의 `variables`를 그대로 다시 보낸다. 성공은 상세·목록을, 409는 해당 상세·목록을 무효화하고 오류를 표시한다. 최초 접수 409는 대상 ID가 없어 목록만 무효화한다. receipt의 과거 revision으로 draft를 되돌리지 않는다.
- sprite 요청은 API에서 DTO 필드만 명시적으로 직렬화한다. route의 job/asset/index와 UI draft 필드는 본문에 넣지 않는다. ProviderModel의 선택 `sprite` capability는 서버가 확인한 `supportsTransparency`·`sizes`이며 누락/null을 공급자 이름으로 추정하지 않는다.

## 탐색과 검증

- E2E는 `tests/e2e/fakeApi.ts`의 가짜 API로 동작한다. 새 API를 화면에서 쓰면 가짜 응답도 함께 추가한다.
- `pnpm test`는 `src/**/*.test.ts`만 수집하며 `.test.tsx`는 수집하지 않는다. 명령과 수집 범위는 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다.
- API 계약이 바뀌면 [backend.md](backend.md)의 Controller·DTO도 확인하고 양쪽 스택을 검증한다.
- 로컬 API 주소는 `apps/frontend/.env.development.local`의 `VITE_API_BASE_URL`이다. 로컬 기동은 [로컬 기동 절차](../../deploy/k8s/README.md)를 따른다.

## 2D 배경 스튜디오

- `SpriteInput.tsx`는 기존 `ImageDropzone`·`ProviderSelect`·`ModelSelect`·`useUpload`를 재사용한다. 시점과 결과 유형은 필수 선택이며, 이미지 모델의 확인된 `sprite.supportsTransparency`와 모든 양수 `sprite.sizes`가 필요하다. 최종 타일 너비로 모델 요청 크기를 필터링하지 않는다.
- 기존 결과 진입은 `routes/paths.ts`의 `spriteBackgroundWithSourcePath(sourceJobId, sourceGeneratedImageId)`와 `readSpriteSource(params)`로 GUID 쌍을 전달한다. 쿼리 이름은 `sourceJobId`·`sourceGeneratedImageId`이며 외부 URL·Blob 키는 원본 정체로 받지 않는다. 작업 주소는 `spriteBackgroundJobPath(jobId)`다. 공유 `background/PartGallery.tsx`의 현재 방향 이미지 dialog에서 이 헬퍼로 2D 입력에 이동하므로 배경·캐릭터 결과 모두 같은 ID 쌍을 전달한다.
- `SpritePlanReview.tsx`는 원본 ROI·이름·깊이 순서·투명 배경·루프 opt-in·4/8프레임(기본8)·FPS 1..30(기본8)·동작 설명 최대500자·대상 추가/삭제를 편집한다. 정적 대상은 계획 frameCount와 무관하게 1프레임이다. 생성 후에도 기존 계획 전체 목록으로 편집을 저장한다. 이름·순서·FPS 저장은 AI 생성 요청을 추가하지 않으며 생성 입력 변경은 서버의 계획 재검수 전이를 따른다. 실행 중인 대상 입력은 현재 frame.currentTaskId로 잠근다. 1..12개·최대 64프레임·원본 내부의 유한한 양수 ROI·중복 없는 순서를 확인하고 모델/생성 수/미확인 비용을 승인 전에 표시한다. draft의 baseline revision을 보존하고 외부 revision 변화 중 미저장 편집은 유지한다. 최신 계획은 명시적으로 다시 불러오며 receipt revision을 draft에 복사하지 않는다.
- `SpriteFrameReview.tsx`는 checkerboard PNG와 기준 선택 승인·대상별 기준 재생성·현재 프레임의 누락/실패/실행 표시·슬롯 재생성·최종 승인을 제공한다. 기준 승인 전에는 후속 프레임 재생성을 막고 기준 재생성의 후속 무효화/재검수 안내를 표시한다. 정적 대상은 기준 승인으로 최종 승인되며 `exportReady`에는 별도 승인을 반복하지 않는다. 루프 대상은 승인된 현재 기준과 모든 현재 슬롯 이미지가 있어야 최종 승인할 수 있고, 승인 snapshot의 imageIds로 완료 프레임 수를 표시한다. 같은 실행 슬롯은 비활성화하고 다른 asset 결과를 유지한다. 완료된 루프도 기준/슬롯 재생성으로 서버 검수를 다시 열 수 있으며 영향받은 ZIP은 이전 파일로 표시한다. 실패 공정 재시도와 취소는 기존 `useJob` mutation을 사용한다.
- `SpritePreview.tsx`의 props는 `{ sprite, timeMs, playing }`다. 초기 표시 시간/재생 상태를 props로 받아 각 asset의 FPS와 현재 슬롯 image ID로 합성/반복한다. 재생/일시정지·라벨이 있는 native range 키보드 탐색을 제공한다. requestAnimationFrame은 재생 중에만 예약하고 정지/unmount 또는 마지막 루프 해제 시 취소한다. 같은 화면에서 루프를 다시 켜도 정지 상태를 유지하고 수동 재생을 기다린다. reduced motion 초기 상태는 정지이며 수동 재생은 가능하다. 레이어는 Order 오름차순으로 같은 캔버스에 합성하며 개별 표시를 끌 수 있다. 타일은 `tileOffsets`로 square X/Y/Both 또는 diamond cell-grid 두 축을 배치한다. PNG URL의 jobId는 현재 라우트에서 읽는다.
- `SpriteExport.tsx`는 승인 asset ID 목록을 받아 사용자가 포함 대상을 선택하고 모든 제외 대상을 표시한다. `exports`에 실제 저장된 항목만 현재/이전 ZIP 링크로 표시하며 실패 pack의 `completedExportId`를 다운로드 완료로 추측하지 않는다. 일반 anchor는 서버의 Content-Disposition 파일명을 따른다.
- `SpriteStudioScreen.tsx`는 `useJob`의 서버 phase/job status를 표시하고 실제 404·조회 실패·로딩·부분 성공·취소·실패 pack을 구분한다. 생성 공정은 현재 frame.currentTaskId로만 표시하며 과거 슬롯 task를 현재 실패/재시도로 표시하지 않는다. 최초 조회 오류와 캐시된 작업의 재조회 오류를 구분하고, 후자는 같은 검수 화면과 미저장 draft를 유지한 채 오류/재조회 행동을 표시한다. 재조회 오류 중에는 상태를 바꾸는 요청을 비활성화한다. 새 동작은 새 UUID, 통신 재전송은 mutation의 같은 variables를 사용한다. 409는 조회 후 충돌을 표시하고 사용자 재조정 없이 새 revision으로 자동 재제출하지 않는다.
- `tests/e2e/fakeApi.ts`의 선택 `sprites` 옵션만 새 `spriteFakeApi.ts`에 연결된다. dedicated Fake는 GUID·출력 크기/실제 알파가 맞는 디코딩 PNG·프레임/시트/manifest가 일치하는 ZIP과 요청 기록을 제공한다. 기존 3D fixture는 유지한다.
