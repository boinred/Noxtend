# 2D 배경 원본 준비 단계 설계

2026-10-13 승인된 대화 설계와 독립 설계 검증(Backend·Frontend 각 1회) 결과를 정리한다. 이전 [2D 배경 프롬프트 모드 설계](2026-10-08-2d-background-prompt-mode-design.md)가 제외한 "이탈 후 복원"과 동기 생성 API를 이 설계가 대체한다.

## 목적

- 짧은 장면 설명을 선택한 시점·형식에 맞는 상세 이미지 프롬프트로 AI가 보완하고, 사용자가 확인·수정한 뒤 원본 후보를 생성한다.
- 보완·생성 과정 전체를 SQL에 저장해 새로고침·이탈 후에도 같은 URL과 홈 "진행 중" 목록에서 복원한다.
- Anthropic 모델의 2D 분석이 픽셀 좌표를 돌려줘 `SpriteRules`에서 거부되는 문제를 프롬프트·스키마 안내로 고친다.

성공 기준:

- "80년대 서울 배경으로 하자" + 아이소메트릭·타일 설정에서 보완 프롬프트가 2:1 다이아몬드·타일 조건을 포함하고, 원본 후보 생성 프롬프트에도 설정이 반영된다.
- 보완·생성 공정 실행 중 새로고침해도 진행 상태와 기존 후보가 같은 화면에 보이고, 화면에 보이는 프롬프트로만 생성된다.
- 2D 분석 프롬프트·스키마가 `sourceBounds`를 원본 너비·높이 대비 0~1 비율로 명시한다.

## 범위

포함:

- 2D 작업의 새 첫 단계 `SpritePhase.SourceDraft`(화면 이름 "원본 준비")와 원본 없는 작업 허용
- 새 공정 `EnhanceSpriteSource`(텍스트)·`GenerateSpriteSource`(이미지)와 기존 공정 실행 기반 재사용
- 원본 준비 명령 API 5개, 후보 이미지 조회 API, 작업 조회·목록 응답 확장
- 프롬프트 모드의 [보완하기]와 원본 준비 화면, 홈 "원본 준비 중" 표시
- 시드 프롬프트: 보완(신규), 원본 후보 생성(설정 반영 개정), 2D 분석(정규화 좌표 개정)
- 기존 동기 API `POST /api/uploads/generate`와 Frontend `generateSpriteSource` 제거
- 검증 중 발견한 기존 결함: 분석 재시도 대기 중 단계가 잘못 재계산되는 문제(아래 "기존 결함")

제외:

- 3D 배경 프롬프트 모드
- 원본 준비 중 설정(시점·형식·타일 크기·반복) 변경. 바꾸려면 새 작업을 만든다
- 후보 개별 삭제, 선택하지 않은 후보로 새 작업 시작, 방치된 원본 준비 작업의 자동 만료(취소·삭제로 정리)
- 이미지 모드(업로드)와 기존 작업 결과에서 시작하는 흐름의 변경
- 재시도 예약 지연 작업의 `ObjectDisposedException`(기존 버그, 별도 작업)

## 결정 사항

| 항목 | 결정 |
| --- | --- |
| 보완 방식 | 텍스트 모델이 설명·설정으로 프롬프트를 만들고 사용자가 확인·수정 후 생성 |
| 상태 저장 | 서버(SQL), 기존 공정·워커·리스·재시도·취소·Sweeper 재사용 |
| 저장 위치 | 2D 작업의 첫 단계. 별도 세션 테이블 없음 |
| 입력 화면 | 현재 배치 유지. 설정·텍스트/이미지 모델·설명이 모두 유효할 때만 [보완하기] 활성 |
| 용어 | 단계 "원본 준비", 버튼 "원본 후보 생성", 확정 "이 원본으로 분석". 기존 "기준 이미지"(에셋 기준)와 구분 |
| 미저장 편집 | 생성 요청이 화면의 프롬프트를 함께 보내 저장과 생성을 한 번에 처리 |
| 다시 보완 | 프롬프트가 있으면 확인 후 진행하고 로컬 편집을 버림 |
| 텍스트 모델 | 작업 생성 시 고른 텍스트 공급자·모델을 원본 준비 정보에 저장해 보완·분석에 사용, 사용 시 활성 재검증 |
| 동시 실행 | 작업당 원본 준비 공정은 하나 |
| 후보 상한 | 작업당 8장 |
| 방치 작업 | 홈 "진행 중"에 "원본 준비 중"으로 구분 표시, 정리는 취소·삭제 |
| 동기 생성 API | 제거 |

## 확인한 현재 구현

- `GenerateSpriteSourceHandler`는 `{{prompt}}`만 렌더링해 시점·형식·타일 설정이 생성 프롬프트에 없다. 결과는 브라우저 상태에만 남는다.
- `PipelineJob.SourceImageId`는 필수(FK·인덱스 없음)이며 공정은 작업에 속한다. `TaskExecution`이 리스·재시도·취소를, `TaskSweeper`가 유실 회수를 맡는다. 공정 scope의 `DbContext`는 리스 갱신 루프와 공유되므로 공정 본문은 commit 클로저 밖에서 저장소에 쓰지 않는다.
- 2D 상태는 `Jobs` 테이블 분할 owned 엔터티이며 JSON 열은 `SpriteJsonSerializer` envelope(schemaVersion 1, 미지정 필드 거부)를 쓴다.
- `SpritePlanParser`·`SpriteRules`는 `sourceBounds`가 0~1 비율이어야 통과한다. `claude-haiku-5-5`는 현재 프롬프트에서 픽셀 좌표를 돌려줬다.

## 흐름과 상태

```text
[보완하기]          → 작업 생성(Phase=SourceDraft, SourceImageId=null, Running) + EnhanceSpriteSource
[저장]              → Draft.Prompt 갱신(공정 없음)
[다시 보완]         → 설명 갱신 + EnhanceSpriteSource
[원본 후보 생성]     → 요청 본문의 프롬프트를 저장하고 그 snapshot으로 GenerateSpriteSource → 후보 추가
[이 원본으로 분석]   → 후보를 StoredImage로 복사·검증 → SourceImageId 확정 → Phase=Analyzing + AnalyzeSprites
```

- `SourceDraft`에서만 `SourceImageId`가 null일 수 있다. 원본을 읽는 handler는 null이면 공정을 Fail로 분류한다.
- 원본 준비 정보 `SpriteSourceDraft { Description, Prompt?, TextProviderConfigId, TextModel, Candidates[] }`는 nullable 열 `Jobs.SpriteSourceDraftJson`에 envelope로 저장한다. 후보는 `SpriteSourceCandidate { Id, BlobKey, ContentType, Width, Height, Prompt, CreatedAt }`로 작업 소유 Blob을 가리킨다. 원본 확정 후에도 유지해 프롬프트 모드 출신 작업과 사용한 프롬프트를 보존한다.
- 공정은 `PipelineTask`에 고정 입력(보완: 설명, 생성: 프롬프트 snapshot)을 `Tasks.SourceInputJson`으로 저장한다.
- 작업 생성 시 원본 캔버스를 이미지 모델의 생성 크기(`PickSize`)로 잠정 고정하고 생성·출력 캔버스를 계산한다. 원본 확정 시 실제 크기가 다르면 다시 계산한다.
- 상태 전이: 공정을 계획하는 명령(초안 생성·다시 보완·생성·선택)은 작업을 `Running`으로 만든다. `SourceDraft`에서 공정이 끝나면 활성 공정이 없을 때 `PendingReview`, 있으면 `Running`이다. 공정 최종 실패·폐기·Sweeper 한도 초과도 작업을 Failed로 만들지 않는다.
- 동시 실행 하나: 모든 원본 준비 명령과 공정 결과 반영이 `ReviewRevision`을 올려 `Jobs` 행을 갱신한다. 동시 요청은 RowVersion 충돌로 하나만 성공한다. 두 공정의 재시도는 다른 미종료 원본 준비 공정이 없을 때만 허용한다.
- 현재 공정 판정: 두 공정은 Phase가 `SourceDraft`이고 원본 준비 공정 중 최신 Ordinal일 때만 current다. current가 아닌 결과(취소·회수 뒤 도착 등)는 반영하지 않고 공정은 Succeeded로 끝나며 저장한 Blob을 정리한다.
- 기존 2D 명령(계획 편집·승인·기준 승인·재생성·대상 승인·내보내기)은 `SourceDraft`에서 `SPRITE_NOT_READY`로 거부한다.
- 원본 확정은 `StartSpriteJobHandler`의 검증(12 MiB, 16,777,216픽셀, 실제 MIME, EXIF 방향, 생성 캔버스)을 공유 함수로 재사용하고 후보 Blob을 독립 Blob으로 복사해 `StoredImage`를 만든다.
- 작업 삭제 시 후보 Blob을 함께 지운다. 후보는 다른 작업이 참조할 수 없으므로 공유 판정이 필요 없다.

## 기존 결함

`UpdateSpriteState`는 `Analyzing`에서도 Phase를 재계산해, asset이 없을 때 분석 재시도 대기 중 Phase가 `ExportReady`로 바뀌고 분석이 다시 적재되지 않을 수 있다(정적 추론, 미실행). 원본 확정 후 이 경로를 그대로 타므로 `SourceDraft`와 `Analyzing`을 재계산에서 제외하고 `MaxAttempts=3` 일시 실패 테스트로 확인한다.

## API

변경 요청은 기존 2D 명령처럼 `requestId` receipt로 중복을 막고 `expectedRevision`으로 낡은 요청을 거부하며 202 `SpriteAcceptedResponse`를 반환한다.

| 요청 | 본문 | 동작 |
| --- | --- | --- |
| `POST /api/jobs/sprites/drafts` | `requestId`, `description`, `providerConfigId`, `model`, `imageProviderConfigId`, `imageModel`, `settings` | 원본 준비 작업 생성 + 보완 공정 |
| `POST /api/jobs/{id}/sprites/source/enhance` | `requestId`, `expectedRevision`, `description` | 설명 갱신 + 보완 공정 |
| `PUT /api/jobs/{id}/sprites/source/prompt` | `requestId`, `expectedRevision`, `prompt` | 프롬프트 저장 |
| `POST /api/jobs/{id}/sprites/source/generate` | `requestId`, `expectedRevision`, `prompt` | 프롬프트 저장 + 후보 1장 생성 |
| `POST /api/jobs/{id}/sprites/source/select` | `requestId`, `expectedRevision`, `candidateId` | 원본 확정 + 분석 시작 |
| `GET /api/jobs/{id}/sprites/source/candidates/{candidateId}` | — | 후보 이미지(작업 소유 확인) |

- 검증: 설명 trim 후 1~1000자, 프롬프트 trim 후 1~4000자. 공급자·모델·capability·활성 프롬프트(`EnhanceSpriteSource`, `GenerateSpriteSource`, `AnalyzeSprites`) 검증은 초안 생성 시, 텍스트 모델 활성은 다시 보완·선택 시 재검증한다.
- 오류: 원본 준비 공정이 실행·대기 중이면 `SPRITE_BUSY`, `SourceDraft`가 아니면 `SPRITE_NOT_READY`(둘 다 기존 409), 후보 8장에서 generate는 새 코드 `SPRITE_SOURCE_LIMIT`(409), 원본 확정 검증 실패는 기존 업로드 코드(400).
- `JobResponse.sourceImageId`와 `JobSummaryResponse.sourceImageId`는 nullable이 되고 `SpriteResponse.source`(`description`, `prompt`, `candidates[{ id, prompt, width, height, createdAt }]`)가 추가된다. 목록 요약은 Phase만으로 "원본 준비 중"을 표시한다.
- 취소는 기존 `POST /api/jobs/{id}/cancel`, 공정 재시도는 기존 `POST /api/jobs/{jobId}/tasks/{taskId}/retry`를 쓴다.
- 제거: `POST /api/uploads/generate`, `GenerateSpriteSourceRequest`, `GenerateSpriteSourceHandler`, Frontend `generateSpriteSource`·`useGenerateSpriteSource`.

## 프롬프트

- 보완(`LlmOperationKind.EnhanceSpriteSource = 8`, Background 슬롯): 입력 `{{description}}`·`{{settings}}`, 출력 스키마 `{ "prompt": string }`. 시점별(sideView 정면 평행 레이어, topDown 수직 하향, isometric 2:1 다이아몬드)·형식별(layers 분리 가능한 깊이 층, tiles 반복 타일 구성·셀 배치) 조건을 넣고 설명을 지시가 아닌 데이터로 다룬다.
- 원본 후보 생성: `{{prompt}}`와 `{{settings}}`를 렌더링하고 설정의 시점·형식이 프롬프트 문장보다 우선한다고 명시한다.
- 2D 분석: `sourceBounds`의 x·y·w·h는 원본 너비·높이 대비 0~1 비율이며 픽셀이 아니라고 예시와 함께 적고, 좌표 필드에 같은 `description`을 둔다.
- 기존 seed 메서드·migration은 수정하지 않는다. 신규는 `IF NOT EXISTS` v1, 개정은 비활성화 + `MAX(Version)+1` 삽입(`BackgroundGeneratePromptV3` 패턴).

## 실행 설정

- 보완 공정은 일반 공정 리스(`JobOptions`)와 워커 1개, 원본 후보 생성 공정은 이미지 생성 리스(`GenerationOptions`)와 이미지 생성 워커 그룹을 쓴다.
- 실패 분류: 보완 JSON 위반은 재시도, 이미지 장수·형식 위반은 유료 재호출을 막기 위해 재시도 없이 실패, 공급자 일시 오류는 재시도.

## 화면

- `/2d/background` 프롬프트 모드: 배치는 그대로이며 설명 입력란과 [보완하기]를 둔다. 설정·텍스트/이미지 모델·설명이 모두 유효할 때만 활성. 성공하면 `/2d/background/{jobId}`로 이동한다. 이미지 모드는 그대로다.
- `/2d/background/{jobId}`의 원본 준비 패널: 고정 설정 요약, 설명·[다시 보완](프롬프트가 있으면 확인), 프롬프트 편집·[저장], [원본 후보 생성], 후보 선택 그리드(후보별 사용 프롬프트는 접힘), [이 원본으로 분석]. 주 행동 solid 버튼은 하나(후보 없음: 생성, 후보 선택: 분석). 공정 중 편집·생성·선택 비활성. 첫 보완 실패로 프롬프트가 없으면 직접 입력해 생성할 수 있다.
- 다른 2D 패널(분석·계획 검수·미리보기·프레임·내보내기)은 `sourceDraft`에서 숨긴다. 공정 목록은 current인 원본 준비 공정만 실패·재시도로 보인다. 취소 확인 문구는 단계에 맞춘다.
- 2D 진행 표시는 프롬프트 모드 출신 작업(`source`가 있는 작업)에만 "원본 준비" 단계를 보이고, 단계 위치는 고정 인덱스가 아니라 Phase로 찾는다. 원본 준비 단계 요약은 "후보 n/8".
- 홈 "진행 중" 목록·spotlight는 원본 준비 작업을 "원본 준비 중"으로 표시하고 원본 썸네일 자리는 자리 표시로 대체한다.
- polling: `sourceDraft`에서 활성 공정이 있으면 2초 고정 간격, 없으면 기존 백오프.
- 진행 표시·원본 준비 패널·홈 표시의 외관은 Frontend 지침에 따라 구현 전에 목업 승인을 받는다.

## 오류 처리

- 공급자 실패·제한 시간: 실행 설정의 분류, 최종 실패는 작업을 유지한 채 공정 실패와 재시도로 보인다.
- 원본 확정 검증 실패: 400을 패널에 표시하고 단계·후보를 유지한다.
- `SPRITE_BUSY`·`SPRITE_NOT_READY`·`SPRITE_SOURCE_LIMIT`·revision 충돌은 상태를 바꾸지 않고 화면은 서버 상태로 갱신한다.

## 테스트

실제 AI·유료 smoke 없이 Fake 공급자와 Fake API로 검증한다.

- Domain: `SourceDraft` 생성·상태 전이 네 경로(성공·폐기·최종 실패·회수 한도), 원본 null 불변식, 기존 명령 거부, 후보 상한, 재시도 제한, 캔버스 재계산, `Analyzing` 재시도 대기 중 Phase 유지.
- Application: 다섯 명령의 정상·중복 receipt·충돌·진행 중 거부, 두 공정의 기록·실패 분류·current 아닌 결과 폐기, SQL 동시 generate 2건 중 1건 성공.
- API: 계약·상태 코드·`sourceImageId` null 직렬화, 후보 이미지 소유 확인, 제거된 동기 API 404.
- Migration: 기존 작업 보존, 시드 활성 버전, 새 열.
- Frontend: API client·parity·파서(`sourceDraft` job·summary), polling 간격, e2e "보완 → 수정 후 바로 생성 → 생성 2회 → 새로고침 복원 → 선택 → 분석", 다시 보완 확인 취소 시 요청 0회, 실패 후 이전 실패 행 없음, 홈 원본 준비 표시, 1440×900·390px·dark theme.
- 실제 모델 확인은 구현 후 사용자 승인 시 1회 수행한다.

## 문서

- `docs/xHuman/backend.md`, `frontend.md`, `providers-and-prompts.md`, `job-execution.md`, `CODEMAP.md` 작업 행, `PRODUCT.md`·`DESIGN.md`의 프롬프트 모드 서술.

## 작업 분할

1. 2D 분석 정규화 좌표 프롬프트 개정(독립)
2. 도메인·영속성·응답 nullable·enum parity·기존 결함 수정
3. 보완·생성 공정과 프롬프트 시드
4. (사용자) 진행 표시·원본 준비 패널·홈 표시 목업 승인
5. 원본 준비 명령·API·동기 API 제거
6. Frontend 데이터 계층
7. Frontend 화면·e2e
8. 마무리 검증·문서
