# 공급자와 프롬프트

이 문서는 LLM·이미지 AI 공급자 호출 경로와 프롬프트·호출 내역의 연결을 설명한다. 지속 규칙은 [Backend 개발 지침](../../apps/backend/AGENTS.md), 구현 상태는 아래 코드와 테스트가 정본이다.

## 호출 경로

### 텍스트 분석

`RunTaskHandler`는 활성 `PromptSnapshot`을 가져오고 `IStage` 변수를 렌더링한 뒤 `ILlmProviderFactory`로 공급자를 만든다. `LlmRequest`에는 작업 맥락, 시스템·사용자 프롬프트, 선택 이미지, JSON Schema가 들어간다. 공급자 어댑터는 외부 응답을 `LlmResult.RawJson`으로 돌려주며, 단계별 해석·검증은 `IStage.Interpret`이 맡는다.

주요 경로:

- 흐름: `apps/backend/Noxtend.Application/Pipeline/RunTaskHandler.cs`, `Application/Stages/IStage.cs`, `Application/Stages/Stages.cs`
- 계약: `apps/backend/Noxtend.Domain/Ports/ILlmProvider.cs`, `IPromptCatalog.cs`
- 공급자 생성·자격 증명: `apps/backend/Noxtend.Infrastructure/Llm/LlmProviderFactory.cs`, `ProviderCredentialResolver.cs`
- 어댑터: `apps/backend/Noxtend.Infrastructure/Llm/OpenAiProvider.cs`, `AnthropicProvider.cs`, `GoogleProvider.cs`
- 설정·장식: `apps/backend/Noxtend.Infrastructure/InfrastructureServiceCollectionExtensions.cs`의 `AddLlm`

### 2D 배경 분석

`AnalyzeSprites`는 TaskKind=7, LlmOperationKind=5의 독립 분석 operation이며 Background 전용 프롬프트다. `RunSpriteAnalysisTaskHandler`는 settings·sourceCanvas를 camelCase JSON 데이터로 렌더하고 접수 시 실제 codec 형식과 일치 확인된 업로드 또는 기존 결과의 독립 복사 이미지를 첨부한다. 업로드 MIME 불일치는 접수 단계에서 거부하며 분석 공급자에 잘못된 MIME을 보내지 않는다. `PromptTemplate.Render`는 원래 템플릿만 한 번 치환하여 삽입한 notes의 `{{...}}`를 재확장하거나 미치환 변수로 거부하지 않는다. 원래 템플릿의 누락 변수는 계속 실패한다.

`SeedPrompts.AnalyzeSprites()`와 `SeedSpriteAnalyzePrompt` migration은 정확한 시점·유형과 대상 schema를 등록한다. 기존 AnalyzeSprites/Background 슬롯이 있으면 운영자 내용을 보존하며 기존 3D·평가 프롬프트는 변경하지 않는다. `FakeLlmProvider`도 같은 schema를 반환하고 관리자 격자·편집·Frontend operation 라벨은 새 분석을 포함한다. 단가가 없는 모델 비용은 기존 ModelPriceBook의 null 규칙을 따른다.

### 2D 배경 이미지 생성

`GenerateSpriteSource`는 Background 전용이며 변수 `{{prompt}}`로 참조 없는 업로드용 기준 이미지 생성을 위한 프롬프트를 정의한다. `SeedPrompts.GenerateSpriteSource()`와 `SeedSpriteSourcePrompt` migration은 기존 운영자 슬롯을 보존한다.

`RunSpriteGenerationTaskHandler`는 `LlmOperationKind.GenerateSprite=6`·Background 전용 활성 프롬프트를 사용한다. `SeedPrompts.GenerateSprite()`와 `SeedSpriteGeneratePrompt` migration은 기존 운영자 슬롯을 보존한다. 허용 변수는 settings·asset·frame·sourceCanvas·outputCanvas이며, asset은 `SpriteFrameInput.Plan` snapshot이다. frame JSON에는 index·count·phase·BaseImageId와 고정 GenerationCanvas·anchor·transform을 전달한다. 이름·FPS를 나중에 편집해도 접수한 입력은 바뀌지 않는다. `ViewDirection`은 사용하지 않는다.

모든 sprite 요청은 Background를 opaque 또는 transparent로 명시하고 기존 Factory·Recording·RateLimitGate를 통과한다. raw 응답은 실제 PNG와 image/png 및 고정 GenerationCanvas 크기가 일치해야 하며, 디코딩·크기·알파 오류는 재시도하지 않는다. 기존 입력 MIME은 원본 참조에 유지하고 출력은 픽셀 검증 후 PNG로 정규화한다. FakeImageProvider의 기본 성공·지연 응답은 GenerateSprite와 SourceGenerationId가 있는 원본 생성의 요청 크기에 맞는 RGBA PNG를 만들며 명시적인 Returning 오류 fixture는 변환하지 않는다.

### 이미지 생성

`RunGenerationTaskHandler`가 활성 Generate 프롬프트를 조회하고 `GenerationStage` 변수·참조 이미지로 `ImageRequest`를 만든다. 이미지 어댑터 응답을 `GenerationStage.Validate`로 검증하고 Blob 저장 후 작업 상태에 반영한다. 성공 확정 전에 검증과 저장을 끝내는 순서를 유지한다. Generate 프롬프트는 카테고리별로 두며 Background와 Character는 공용 `RotationContractTemplate`을 조합하고, 두 카테고리 모두 같은 궤도 규약으로 방향을 정의한다(오른쪽 뷰는 정면이 이미지 왼쪽 가장자리를 향한다). 배경 시드는 `SeedPrompts.BackgroundGenerateV3()`이며 migration `BackgroundGeneratePromptV3`로 활성화한다.

주요 경로:

- 흐름·검증: `apps/backend/Noxtend.Application/Generation/RunGenerationTaskHandler.cs`, `GenerationStage.cs`
- 계약: `apps/backend/Noxtend.Domain/Ports/IImageProvider.cs`
- Factory·어댑터·기록: `apps/backend/Noxtend.Infrastructure/Image/`
- 호출·응답 검증: `apps/backend/Noxtend.Tests/Application/RunGenerationTaskHandlerTests.cs`, `ImageProviderUsageTests.cs`

### 스프라이트 모델·요청 계약

`ProviderModel.Sprite`와 모델 목록 응답의 optional `sprite`는 `SpriteImageCapabilities(SupportsTransparency, Sizes)`를 전달한다. null은 지원 미확인이며 Google과 기존 `gpt-image-2`에는 sprite 지원을 추정하지 않는다. `ImageModels`의 `gpt-image-2.5-sunburst`와 Fake 두 모델에만 투명 및 초기 생성 크기 `1024x1024 / 1536x1024 / 1024x1536 / 1536x768 / 768x1536 / 1536x864 / 864x1536`를 명시한다. 실 목록은 기존 remote ID 교차를 유지하므로 허용목록에 있어도 해당 자격 증명의 목록에 없으면 반환하지 않는다.

`ImageRequest.Background`가 명시된 요청은 sprite 계약을 사용한다. OpenAI JSON·multipart 양쪽에 지정 size와 `background=opaque|transparent`, `output_format=png`를 전송하며 quality는 기존 medium을 유지한다. 모델의 sprite capability·배경 값·허용 크기가 확인되지 않으면 HTTP 전 비재시도 오류로 거부한다. Google은 명시 sprite 요청을 거부하고 기존 Background=null의 1:1·1K 요청을 유지한다. 현재 catalog의 두 GPT image 모델은 기존 3D 요청에서도 `response_format`을 전송하지 않고 공통 JSON·edit builder에서 `output_format=png`를 사용한다. Background=null이면 background는 추가하지 않으며 quality=medium·n=1·size·base64 응답 읽기는 유지한다. PNG 응답 형식만으로 실제 투명을 판단하지 않으며 아래 픽셀 검증을 거친다.

`SpriteRules.GenerationCanvas`는 확인된 크기 중 출력 비율과의 로그 비율 차이가 가장 작은 크기를 선택한다. 동률은 catalog 순서를 유지하며 `Transform`의 공통 contain 배치를 따른다. `ImageCallContext`는 기존 PartId GUID를 보존하고 sprite에는 null을 허용하며 Kind 기본값은 Generate다. 기록 데코레이터는 작업 호출의 Kind를 기존 operation 매핑에 넘기고 TaskId·프롬프트 버전·모델 및 배경·크기 metadata를 남긴다. `ImageCallContext.ForSourceGeneration`은 JobId·TaskId·PartId가 null인 원본 생성 맥락을 만들고, `LlmCallContext.ForSourceGeneration`을 통해 GenerateSpriteSource operation과 SourceGenerationId로 기록한다. sprite asset/index는 Tasks에서 조회하며 LlmCalls에 중복 열을 추가하지 않는다.

- 계약·선택: `apps/backend/Noxtend.Domain/Ports/IImageProvider.cs`, `IModelCatalog.cs`, `Noxtend.Domain/Sprites/SpriteRules.cs`
- catalog·전송·기록: `apps/backend/Noxtend.Infrastructure/Image/ImageModels.cs`, `OpenAiImageProvider.cs`, `GoogleImageProvider.cs`, `RecordingImageProvider.cs`
- wire 매핑: `apps/backend/Noxtend.Api/Contracts/ProviderResponse.cs`의 `ProviderModelResponse`
- 가짜 HTTP·기록·capability 회귀: `apps/backend/Noxtend.Tests/Application/SpriteProviderRequestTests.cs`, `ImageModelCatalogTests.cs`

### 스프라이트 이미지 검증·정규화

`IImageTranscoder.InspectSpriteAsync`는 MIME 문자열 대신 PNG·JPEG·WebP 디코딩 결과를 검사하고 SpriteImageInfo.ContentType으로 실제 MIME을 반환한다. 입력 바이트는 비seek 스트림에서도 지정 상한+1바이트까지만 읽고, 64비트 픽셀 곱과 최대 16,777,216픽셀을 비트맵 할당 전에 확인한다. 잘린 디코딩·전체 투명 이미지·지원하지 않는 형식은 `ProviderBadResponseException`으로 실패한다. 원본 업로드는 호출자가 12 MiB를 지정하며 생성 응답·PNG 결과는 32 MiB 상한을 적용한다.

`NormalizeSpriteAsync`는 EXIF 방향을 적용한 뒤 전달된 `SpriteCanvas`·`SpriteTransform`으로 배치한다. 프레임별 콘텐츠 경계를 자르거나 중심을 다시 계산하지 않는다. 투명 요구는 패딩·다이아몬드 마스크를 적용하기 전 실제 원본 알파로 확인한다. 출력은 알파를 유지한 PNG이며 다이아몬드 셀 밖은 투명하고 마스크 뒤 전체가 빈 결과는 거부한다. 입력 스트림은 호출자가 소유하고 반환 스트림은 position 0에서 읽고 닫는다. 취소는 `OperationCanceledException`으로 전파한다.

- 계약·값: `apps/backend/Noxtend.Domain/Ports/IImageTranscoder.cs`, `Noxtend.Domain/Sprites/SpriteTypes.cs`의 `SpriteImageInfo`
- 구현: `apps/backend/Noxtend.Infrastructure/Mesh/SkiaImageTranscoder.Sprites.cs`; EXIF 좌표 변환은 기존 `SkiaImageTranscoder.cs`와 공유
- 픽셀·자원 상한 회귀: `apps/backend/Noxtend.Tests/Infrastructure/SpritePixelTests.cs`, 기존 `SkiaImageTranscoderTests.cs`

## 프롬프트·공급자 규칙

- 현재 공급자와 기능 조합은 `apps/backend/Noxtend.Domain/Provider/ProviderCapability.cs`, 텍스트 `LlmProviderFactory`, 이미지 `ImageProviderFactory`, 각 모델 목록에서 확인한다. 공급자 이름만으로 텍스트·이미지·3D 지원 여부를 가정하지 않는다.
- 키 복호화는 Infrastructure의 `ProviderCredentialResolver` 안에 둔다. Application과 Domain에는 설정 id·모델만 전달한다. API 응답은 `apps/backend/Noxtend.Api/Contracts/ProviderResponse.cs` 마스킹 경계를 지킨다.
- 공통 `Llm:UseFake` 설정을 텍스트·이미지·3D Factory가 공유한다. 설정 파일의 기본값만 보고 실행 모드를 단정하지 말고 실제 호스트 설정과 Factory 등록을 확인한다.
- 활성 프롬프트 선택과 카테고리별 기본값 fallback은 `apps/backend/Noxtend.Infrastructure/Llm/TuningPortAdapters.cs`의 `TuningPromptCatalog`에서 확인한다. 프롬프트 버전 id는 호출 맥락과 내역에 연결되므로 요청·응답 처리 과정에서 버리지 않는다.
- 공급자 출력의 JSON 파싱·도메인 검증은 Application 단계에 둔다. 어댑터는 공급자 프로토콜에서 공통 계약으로 옮기는 데 필요한 파싱만 수행한다.
- `OpenAiImageProvider`는 성공 HTTP 응답의 JSON 문법·필수 data·shape·base64 파싱 오류를 원문과 내부 예외 없이 안전한 `ProviderBadResponseException`으로 정규화한다. 생성과 edit가 같은 파싱 경계를 사용하며 원본 생성 API는 이를 `ProviderBadResponse` 502 봉투로 반환하고 업로드하지 않는다. Recorder는 실패 호출 한 건과 알 수 없는 사용량(null)을 남긴다. HTTP 실패의 transient 분류·취소 전파·유효 응답의 바이트·장수·사용량·rate-limit 헤더는 기존 계약을 유지한다.
- `RewriteDescriptions`는 `RunTaskHandler`가 `IStage.BuildJsonSchema`를 호출해 현재 재작성 대상 이름을 JSON Schema `enum`으로 제한한다. 응답 해석은 정확한 대상 이름을 우선하고, 미확인 이름 하나와 누락 대상 하나가 남을 때만 1:1 보정한다. 중복·누락·모호한 응답은 실패시키며 Domain 검증은 그대로 유지한다. 관련 회귀는 `RewriteDescriptionsStageTests.cs`에서 확인한다.
- `ProviderHttp`의 OpenAI 응답 분류는 텍스트·이미지 양쪽에서 429의 insufficient_quota type/code와 문서화된 credit_balance_exhausted·organization_spend_limit_exceeded·project_spend_limit_exceeded·organization_usage_limit_exceeded code를 즉시 실패로 분류한다. 일반 rate limit·잘못된/없는 오류 본문은 기존 상태 코드 기준 재시도를 유지하고 원문 message는 예외에 넣지 않는다.
- 취소 토큰, transient 오류 분류, 재시도 한도는 Provider 예외와 `RunTaskHandler`·`TaskExecution`에서 함께 확인한다. 인증 실패·크레딧 부족·잘못된 설정을 무조건 재시도하지 않는다.

## 호출 기록·비용·민감 정보

- `EfLlmCallRepository`는 IDbContextFactory가 만든 별도 context를 scoped 수명 동안 소유하고 Dispose한다. AddAsync 후 SaveChangesAsync 계약을 유지하며 작업 리스 ReloadAsync의 추적 초기화와 호출 기록 저장을 분리한다.
- `RecordingLlmProvider`와 `RecordingImageProvider`는 Factory가 씌우는 데코레이터다. 두 경로는 `ILlmCallRecorder`와 `TuningLlmCallRecorder`를 통해 같은 호출 저장소에 기록된다. 기록 실패는 호출 자체를 실패시키지 않고 경고 로그로 남긴다.
- `LlmCall.SourceGenerationId`는 작업 없는 원본 생성 상관관계이며 JobId·TaskId·SimilarityEvaluationId는 null이다. `AddSourceGenerationCalls` migration과 `LlmCallConfiguration`의 check constraint는 작업+공정, 작업+평가, 원본 생성 중 하나만 허용한다. JobId null 원본 생성 기록은 작업 삭제 대상이 아니며 전체 통계에만 포함한다. 공급자 응답 이후의 검증 실패는 `Succeeded`와 실제 사용량을 바꾸지 않는다.
- 텍스트 호출 기록에는 렌더된 프롬프트·응답 문자열이 포함될 수 있다. 텍스트 요청에 첨부한 이미지는 이름·형식·크기·SHA-256만 기록한다. 이미지 생성 호출은 프롬프트·크기·참조 개수와 응답 형식·크기만 남기며 입력·출력 이미지 바이트는 저장하지 않는다. 텍스트 입력도 개인정보·비밀값 포함 여부를 확인한다.
- API 키, 인증 헤더, 원본 외부 요청 본문, 이미지 bytes/base64를 로그나 호출 기록에 추가하지 않는다. 실패 기록은 원문 예외 전체 대신 현재의 정규화 규칙을 따른다.
- 새 `gpt-image-2.5-sunburst` 단가는 미확인으로 seed하지 않으며 실제 calculator 비용은 null이다. `SeedModelPricesTests`와 SQL `ModelPriceMigrationTests`는 기존 이미지 모델의 등록 비용과 신규 모델의 unknown 비용을 각각 검증한다.
- 비용은 `Noxtend.Tuning.Domain/Call/ModelPriceBook.cs`와 등록 단가를 따른다. 실측 토큰·이미지 수를 확인할 수 없거나 단가가 없을 때 0으로 꾸며내지 않는다.
- 키 암호화·Data Protection key ring 보존·응답 마스킹은 [Backend 개발 지침](../../apps/backend/AGENTS.md)과 [배포 지침](../../deploy/AGENTS.md)을 따른다.

## 프롬프트 변경·테스트

- 2D view×kind별 입력·시점 재구성·반복 경계·고정 anchor/canvas·루프 검수 기준과 호출 수는 [실행 기록](../superpowers/plans/2026-10-06-2d-background-sprites-execution.md)에 모은다. 정적 대상당 1장, 루프 대상당 기준 포함 4·8장, 묶음 최대 64프레임은 계획한 이미지 슬롯 수다. 공급자 재시도·명시적 재생성은 추가 호출/비용을 만들 수 있으며 64를 최종 과금 호출 상한으로 설명하지 않는다. metadata 편집과 패키징은 AI를 호출하지 않는다. 현재 실 AI 품질은 미확인이며 유료 생성·smoke는 별도 사용자 요청 범위에서만 실행한다.
- 프롬프트 버전·모델·출력 품질을 비교할 때는 [평가 절차](prompt-evaluation.md)를 따른다. 오프라인 계약 회귀와 실제 모델 품질 평가를 구분하고 기존 골든 샘플·호출 내역·사람 판정을 재사용한다.
- 기본 프롬프트는 `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs`에서 확인한다. 이미 적용된 DB 프롬프트는 seed 코드가 항상 덮어쓴다고 가정하지 말고 관련 migration과 시드 테스트를 함께 본다.
- 프롬프트 변수·스키마·응답 의미가 바뀌면 `PromptTemplate`, `IStage` 구현과 관련 `PromptVariableInjectionTests`, `PromptCompositionTests`, 단계 테스트를 확인한다.
- `OpenAiProviderTests.cs`, `GoogleProviderTests.cs`, `ImageProviderUsageTests.cs`의 가짜 HTTP handler 패턴을 따른다. 변경한 어댑터에 기존 격리 테스트가 없으면 해당 프로토콜을 가짜 응답으로 검증한다. 프롬프트 저장·기록은 Fake·테스트 저장소로 검증하며, 일반 회귀에서 유료 실 API를 호출하지 않는다.
- 권장 테스트 위치: `apps/backend/Noxtend.Tests/Application/`의 handler·provider 테스트, `Noxtend.Tests/Infrastructure/`의 seed·migration·저장소 테스트. 정확한 실행 명령과 Docker·smoke 조건은 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다.

## 확인 기준과 미확인

- 마지막 확인: 2026-10-08, revision `d2cad8d`. 코드 지도 도입 때 문서의 코드 경로와 상대 링크 존재를 자동 대조했다. 서술된 규칙 전체를 코드와 다시 대조하지는 않았다.
- 미확인: 실 공급자의 출력 품질과 응답 형식 변화, 단가 미등록 모델의 실제 비용. 일반 회귀는 Fake·가짜 HTTP 응답만 사용한다.
