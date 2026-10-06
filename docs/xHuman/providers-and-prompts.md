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

### 이미지 생성

`RunGenerationTaskHandler`가 활성 Generate 프롬프트를 조회하고 `GenerationStage` 변수·참조 이미지로 `ImageRequest`를 만든다. 이미지 어댑터 응답을 `GenerationStage.Validate`로 검증하고 Blob 저장 후 작업 상태에 반영한다. 성공 확정 전에 검증과 저장을 끝내는 순서를 유지한다.

주요 경로:

- 흐름·검증: `apps/backend/Noxtend.Application/Generation/RunGenerationTaskHandler.cs`, `GenerationStage.cs`
- 계약: `apps/backend/Noxtend.Domain/Ports/IImageProvider.cs`
- Factory·어댑터·기록: `apps/backend/Noxtend.Infrastructure/Image/`
- 호출·응답 검증: `apps/backend/Noxtend.Tests/Application/RunGenerationTaskHandlerTests.cs`, `ImageProviderUsageTests.cs`

## 프롬프트·공급자 규칙

- 현재 공급자와 기능 조합은 `apps/backend/Noxtend.Domain/Provider/ProviderCapability.cs`, 텍스트 `LlmProviderFactory`, 이미지 `ImageProviderFactory`, 각 모델 목록에서 확인한다. 공급자 이름만으로 텍스트·이미지·3D 지원 여부를 가정하지 않는다.
- 키 복호화는 Infrastructure의 `ProviderCredentialResolver` 안에 둔다. Application과 Domain에는 설정 id·모델만 전달한다. API 응답은 `apps/backend/Noxtend.Api/Contracts/ProviderResponse.cs` 마스킹 경계를 지킨다.
- 공통 `Llm:UseFake` 설정을 텍스트·이미지·3D Factory가 공유한다. 설정 파일의 기본값만 보고 실행 모드를 단정하지 말고 실제 호스트 설정과 Factory 등록을 확인한다.
- 활성 프롬프트 선택과 카테고리별 기본값 fallback은 `apps/backend/Noxtend.Infrastructure/Llm/TuningPortAdapters.cs`의 `TuningPromptCatalog`에서 확인한다. 프롬프트 버전 id는 호출 맥락과 내역에 연결되므로 요청·응답 처리 과정에서 버리지 않는다.
- 공급자 출력의 JSON 파싱·도메인 검증은 Application 단계에 둔다. 어댑터는 공급자 프로토콜에서 공통 계약으로 옮기는 데 필요한 파싱만 수행한다.
- `RewriteDescriptions`는 `RunTaskHandler`가 `IStage.BuildJsonSchema`를 호출해 현재 재작성 대상 이름을 JSON Schema `enum`으로 제한한다. 응답 해석은 정확한 대상 이름을 우선하고, 미확인 이름 하나와 누락 대상 하나가 남을 때만 1:1 보정한다. 중복·누락·모호한 응답은 실패시키며 Domain 검증은 그대로 유지한다. 관련 회귀는 `RewriteDescriptionsStageTests.cs`에서 확인한다.
- 취소 토큰, transient 오류 분류, 재시도 한도는 Provider 예외와 `RunTaskHandler`·`TaskExecution`에서 함께 확인한다. 인증 실패·크레딧 부족·잘못된 설정을 무조건 재시도하지 않는다.

## 호출 기록·비용·민감 정보

- `RecordingLlmProvider`와 `RecordingImageProvider`는 Factory가 씌우는 데코레이터다. 두 경로는 `ILlmCallRecorder`와 `TuningLlmCallRecorder`를 통해 같은 호출 저장소에 기록된다. 기록 실패는 호출 자체를 실패시키지 않고 경고 로그로 남긴다.
- 텍스트 호출 기록에는 렌더된 프롬프트·응답 문자열이 포함될 수 있다. 텍스트 요청에 첨부한 이미지는 이름·형식·크기·SHA-256만 기록한다. 이미지 생성 호출은 프롬프트·크기·참조 개수와 응답 형식·크기만 남기며 입력·출력 이미지 바이트는 저장하지 않는다. 텍스트 입력도 개인정보·비밀값 포함 여부를 확인한다.
- API 키, 인증 헤더, 원본 외부 요청 본문, 이미지 bytes/base64를 로그나 호출 기록에 추가하지 않는다. 실패 기록은 원문 예외 전체 대신 현재의 정규화 규칙을 따른다.
- 비용은 `Noxtend.Tuning.Domain/Call/ModelPriceBook.cs`와 등록 단가를 따른다. 실측 토큰·이미지 수를 확인할 수 없거나 단가가 없을 때 0으로 꾸며내지 않는다.
- 키 암호화·Data Protection key ring 보존·응답 마스킹은 [Backend 개발 지침](../../apps/backend/AGENTS.md)과 [배포 지침](../../deploy/AGENTS.md)을 따른다.

## 프롬프트 변경·테스트

- 프롬프트 버전·모델·출력 품질을 비교할 때는 [평가 절차](prompt-evaluation.md)를 따른다. 오프라인 계약 회귀와 실제 모델 품질 평가를 구분하고 기존 골든 샘플·호출 내역·사람 판정을 재사용한다.
- 기본 프롬프트는 `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs`에서 확인한다. 이미 적용된 DB 프롬프트는 seed 코드가 항상 덮어쓴다고 가정하지 말고 관련 migration과 시드 테스트를 함께 본다.
- 프롬프트 변수·스키마·응답 의미가 바뀌면 `PromptTemplate`, `IStage` 구현과 관련 `PromptVariableInjectionTests`, `PromptCompositionTests`, 단계 테스트를 확인한다.
- `OpenAiProviderTests.cs`, `GoogleProviderTests.cs`, `ImageProviderUsageTests.cs`의 가짜 HTTP handler 패턴을 따른다. 변경한 어댑터에 기존 격리 테스트가 없으면 해당 프로토콜을 가짜 응답으로 검증한다. 프롬프트 저장·기록은 Fake·테스트 저장소로 검증하며, 일반 회귀에서 유료 실 API를 호출하지 않는다.
- 권장 테스트 위치: `apps/backend/Noxtend.Tests/Application/`의 handler·provider 테스트, `Noxtend.Tests/Infrastructure/`의 seed·migration·저장소 테스트. 정확한 실행 명령과 Docker·smoke 조건은 [검증 절차](../../.agents/skills/noxtend-workflow/references/verification.md)를 따른다.
