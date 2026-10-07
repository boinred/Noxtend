# Frontend 개발 지침

`apps/frontend/**`에 적용한다. 루트 `AGENTS.md`, [PRODUCT.md](../../PRODUCT.md), [DESIGN.md](../../DESIGN.md)를 따르고 [noxtend-workflow](../../.agents/skills/noxtend-workflow/SKILL.md)의 Frontend·검증 자료를 읽는다. UI·모션 스킬 선택표와 실행 명령은 해당 자료에서 확인한다.

## 계층 경계

- `src/domain/`: 서버 상태 타입·순수 규칙. React·Query·라우터·HTTP·저장소 어댑터에 의존하지 않는다.
- `src/infra/`: API·테마·레이아웃 어댑터. `app/`·`features/`·`routes/`를 역참조하지 않는다.
- `src/app/queries/`: 조회·mutation·폴링·캐시 조립. 화면은 이 계층을 사용하며 `infra/api/`를 직접 호출하지 않는다.
- `src/features/`: 화면·앱 셸. `routes/paths.ts`·`routes/navItems.ts`를 참조하고 라우트 트리 `routes/index.tsx`를 가져오지 않는다.
- `src/routes/`: 경로·화면·프리페치. `infra/`를 참조하지 않는다. 스튜디오 지연 import는 `routes/prefetch.ts`와 공유하고 초기 번들에 넣지 않는다.
- `src/components/ui/`는 `src/lib/` 외 프로젝트 계층을 참조하지 않는다. `src/lib/`는 프로젝트 계층을 역참조하지 않는 공통 유틸리티다.

## 작업 진행 표시

- 신규·수정 작업의 진행 상태 UI는 [JobProgressPanel](src/features/screens/JobProgressPanel.tsx)을 우선 재사용한다. 헤더·진행 막대·단계 카드·성공 요약·새로고침/취소 행동 표시를 별도로 복제하지 않는다.
- 단계 판단·실제 완료 여부·집계는 작업별 연결부와 기존 도메인 함수에, 조회·취소 요청·취소 후 이동은 기존 화면에 둔다. 공용 표시 안에 작업 종류 분기·서버 요청·집계를 넣지 않는다.
- 기존 2D 진행 상태의 외관을 바꾸려면 구현 전에 목업 승인을 받는다.

## 서버 상태·API·오류

- 작업·공정·검수 상태의 정본은 서버다. 로컬 state는 draft·선택·표시에 쓰며 서버 상태를 복제하는 별도 상태 머신을 만들지 않는다. 생성 요청 접수와 완료를 구분한다.
- 봉투·`ApiError`·204·URL·헤더는 `src/infra/api/client.ts`에서 처리한다. 화면에 직접 `fetch`나 별도 봉투 해석을 추가하지 않고 FormData boundary는 브라우저에 맡긴다.
- 결과를 바꾸는 모든 요청 입력을 쿼리 키에 반영한다. mutation은 영향받는 캐시만 갱신·무효화한다. 조회 `signal`을 전달하고 `AbortError`를 오류 UI로 바꾸지 않는다. 늦은 응답이 새 선택·draft를 덮지 않게 한다.
- 로딩·정상 빈 결과·연결 실패·실제 404·부분 성공·취소·실패를 구분한다. 중복 제출을 막고 실패 이유와 재시도 행동을 표시한다. 기존 빈 목록 fallback을 새 오류 처리 기준으로 복사하지 않는다.
- `VITE_API_BASE_URL`을 사용하며 Vite 환경 변수는 공개된다. 공급자 API 키·비밀 연결 문자열을 넣지 않는다.

## UI·상호작용·스타일

- 기존 훅·프리미티브·`src/index.css` 테마 토큰·`*Styles.ts` 패턴을 재사용한다. 기존 shadcn/ui·Radix는 승인된 `design-system` 예외이며 라이브러리 추천만으로 Base UI로 교체하지 않는다.
- native dialog와 기존 프리미티브를 재사용하고 내부 메뉴·Select 포털의 top layer 표시·클릭을 유지한다.
- 모든 컨트롤의 접근 가능한 이름·키보드 조작·포커스 표시를 유지한다. 상태는 텍스트·ARIA로도 전달하며 dialog 닫기·포커스 복귀·`prefers-reduced-motion`을 지원한다.
- 화면은 기본 1440×900과 좁은 화면에서 확인한다. 긴 파츠 이름·메시지·메뉴·이미지·3D 결과의 잘림·가로 넘침을 방지한다.
- 포맷은 루트 `.editorconfig`·이 디렉터리의 `.prettierrc.json`·`.prettierignore`, 코드 품질·계층 검사는 기존 ESLint·Oxlint 설정을 따른다. `pnpm lint`에는 포맷 검사도 포함한다. 무관한 전체 소스 재포맷을 묶지 않는다.
- 커밋 전 lint는 루트 `.githooks/pre-commit`이 실행한다. 새 clone은 루트에서 `pnpm hooks:install`로 한 번 등록한다. 실행 대상·부분 stage 제약은 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)의 커밋 훅 항목을 따른다.
