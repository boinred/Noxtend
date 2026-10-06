# Frontend 작업 절차

파일 경로는 별도 표시가 없으면 `apps/frontend/` 기준이며 명령은 저장소 루트에서 실행한다. 상시 계층·서버 상태·접근성 규칙은 `apps/frontend/AGENTS.md`를 따른다.

## 변경 전 확인

- `package.json`·루트 `pnpm-lock.yaml`, `vite.config.ts`·`playwright.config.ts`와 관련 테스트를 읽는다.
- 현재 React 19·TypeScript·Vite·React Router·TanStack Query·Tailwind·shadcn/ui·Radix 구성을 확인하고 기존 훅·프리미티브로 해결 가능한지 먼저 검토한다.
- API 계약 변경은 Backend DTO·컨트롤러를 함께 확인한다. 과거 기획 문서·README 완료 표시를 현재 동작으로 사용하지 않는다.

## UI 스킬 선택

저장소 루트의 `.agents/skills/`에서 작업에 해당하는 스킬만 선택한다.

| 요청 범위 | 스킬과 경계 |
|---|---|
| UI 다듬기·컴포넌트 설계·일반 모션 결정 | `$emil-design-eng` |
| 제스처·spring·drag/swipe/sheet·관성·중단 가능한 전환·Apple 재질·타이포그래피·reduced motion | `$apple-design` |
| 기존 모션의 코드베이스 전체 감사와 개선 계획 | `$improve-animations` — 읽기 전용, 소스 수정 금지 |
| 모션이 필요한데 없는 지점 탐색 | `$find-animation-opportunities` — 읽기 전용, 기존 모션 검토·수정에 사용하지 않음 |
| 모션 효과의 정확한 이름 식별 | `$animation-vocabulary` — 설계·구현에 사용하지 않음 |
| 명시적으로 요청한 모션 코드·diff 리뷰 | `$review-animations` — 모션 문제만 검토 |
| 명시적으로 요청한 목록 내 프런트엔드 라이브러리 선택 | `$pick-ui-library` |

- 범위가 겹치면 단일 모션 diff 리뷰는 `$review-animations`, 전체 감사는 `$improve-animations`, 모션 부재 탐색은 `$find-animation-opportunities`로 구분한다.
- 기존 shadcn/ui·Radix 프리미티브는 승인된 `design-system` 예외다. `$pick-ui-library`의 신규 선택 권고만으로 Base UI로 교체하지 않는다.

## API·캐시·폴링 변경

- 경계 검사는 `eslint.config.js`·`src/routes/layerRules.test.ts`를 함께 확인한다. 테스트 통과가 모든 import 형태·런타임 동작을 검증하지는 않는다.
- `src/app/queries/keys.ts`에서 작업 ID·공급자·모델·카테고리·조회 조건 등 실제 요청 입력과 키를 대조한다. mutation 후 정본 응답 반영 또는 관련 상세·검수·목록·호출 내역의 무효화 범위를 확인한다.
- 작업/공급자 전환·연속 편집에서 늦은 응답이 새 선택/draft를 덮지 않는지 검증한다. 현재 통합 응답 버전 검사가 있다고 가정하지 않는다.
- 폴링은 `useJob.ts`·`useJobList.ts`·`useSimilarity.ts` 정책과 도메인 종료 판정을 따른다. 단계 추가 시 종료·재개·재시도·부분 성공의 폴링·화면 파생 규칙을 확인한다.
- 관련 화면 변경 시 `useJobList.ts` 빈 목록 fallback·`useJob.ts` `isNotFound` 매핑을 확인하고 연결 실패·실제 404·정상 빈 결과를 구분하는 검증을 포함한다.

## 화면 검증

- `src/index.css` 테마 토큰·기존 `*Styles.ts`, `src/features/screens/PageContainer.tsx`·앱 셸의 스크롤/폭 규칙을 확인한다.
- dialog 내부 메뉴·Select 포털은 `src/lib/top-layer-boundary.tsx`와 top layer 표시·클릭을 확인한다.
- 1440×900·좁은 화면, 키보드·dialog 포커스 복귀·reduced motion·긴 텍스트·로딩/실패·이미지/3D 결과를 직접 확인한다.

검증 명령·Vitest 수집 범위·Playwright Fake 사용은 [verification.md](verification.md)를 읽는다.
