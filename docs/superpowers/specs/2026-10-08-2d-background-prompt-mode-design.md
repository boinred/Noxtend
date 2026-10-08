# 2D 배경 프롬프트 모드 설계

- 작성일: 2026-10-08
- 상태: 2026-10-08 대화 설계 승인, 문서 검토 대기
- 기준: `main`의 `be3a134`
- 대상: 2D 배경 입력 화면, 업로드 API, LLM 호출 기록, 3D 배경 입력 탭 순서

## 목적

2D 배경을 이미지 없이 장면 설명으로 시작한다. 사용자는 프롬프트 모드에서 설명을 입력해 기준 이미지를 생성하고, 생성 결과 중 하나를 골라 기존 업로드 이미지와 같은 경로로 2D 작업을 시작한다. [2D 배경 설계 §13](2026-10-06-2d-background-sprites-design.md#13-후속-텍스트-입력)의 `설명 → 기준 이미지 생성 → 기존 이미지 입력 접수`를 구현한다.

2D 작업의 분석·계획·프레임·검수·내보내기와 시작 API 계약은 바꾸지 않는다.

## 범위

포함:

- 2D 입력 화면에 `프롬프트 모드 | 이미지 모드` 탭과 프롬프트 패널 추가
- 동기 생성 API `POST /api/uploads/generate` 추가, 결과를 업로드로 저장
- 새 프롬프트 종류 `LlmOperationKind.GenerateSpriteSource`와 v1 seed
- 작업 없는 이미지 호출을 `LlmCalls`에 기록하는 상관관계 추가
- 공용 `ModeTabs` 탭 순서를 프롬프트 먼저로 변경

제외:

- 생성 결과의 페이지 이탈 후 복원
- 한 번의 요청으로 여러 장 생성
- `requestId` 기반 중복 생성 방지
- 생성 비용을 이후 2D 작업의 호출 목록에 연결
- 선택하지 않은 생성 이미지 정리; 업로드 후 사용하지 않은 이미지와 같은 상태로 남는다
- 3D 배경 프롬프트 모드 동작; 기존 `PromptModePanel` 껍데기를 유지한다

## 확인한 현재 구현

- `apps/frontend/src/features/screens/sprites/SpriteInput.tsx`는 모드 없이 `file ?? readSpriteSource(params)`를 원본으로 쓴다. 제출 시 `file`이면 `useUpload`로 올린 `uploadId`, 아니면 `sourceJobId`·`sourceGeneratedImageId`를 `startSpriteJob`에 넘긴다.
- 같은 화면의 설정 섹션이 이미지 공급자·모델을 고르며 `sprite.supportsTransparency`와 양수 `sizes`가 있는 모델만 허용한다.
- `background/ModeTabs.tsx`는 `StudioMode = 'image' | 'prompt'`와 `이미지 모드`, `프롬프트 모드` 순서의 탭을 정의한다. `BackgroundStudioScreen`이 기본값 `'image'`로 사용한다.
- `IImageProvider.GenerateAsync`는 참조가 빈 `ImageRequest`를 텍스트 생성으로 처리한다. OpenAI는 `/v1/images/generations`로 보낸다. 현재 이 경로를 쓰는 제품 흐름은 없다.
- `ImageCallContext`는 `JobId`·`TaskId`가 필수이고 `RecordingImageProvider`가 `LlmCallContext.ForTask`로 변환한다.
- `LlmCalls.JobId`는 필수이고 `CK_LlmCalls_ExactlyOneCorrelation`이 `TaskId`와 `SimilarityEvaluationId` 중 정확히 하나를 요구한다. 호출 조회는 작업별 `GET /api/jobs/{jobId}/calls`와 전체 `GET /api/calls/stats`다.
- `CreateUploadHandler`가 `UploadRules.Validate`로 형식·크기를 검사하고 Blob·`StoredImage`를 저장한다. 디코딩·픽셀 상한은 `StartSpriteJobHandler`가 시작 시 검사한다.
- `StartSpriteJobHandler`는 이미지 공급자·모델·활성 프롬프트를 검증하고 `SpriteRules.GenerationCanvas`로 모델의 확인된 크기를 고른다.
- API는 Azure App Service에서 실행된다. 플랫폼 요청 상한은 약 230초다.

## 화면과 흐름

```text
[프롬프트 모드 | 이미지 모드]
 프롬프트: 장면 설명 → [기준 이미지 생성] → 결과 썸네일 누적 → 하나 선택
 이미지:   기존 드롭존·기존 결과 안내
 ── 공유 ──
 시점·결과 유형·타일·공급자·모델 → [2D 분석 시작]
```

- 탭은 공용 `ModeTabs`를 쓴다. `TABS` 순서를 `프롬프트 모드`, `이미지 모드`로 바꾼다. 3D 배경의 기본 탭은 `'image'`로 유지한다.
- 2D 기본 탭은 새 진입이면 `prompt`, 유효하거나 유효하지 않은 `sourceJobId`·`sourceGeneratedImageId` 쿼리가 있으면 `image`다.
- 프롬프트 패널은 `sprites/` 아래 새 컴포넌트다. 3D `PromptModePanel`과 공유하지 않는다.
  - `장면 설명` textarea, trim 기준 1~1000자. 범위 밖이면 생성 버튼을 비활성화한다.
  - `[기준 이미지 생성]`은 설정 섹션에서 고른 이미지 공급자·모델을 사용한다. 둘 중 하나가 없으면 비활성화한다.
  - 진행 중에는 버튼을 비활성화하고 "생성 중에 페이지를 떠나면 결과를 다시 볼 수 없습니다"를 표시한다.
  - 성공 결과는 `/api/uploads/{id}/content` 썸네일로 누적한다. 새 결과를 자동 선택하고 사용자가 다른 결과를 다시 고를 수 있다.
  - 실패하면 서버 오류 메시지를 표시하고 설명과 기존 결과를 유지한다.
- 탭 전환은 각 모드의 설명·결과·선택·파일을 유지한다. `[2D 분석 시작]`은 현재 탭 입력만 쓴다.
  - 프롬프트: 선택한 결과의 `uploadId`. 업로드를 다시 하지 않는다.
  - 이미지: 현재 동작 그대로.
- 시작 가능 조건의 원본 부분만 현재 탭 기준으로 바꾸고 나머지 설정 조건은 유지한다.

## API

`POST /api/uploads/generate` → `201 UploadResponse`

```json
{ "requestId": "guid", "prompt": "string", "imageProviderConfigId": "guid", "imageModel": "string" }
```

- 알 수 없는 필드는 400으로 거부한다.
- 처리 순서:
  1. `prompt` trim 후 1~1000자 검사.
  2. 공급자 존재·활성, 이미지 모델 카탈로그 존재, `Sprite.SupportsTransparency`와 유효한 `Sizes` 검사. 오류 코드는 `StartSpriteJobHandler`의 이미지 공급자·모델 코드와 같다.
  3. `GenerateSpriteSource`·`Background` 활성 프롬프트 검사. 없으면 `PromptNotActive`.
  4. 크기 선택: `Sizes` 중 너비 ≥ 높이인 것의 최대 면적, 없으면 전체 최대 면적.
  5. `PromptTemplate.Render`로 `{{prompt}}`를 채우고 참조 없이 `ImageBackground.Opaque`로 호출한다. 호출 전후로 기존 `rateLimitGate`를 거친다.
  6. `ImageCount == 1` 검사 후 바이트를 `CreateUploadHandler`로 저장한다. 저장 파일명은 `sprite-prompt`다.
- 공급자 예외는 `ProviderCallFailed`, 장수·업로드 규칙 위반은 `ProviderBadResponse`로 반환한다. 둘 다 502이며 업로드를 만들지 않는다.
- 디코딩·픽셀 상한은 다른 업로드처럼 2D 시작 시 `StartSpriteJobHandler`가 검사한다.

Frontend는 `infra/api/uploadApi.ts`에 `generateSpriteSource`, `app/queries/useUpload.ts` 옆에 `retry: false` mutation을 추가한다. `StartSpriteJobInput`은 바꾸지 않는다.

## 프롬프트

- `LlmOperationKind.GenerateSpriteSource = 7`. 기존 값은 유지한다. `LlmOperation.FromTask`에는 매핑하지 않는다.
- migration으로 `Background` 카테고리 v1을 활성 상태로 seed한다. 변수는 `{{prompt}}` 하나다.
- 고정 지시: 게임용 2D 배경의 단일 기준 장면, 텍스트·UI·워터마크·프레임 없음, 사용자 설명의 화풍·시점·색을 따른다.
- 관리자 프롬프트 화면이 새 종류를 표시·버전 관리하는지 확인하고 필요한 목록만 보강한다.

## 호출 기록

- `LlmCall.JobId`를 `Guid?`로 바꾸고 `Guid? SourceGenerationId`를 추가한다. 값은 요청의 `requestId`다.
- `CK_LlmCalls_ExactlyOneCorrelation`을 `TaskId`, `SimilarityEvaluationId`, `SourceGenerationId` 중 정확히 하나로 바꾼다. `JobId IS NULL`은 `SourceGenerationId IS NOT NULL`과 동치로 제한한다.
- `LlmCallContext.ForSourceGeneration(sourceGenerationId, promptVersionId, providerConfigId, model)`와 같은 의미의 `ImageCallContext` 생성 경로를 추가한다. `RecordingImageProvider`가 두 경우를 각각 변환한다. 기존 factory와 호출부는 바꾸지 않는다.
- 전체 통계에 포함되고 작업별 호출 목록에는 나오지 않는다. 작업 삭제는 `JobId`가 null인 기록을 지우지 않는다.

## 테스트

Backend는 Fake 공급자를 사용하며 유료 호출을 하지 않는다.

- 생성 handler: 정상 저장과 `LlmCalls` 기록, 참조 없음·크기·불투명 요청, 각 검증 실패 코드, 공급자 실패·잘못된 응답의 502·실패 기록·업로드 없음.
- `SpriteApiTests`: 생성 endpoint의 201 계약, 알 수 없는 필드 400.
- `RecordingLlmProviderTests`: 원본 생성 상관관계 기록과 기존 규칙 회귀.
- migration 테스트: v1 seed와 `{{prompt}}`, `LlmCalls` nullable·새 check constraint. 기존 `*PromptMigrationTests` 형식을 따른다.
- `JobDeletionPersistenceTests`: 작업 삭제 후 원본 생성 기록 유지.

Frontend:

- 새 `src/infra/api/uploadApi.test.ts`: `generateSpriteSource` 요청 본문과 오류 전달.
- `tests/e2e/sprites-static.spec.ts`와 `spriteFakeApi.ts`: 기본 탭, 생성·누적·선택·시작 `uploadId`, 실패 표시와 설명 유지, 기존 결과 진입의 이미지 탭, 탭 전환 후 현재 탭 입력만 사용.
- `tests/e2e/background-studio-actions.spec.ts`: 탭 순서와 3D 기본 이미지 탭.

검증 명령은 루트 [AGENTS.md](../../../AGENTS.md#빌드테스트-명령)의 Backend·Frontend 전체 회귀다.

## 문서

구현과 같은 커밋에서 갱신한다.

- [Backend 안내](../../xHuman/backend.md): 생성 API·handler·`LlmCalls` 상관관계
- [Frontend 안내](../../xHuman/frontend.md): 2D 입력 탭·프롬프트 패널·API 함수
- [공급자·프롬프트 안내](../../xHuman/providers-and-prompts.md): `GenerateSpriteSource`와 작업 없는 호출 기록
- [PRODUCT.md](../../../PRODUCT.md): 2D 배경 시작 경로에 장면 설명 추가
- [2D 배경 설계 §13](2026-10-06-2d-background-sprites-design.md#13-후속-텍스트-입력): 이 문서 링크

## 실행

구현은 AGENTS.md의 Subagent-driven 방식으로 진행한다. 실제 공급자 호출 확인은 별도 요청 범위에서만 수행한다.
