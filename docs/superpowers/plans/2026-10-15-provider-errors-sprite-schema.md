# 공급자 오류 진단과 2D 분석 스키마 보완

**목표:** 민감정보 없는 공급자 오류 진단과 OpenAI 호환 2D 분석 스키마

**설계:** [2D 에셋 역할 설계](../specs/2026-10-14-2d-asset-roles-design.md)와 사용자 승인 버그 수정

## 목적과 실행 방식

사용자가 승인한 버그 수정이다. 2026-10-15 원본 생성 성공 후 AnalyzeSprites v3 요청이 HTTP 400으로 실패했으며 원문 본문은 저장되지 않았다. oneOf가 유력한 원인이지만 과거 응답 원문으로 확정하지 않는다. Subagent-driven으로 작업별 구현·독립 리뷰 후 전체 Backend 회귀를 수행한다.

## Global Constraints

- 실제 AI 호출·작업 재시도·배포·개발 DB 변경 없이 Fake HTTP와 격리 SQL로 검증한다.
- HTTP DTO·상태 코드·작업 실패 코드·취소·재시도 분류를 유지한다. 기존 FailureReason 문자열을 통해 안전한 진단을 기록하며 원문 ResponsePayload는 저장하지 않는다.
- 원문 오류 전체·인증값·키·조직/프로젝트 식별자·URL 쿼리·개인정보·요청/이미지/base64를 로그나 호출 내역에 추가하지 않는다. 확인할 수 없는 오류 내용은 원문 대신 안전한 대체 문구로 처리한다.
- 기존 migration과 과거 프롬프트를 바꾸지 않는다. 새 migration으로 변경 대상의 새 버전을 추가하고 다른 슬롯과 운영자 프롬프트를 보존한다.
- 기존 C# 스타일과 짧은 한국어 명사구 주석을 유지한다. 무관한 파일·데이터는 수정하지 않는다.

## Review Focus

오류 진단의 민감정보 미노출·기록 크기·재시도 유지와 새 프롬프트의 공급자 호환성·과거 버전 및 운영자 데이터 보존을 독립 검토한다.

## Task 1: OpenAI 오류 응답의 안전한 진단

**파일:** `apps/backend/Noxtend.Infrastructure/ProviderHttp.cs`, 관련 OpenAI 어댑터와 테스트

- [x] 오류 기록 보완·focused 테스트·독립 리뷰

- 범위: `Noxtend.Infrastructure/ProviderHttp.cs`, OpenAiProvider·OpenAiImageProvider, 필요한 소규모 진단 helper, 관련 provider·RecordingLlmProvider/RecordingImageProvider 테스트, `docs/xHuman/providers-and-prompts.md`.
- 현재의 호출·기록·예외 경로와 FailureReason 저장 길이를 먼저 확인한다. OpenAI 텍스트·이미지의 공통 오류 처리에서 status와 유효한 error.code/type/param 및 민감정보를 제거한 원인 정보를 제공한다. 알려진 스키마 오류는 문제 키워드와 안전한 필드 정보를 보존한다. 안전성을 보장할 수 없는 자유 텍스트·필드는 제한한다.
- 서버가 받은 원문 전체를 예외/로그/ResponsePayload에 넣지 않는다. 정확한 API 키와 일반 키 형태, Authorization/Bearer, 조직·프로젝트 ID, 이메일, URL·쿼리, 요청/이미지/base64가 섞인 오류, 악성/잘못된 JSON·HTML·빈 응답·과대 응답·제어문자를 검증한다. 출력과 읽기 크기를 제한하고 실패 처리 자체가 기존 HTTP 실패를 덮어쓰지 않도록 한다. 취소는 유지한다.
- 429 insufficient_quota와 rate_limit, 일반 4xx/5xx의 재시도 분류는 유지한다. 성공 응답 처리와 API 계약은 바꾸지 않는다.
- RED→GREEN: 기존 OpenAiProviderTests/RecordingLlmProviderTests와 관련 이미지 테스트에서 실제 adapter→recorder 경로의 진단 보존·비밀값 미노출을 검증한다. 테스트 원문은 가짜 값만 사용한다.
- 전체 회귀는 두 작업 후 주 에이전트가 한 번 수행한다. 커밋·푸시·배포하지 말고 파일 변경과 focused 결과를 보고한다.

## Task 2: 호환 분석 스키마와 후속 migration

**파일:** `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs`, 새 migration과 프롬프트 테스트

- [x] V4 스키마·migration·focused 테스트·독립 리뷰

- 범위: `SeedPrompts.cs`, 새 `ReviseSpriteAnalyzePromptV4` migration, SeedPromptVariableTests/SpriteAnalyzePromptMigrationTests 및 필요한 schema 검사, `docs/xHuman/providers-and-prompts.md`.
- AnalyzeSpritesV3와 기존 migration의 의미를 유지하고 새 AnalyzeSpritesV4를 만든다. grid의 null/object 합집합은 OpenAI 지원 anyOf로 표현한다. 역할·격자·좌표·숫자 범위·파서 도메인 검증은 유지한다.
- 새 migration은 기존 Background AnalyzeSprites 슬롯의 MAX(Version)+1과 새 ID를 사용한다. 기본 v3가 활성일 때 v4로 교체하고 과거 행·다른 슬롯을 보존한다. 운영자 활성 프롬프트는 자동 변경하지 않는다. Down은 이 migration이 활성화한 v4가 여전히 활성일 때만 이전 v3를 복구하며, 운영자가 이후 활성화한 버전을 바꾸지 않는다. 운영자 추가 버전·비활성 최신 버전·건너뛴 migration의 Down 경계를 검증한다.
- RED→GREEN: 최신 V4의 중첩 합집합까지 required/additionalProperties와 unsupported oneOf 검사를 수행하고 실제 provider payload에서도 새 schema를 확인한다. 격리 SQL에서 V3→V4 Up/Down, 새 DB, 운영자 버전·다른 슬롯 보존을 검증한다.
- Task 1의 diagnostic helper/API를 변경하지 않는다. 충돌 시 주 에이전트에게 알린다. 전체 회귀는 주 에이전트가 수행한다. 커밋·푸시·배포하지 않는다.

## 검증 및 실행 기록

- 기준: main `fbe2bc0`, 작업 브랜치 `codex/provider-errors-sprite-schema`; 깨끗한 기존 checkout을 재사용한다.
- 작업별 focused RED/GREEN 및 독립 리뷰 후 `dotnet build apps/backend/Noxtend.slnx`, 유료 smoke 제외 전체 `dotnet test`, `pnpm docs:check`, `git diff --check`를 수행한다.
- 배포·실제 공급자 호출은 이번 검증에 포함하지 않는다.

### 작업별 결과

- Task 1: 실제 OpenAI 텍스트/이미지 adapter → recorder에서 기존 상태 코드만 남는 실패 2건 재현 후 구현. 독립 리뷰에서 성공 본문 전송 오류의 재시도 회귀를 발견해 수정했고, 스키마 context 외의 인용문에서 필드를 오인하지 않도록 보완했다. 최종 focused 82 통과, 실패·건너뜀 0. 재리뷰 승인.
- Task 2: 최신 schema·실제 요청 payload·후속 migration의 누락 동작 10건 재현 후 구현. 최종 focused 76 통과, 실패·건너뜀 0. 독립 리뷰 승인.
- 판단: 기본 v3만 자동 교체하고 운영자 활성/수정 프롬프트는 보존한다. 운영자 프롬프트에 같은 oneOf 문제가 있으면 별도 수정이 필요하다.
- 오류 기록 한계: 알려진 code/type/param과 oneOf 거절 진단만 허용하고 자유 문장은 저장하지 않는다. 16 KiB를 넘거나 해석할 수 없는 응답은 상태 코드로 대체하며, 임의 공급자 오류 원문 복구를 보장하지 않는다.
- 빌드: `dotnet build apps/backend/Noxtend.slnx` 통과(오류 0, SSH.NET NU1903 경고 4). 로그: `/tmp/noxtend-provider-schema-build.log`.
- 전체 Backend 회귀: `dotnet test apps/backend/Noxtend.slnx --no-build --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests' --logger 'trx;LogFileName=provider-schema-regression.trx' --results-directory /tmp/noxtend-provider-schema-results` → 2016 통과, 실패 0, 건너뜀 0, 12분 8초. 로그: `/tmp/noxtend-provider-schema-regression.log`; TRX: `/tmp/noxtend-provider-schema-results/provider-schema-regression.trx`.
- 최종 독립 리뷰: Critical/Important/Minor 지적 없음, 승인. 초기 리뷰의 전송 오류 재시도 및 context 필드 오진 지적은 모두 수정·재리뷰 완료.
- 문서·공백: `pnpm docs:check`, `git diff --check` 통과. 기존 계획 문서 2개의 500행 초과 경고는 남아 있다.
- 검증 대상은 기준 `fbe2bc0` 위 작업 브랜치의 코드 변경 전체다. 실제 AI·유료 smoke·Frontend 전체 회귀·Docker 배포·개발 DB migration·작업 재시도·커밋·푸시는 수행하지 않았다. HTTP DTO·Frontend 소스 변경은 없다. 과거 400 응답의 원문은 복구하지 못하며 실제 공급자의 수정 스키마 수락은 별도 확인 대상이다.

### 사용자 승인 후 로컬 배포 — 2026-10-15

- 후속 Docker 반영 요청에 따라 `deploy/local-up.sh -d`로 현재 미커밋 수정본을 빌드하고 `docker-desktop`의 `noxtend` namespace에 배포했다. 위 미배포 기록은 배포 전 자동 검증 시점이다.
- 새 API 파드 `api-79d884c896-9nrk7` Ready, 재시작 0. 런타임 이미지 ID `sha256:3dc290ca10cb16184dca8a5fdfff3750e35c202eaa7a7de53b67df54e98edd16`이 빌드 config digest와 일치한다.
- `20261015080000_ReviseSpriteAnalyzePromptV4` 적용 로그와 API 조회에서 활성 v4·grid.anyOf를 확인했다. `/health`의 DB·Redis·imageDecoder·Blob 모두 ok, Frontend 5173 응답 HTTP 200. DB·Blob·Data Protection PVC의 기존 volume ID를 유지했다.
- 도구 실행 세션 종료 후 스크립트의 포워딩도 종료되어, `start_new_session=True`로 독립 포워딩 PID 77780을 생성했다. 부모 PID 1과 별도 호출에서 health 정상 응답을 확인했다. 포워딩은 파드 재시작 시 다시 연결해야 한다.
- 근거: `/tmp/noxtend-provider-schema-deploy.log`, `/tmp/noxtend-provider-schema-deploy.json`, `/tmp/noxtend-provider-schema-startup.log`. 실제 AI 호출·기존 실패 작업 재시도·커밋·푸시는 수행하지 않았다.
