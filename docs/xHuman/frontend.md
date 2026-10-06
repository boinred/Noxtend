# Frontend 코드 안내

Frontend의 상시 규칙은 [apps/frontend/AGENTS.md](../../apps/frontend/AGENTS.md), 작업 절차·UI 스킬 선택·검증 명령은 [Frontend 작업 절차](../../.agents/skills/noxtend-workflow/references/frontend.md)가 정본이다. 이 문서는 화면 코드를 찾기 위한 경로 지도다. 경로는 `apps/frontend/` 기준이며, 현재 구현과 다르면 코드·테스트를 확인하고 이 안내를 고친다.

## 앱 구조

React 19·Vite·React Router·TanStack Query·Tailwind·shadcn/ui(Radix) 구성이다. 계층 경계는 `eslint.config.js`와 `src/routes/layerRules.test.ts`로 확인한다.

| 책임 | 시작점 |
| --- | --- |
| 진입점·전역 Provider | `src/main.tsx`, `src/app/App.tsx`, `src/app/providers.tsx`(`QueryClient`·`BrowserRouter`·레이아웃 설정) |
| 서버 상태 타입·순수 규칙 | `src/domain/job`, `src/domain/provider`, `src/domain/similarity`, `src/domain/tuning`, `src/domain/layout` |
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
| 캐릭터 스튜디오 | `/character`, `/character/:jobId` | `features/screens/character/CharacterStudioScreen.tsx` |
| 오브젝트(준비 중) | `/object` | `features/screens/coming-soon/`, `navItems.ts`의 `comingSoon` |
| 관리자 | `/admin/*`(공급자·프롬프트·골든·단가·호출 내역) | `features/screens/admin/` |

스튜디오와 관리자 화면은 `lazy()`로 지연 로드한다. 스튜디오의 `import()` 지정자는 `routes/prefetch.ts`에만 두고 라우트와 사이드바 프리페치가 공유한다.

## 작업 흐름별 진입점

| 변경 대상 | 코드 흐름 | 관련 테스트 시작점 |
| --- | --- | --- |
| 작업 생성·조회·폴링 | 화면 → `useJob`·`useStartJob`·`useJobList` → `jobApi.ts` → `client.ts` | `src/infra/api/jobApi.test.ts`, `src/domain/job/progress.test.ts`, `tests/e2e/home-active-job.spec.ts` |
| 이미지 업로드 | 스튜디오 화면의 `ImageDropzone` → 화면의 `useUpload` → `uploadApi.ts`, 검증은 `domain/job/rules.ts` | `src/domain/job/rules.test.ts` |
| 검수 게이트·설명 확인 | `ReviewGate`·`DescriptionsReview` → `useReview.ts` → `reviewApi.ts` | `tests/e2e/review-gate.spec.ts`, `features/screens/background/descriptionReview.test.ts` |
| 파츠 이미지 생성·재시도 | 스튜디오 화면·`PartGallery` → `useJob.ts`의 mutation(`useRetryTask`·`useGenerateSelectedViews` 등) → `jobApi.ts` | `tests/e2e/part-generation-*.spec.ts`, `src/domain/job/generation.test.ts` |
| 3D 메시 결과·다운로드 | `MeshTile`·`MeshViewerDialog`(`@google/model-viewer`), `MeshDownloadMenu` | `src/domain/job/mesh*.test.ts`, `tests/e2e/mesh*.spec.ts` |
| 장면 조립·유사도 | `BackgroundStudioScreen`의 `useSceneLayout` → `SceneAssemblyView`·`ModelViewerCanvas`(`@react-three/fiber`), `SimilarityInspector` → `useSimilarity` | `src/domain/job/scene*.test.ts`, `tests/e2e/scene-*.spec.ts`, `tests/e2e/similarity.spec.ts` |
| 공급자 관리 | `ProviderList`·`ProviderForm` → `useProviders.ts` → `providerApi.ts` | `src/infra/api/providerApi.test.ts` |
| 프롬프트·골든·단가·호출 내역 | `features/screens/admin/*Screen.tsx` → `useTuning.ts` → `tuningApi.ts` | `src/domain/tuning/*.test.ts`, `tests/e2e/decomposition-admin.spec.ts`, `tests/e2e/prompts-category.spec.ts`, `tests/e2e/prices-admin.spec.ts` |

## 서버 상태와 계약

- 작업·공정 상태 문자열은 `src/domain/job/types.ts`에 있고, 종료 판정(`isTerminal`)과 폴링 간격(`nextPollDelayMs`)도 이곳에서 정한다. `useJob`은 종료 상태가 되면 폴링을 멈춘다.
- `JobStatus`·`TaskKind`는 Backend 열거형과 `src/domain/job/backendParity.test.ts`로 대조한다. Backend 상태·공정이 바뀌면 이 테스트와 타입을 함께 고친다.
- 오류 메시지·코드 해석은 `src/app/queries/errors.ts`, 공정 실패 문구는 `features/screens/background/failureMessages.ts`에서 확인한다.
- 이미지·메시 파일 URL은 `jobApi.ts`·`uploadApi.ts`의 `*Url` 함수로 만들고 화면에서 경로를 조합하지 않는다.

## 탐색과 검증

- E2E는 `tests/e2e/fakeApi.ts`의 가짜 API로 동작한다. 새 API를 화면에서 쓰면 가짜 응답도 함께 추가한다.
- `pnpm test`는 `src/**/*.test.ts`만 수집하며 `.test.tsx`는 수집하지 않는다. 명령과 수집 범위는 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다.
- API 계약이 바뀌면 [backend.md](backend.md)의 Controller·DTO도 확인하고 양쪽 스택을 검증한다.
- 로컬 API 주소는 `apps/frontend/.env.development.local`의 `VITE_API_BASE_URL`이다. 로컬 기동은 [로컬 기동 절차](../../deploy/k8s/README.md)를 따른다.
