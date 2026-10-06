---
name: api-breaking-change-detector
description: Review Noxtend HTTP contract changes across C# controllers and TypeScript consumers. Use for DTO, route, status, or serialization drift; report findings without changing code.
license: MIT
---

# API 계약 리뷰

[Awesome Copilot 원본](https://github.com/github/awesome-copilot/blob/143a3d976b3c1603cc8932984d5e1f28501cb5fc/skills/api-breaking-change-detector/SKILL.md)을 Noxtend에 맞게 수정했다. [MIT 라이선스](../LICENSE.awesome-copilot)를 유지한다.

루트·양쪽 영역의 `AGENTS.md`와 `$karpathy-guidelines`를 따른다. 리뷰 요청은 읽기 전용이며 수정 요청이 함께 있으면 리뷰 후 기존 `$tdd-cycle`로 진행한다.

## 계약의 실제 경로

- 서버: `apps/backend/Noxtend.Api/Program.cs`, `Controllers/`, `Contracts/ApiResponse.cs`, `Contracts/ApiResults.cs`와 해당 DTO·handler
- 클라이언트: `apps/frontend/src/infra/api/client.ts`, 해당 `*Api.ts`, `src/domain/`의 타입·파서와 실제 호출부
- 회귀: 관련 Backend API 테스트, Frontend API 테스트·`src/domain/job/backendParity.test.ts`, 해당 Playwright Fake 응답

## 검토

1. 요청된 diff·엔드포인트 범위를 정하고 현재 소스에서 HTTP method·route·query·header·body·응답의 연결을 찾는다. 기존 동작과 비교하려면 실제 기준 revision이나 변경 전 자료를 확인한다.
2. JSON 옵션·속성·변환기·DTO 매핑을 확인한다. C# 이름·enum·nullable 표기를 실제 JSON 값으로 단정하지 않는다. 필수 입력은 serializer·model binding·validator·handler 규칙과 테스트로 판단하며 값 타입·기본값 유무만으로 결정하지 않는다.
3. 양방향으로 필드 이름·타입·null/생략·빈 배열의 의미·필수값·상태 코드·오류 코드를 대조한다. 경로의 변수 이름은 정규화하되 query·header·배열 직렬화 조건을 보존한다.
4. `{ data, error }`와 `ApiResults` 매핑을 확인한다. 성공 204·이미지/메시 파일·파일 미존재의 빈 404는 해당 컨트롤러와 클라이언트 처리 경로로 검토한다. 봉투 예외를 일괄 결함으로 보고하지 않는다.
5. TypeScript 타입 선언만으로 응답 검증을 주장하지 않는다. 파서·실제 필드 사용·오류 처리·테스트 수집 범위를 확인한다. 소비 코드를 찾지 못하면 검색 범위와 미확인 연결을 기록한다.

## 결과와 검증

- 각 발견에 양쪽 파일 위치·실제 불일치·영향·재현 입력 또는 검증 방법·최소 수정 방향을 적는다. 라우트 추적·타입 연결·실행 검증 중 근거를 표시한다.
- 확인된 결함, 정적 탐색으로 추정한 위험, 미확인 항목을 구분한다. 서버가 허용하는 추가 요청 필드를 자동 오류로 분류하지 않는다.
- 선택한 테스트는 [검증 절차](../noxtend-workflow/references/verification.md)를 따른다. 코드 수정 시 API 계약 변경은 양쪽 스택을 검증한다. 리뷰만으로 전체 API 호환성이나 실 서버 검증을 주장하지 않는다.
