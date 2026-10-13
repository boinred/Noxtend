# 2D 배경 기준 준비 단계 설계

2026-10-13 승인된 대화 설계를 정리한다. 이전 [2D 배경 프롬프트 모드 설계](2026-10-08-2d-background-prompt-mode-design.md)가 제외한 "이탈 후 복원"과 동기 생성 API를 이 설계가 대체한다.

## 목적

- 짧은 장면 설명을 선택한 시점·형식에 맞는 상세 이미지 프롬프트로 AI가 보완하고, 사용자가 확인·수정한 뒤 기준 이미지를 생성한다.
- 보완·생성 과정 전체를 SQL에 저장해 새로고침·이탈 후에도 같은 URL과 홈 "진행 중" 목록에서 복원한다.
- Anthropic 모델의 2D 분석이 픽셀 좌표를 돌려줘 `SpriteRules`에서 거부되는 문제를 프롬프트·스키마 안내로 고친다.

성공 기준:

- "80년대 서울 배경으로 하자" + 아이소메트릭·타일 설정에서 보완 프롬프트가 2:1 다이아몬드·타일 조건을 포함하고, 생성 프롬프트에도 설정이 반영된다.
- 보완·생성 공정 실행 중 새로고침해도 진행 상태와 기존 후보가 같은 화면에 보인다.
- 2D 분석 프롬프트·스키마가 `sourceBounds`를 원본 너비·높이 대비 0~1 비율로 명시한다.

## 범위

포함:

- 2D 작업의 새 첫 단계 `SpritePhase.SourceDraft`(기준 준비)와 원본 없는 작업 허용
- 새 공정 `EnhanceSpriteSource`(텍스트)·`GenerateSpriteSource`(이미지)와 기존 공정 실행 기반 재사용
- 기준 준비 명령 API 5개와 작업 조회 응답 확장
- 프롬프트 모드 입력 순서 변경(설정·모델 → 설명 → 보완)과 기준 준비 화면
- 시드 프롬프트: 보완(신규), 기준 이미지 생성(설정 반영 개정), 2D 분석(정규화 좌표 개정)
- 기존 동기 API `POST /api/uploads/generate`와 Frontend `generateSpriteSource` 제거

제외:

- 3D 배경 프롬프트 모드
- 기준 준비 중 설정(시점·형식·타일 크기·반복) 변경. 바꾸려면 새 작업을 만든다
- 후보 이미지 개별 삭제·정리, 생성 비용의 별도 집계 화면
- 이미지 모드(업로드)와 기존 작업 결과에서 시작하는 흐름의 변경
- 재시도 예약 지연 작업의 `ObjectDisposedException`(기존 버그, 별도 작업)

## 결정 사항

| 항목 | 결정 |
| --- | --- |
| 보완 방식 | 텍스트 모델이 설명·설정으로 프롬프트를 만들고 사용자가 확인·수정 후 생성 |
| 상태 저장 | 서버(SQL)에 저장, 기존 공정·워커·리스·재시도·취소·Sweeper 재사용 |
| 저장 위치 | 2D 작업의 첫 단계로 편입. 별도 세션 테이블을 만들지 않음 |
| 입력 순서 | 설정·텍스트/이미지 모델을 보완 전에 선택 |
| 보완 모델 | 분석용 텍스트 모델을 그대로 사용 |
| 동시 실행 | 작업당 기준 준비 공정은 한 번에 하나 |
| 후보 상한 | 작업당 8장 |
| 동기 생성 API | 제거 |

## 확인한 현재 구현

- `GenerateSpriteSourceHandler`는 `{{prompt}}`만 렌더링해 시점·형식·타일 설정이 생성 프롬프트에 없다. 결과는 브라우저 상태에만 남고 서버 조회 API가 없다.
- `PipelineJob.SourceImageId`는 필수이며 공정(`Tasks`)은 작업에 속한다. `TaskExecution`이 리스·재시도·취소를, `TaskSweeper`가 유실 회수를 맡는다.
- `SpritePlanParser`·`SpriteRules`는 `sourceBounds`가 0~1 비율이고 `X+W ≤ 1`, `Y+H ≤ 1`이어야 통과한다. `claude-haiku-5-5`는 현재 프롬프트에서 픽셀 좌표를 돌려줬다.

## 흐름

```text
[보완하기]        → 작업 생성(Phase=SourceDraft, SourceImageId=null) + EnhanceSpriteSource
[프롬프트 저장]   → Source.Prompt 갱신(공정 없음)
[다시 보완]       → 설명 갱신 + EnhanceSpriteSource
[기준 이미지 생성] → 현재 프롬프트 snapshot으로 GenerateSpriteSource → 작업 소유 후보 추가
[이 이미지로 분석] → 원본 확정·캔버스 고정 → Phase=Analyzing + AnalyzeSprites(기존)
```

- `SourceDraft`에서만 `SourceImageId`가 null일 수 있다. 다른 단계와 3D 작업은 기존처럼 필수이며, 원본을 읽는 handler는 null을 만나면 공정 실패가 아니라 도메인 위반으로 거부한다.
- 작업 상태: 기준 준비 공정 실행 중이면 `Running`, 사용자 입력 대기면 `PendingReview`.
- 2D 상태 JSON에 `Source { Description, Prompt, PromptRevision, Candidates[{ImageId, Prompt, CreatedAt}], SelectedImageId }`를 추가한다. 기존 행은 `Source` 없음으로 읽는다.
- 보완 결과는 현재 공정일 때만 `Prompt`를 덮어쓴다. 사용자가 그 사이 프롬프트를 저장했으면 늦은 보완 결과를 버린다(`IsCurrentTask`·revision 확인).
- 생성 결과는 작업 소유 이미지로 저장하고 후보로 추가한다. 후보 원본 확정은 `StartSpriteJobHandler`의 검증(12 MiB, 16,777,216픽셀, 실제 MIME, EXIF 방향, 생성 캔버스·Transform 고정)을 공유 함수로 재사용한다.

## API

변경 요청은 기존 2D 명령처럼 `requestId` receipt로 중복을 막고 `expectedRevision`으로 낡은 요청을 거부하며 202 `SpriteAcceptedResponse`를 반환한다.

| 요청 | 본문 | 동작 |
| --- | --- | --- |
| `POST /api/jobs/sprites/drafts` | `requestId`, `description`, `providerConfigId`, `model`, `imageProviderConfigId`, `imageModel`, `settings` | 기준 준비 작업 생성 + 보완 공정 |
| `POST /api/jobs/{id}/sprites/source/enhance` | `requestId`, `expectedRevision`, `description` | 설명 갱신 + 보완 공정 |
| `PUT /api/jobs/{id}/sprites/source/prompt` | `requestId`, `expectedRevision`, `prompt` | 프롬프트 저장 |
| `POST /api/jobs/{id}/sprites/source/generate` | `requestId`, `expectedRevision` | 현재 프롬프트로 후보 1장 생성 |
| `POST /api/jobs/{id}/sprites/source/select` | `requestId`, `expectedRevision`, `imageId` | 후보를 원본으로 확정하고 분석 시작 |

- 검증: 설명 trim 후 1~1000자, 프롬프트 trim 후 1~4000자, 텍스트·이미지 모델 capability는 기존 2D 시작 검증과 같다.
- 기존 오류 코드를 재사용한다. 기준 준비 공정이 실행·대기 중인 enhance·prompt·generate·select는 `SPRITE_BUSY`, `SourceDraft`가 아닌 작업의 기준 준비 명령은 `SPRITE_NOT_READY`다. 후보 8장에서 generate만 새 코드 `SPRITE_SOURCE_LIMIT`을 추가한다. HTTP 상태는 기존 코드의 매핑을 따르고 새 코드는 409다.
- `GET /api/jobs/{id}` 응답의 `sourceImageId`는 nullable이 되고 `sprites.source`가 추가된다. 후보 이미지는 기존 `GET /api/jobs/{id}/sprites/images/{imageId}`로 내려준다.
- 취소는 기존 `POST /api/jobs/{id}/cancel`, 공정 재시도는 기존 `POST /api/jobs/{jobId}/tasks/{taskId}/retry`에 두 공정을 추가한다.
- 제거: `POST /api/uploads/generate`, `GenerateSpriteSourceRequest`, Frontend `generateSpriteSource`·`useGenerateSpriteSource`.

## 프롬프트

- 보완(`LlmOperationKind.EnhanceSpriteSource = 8`, Background 슬롯): 입력 `{{description}}`·`{{settings}}`, 출력 스키마 `{ "prompt": string }`. 시점별 조건(sideView 정면 평행 레이어, topDown 수직 하향, isometric 2:1 다이아몬드)과 형식별 조건(layers는 분리 가능한 깊이 층, tiles는 반복 가능한 타일 구성과 셀 단위 배치)을 넣고, 설명을 지시가 아닌 데이터로 다룬다.
- 기준 이미지 생성: `{{prompt}}`에 더해 `{{settings}}`를 렌더링하고 설정의 시점·형식이 프롬프트 문장보다 우선한다고 명시한다.
- 2D 분석: `sourceBounds`의 x·y·w·h는 원본 너비·높이 대비 0~1 비율이며 픽셀이 아니라고 예시와 함께 적는다. 스키마의 좌표 필드에 같은 `description`을 둔다(Anthropic 지원 키워드).
- 기존 활성 버전은 비활성으로 남기고 새 버전을 시드 migration으로 추가한다. 시드 SQL은 `AND [Category] ...` 스코프 규칙을 따른다.

## 데이터와 migration

- `Jobs.SourceImageId` nullable. 기존 행·FK·삭제 경로는 유지하며 null 원본은 삭제 대상 Blob이 없다.
- 2D 상태 JSON `Source` 추가(schemaVersion 호환 읽기), 새 `TaskKind` 2개는 기존 숫자 뒤에 추가한다.
- `TaskWorkerRegistration`·`ReclaimPlan`에 두 공정을 등록한다. 리스는 보완=일반 텍스트, 생성=이미지 생성 옵션을 따른다.
- `LlmCalls`: 새 호출은 `JobId`·`TaskId`로 기록한다. 기존 `SourceGenerationId` 기록은 과거 데이터로 보존하고 새로 쓰지 않는다.

## 화면

- `/sprites` 프롬프트 모드: 시점·형식·타일·반복 → 텍스트·이미지 모델 → 장면 설명 → [보완하기]. 성공하면 `/sprites/{jobId}`로 이동한다. 이미지 모드는 그대로다.
- `/sprites/{jobId}`의 `SourceDraft`: 설명과 [다시 보완], 보완 프롬프트 편집·[저장], [기준 이미지 생성], 기존 `JobProgressPanel`, 후보 선택 그리드, [이 이미지로 분석]. 공정 중에는 편집·생성·선택을 비활성화하고 저장되지 않은 편집은 덮어쓰지 않는다.
- "이 화면을 유지해 주세요" 안내를 제거한다. 홈 "진행 중" 목록은 기준 준비 작업을 2D 배경으로 표시한다.

## 오류 처리

- 공급자 호출 실패·제한 시간: 기존 공정 재시도 정책, 최종 실패는 공정 실패와 화면 재시도.
- 보완 JSON 위반·이미지 장수·형식 위반: `ProviderBadResponse`로 재시도 분류.
- 원본 확정 검증 실패: 400, 단계·후보 유지.
- 진행 중 명령(`SPRITE_BUSY`)·단계 불일치(`SPRITE_NOT_READY`)·후보 상한(`SPRITE_SOURCE_LIMIT`)은 상태를 바꾸지 않는다.

## 테스트

실제 AI·유료 smoke 없이 Fake 공급자와 Fake API로 검증한다.

- Domain: `SourceDraft` 생성·전이, 원본 null 불변식, 후보 상한, 늦은 보완 결과 무시, revision.
- Application: 다섯 명령의 정상·중복 receipt·충돌·진행 중 거부, 공정 handler의 기록·재시도 분류, 원본 확정 검증 공유.
- API: 계약·상태 코드·`sourceImageId` null 직렬화, 제거된 동기 API 404.
- Migration: 기존 작업 보존, 시드 활성 버전 3종, `SourceImageId` nullable.
- Frontend: API client·`backendParity`·응답 파서, e2e "보완 → 수정 → 생성 2회 → 새로고침 후 복원 → 선택 → 분석 시작"과 진행 중 비활성·오류 표시.
- 실제 모델 확인은 구현 후 사용자 승인 시 1회 수행한다.

## 문서

- `docs/xHuman/backend.md`(2D 기준 준비 행·공정), `frontend.md`(입력·기준 준비 화면), `providers-and-prompts.md`(새 operation·프롬프트), `job-execution.md`(새 공정 등록), `CODEMAP.md` 필요 시.

## 작업 분할

1. 2D 분석 정규화 좌표 프롬프트 개정(독립)
2. 도메인·상태 JSON·migration
3. 보완·생성 공정과 프롬프트 시드
4. 기준 준비 명령 handler·API·조회 응답
5. Frontend 입력·기준 준비 화면·e2e
6. 동기 생성 API 제거와 문서 정리
