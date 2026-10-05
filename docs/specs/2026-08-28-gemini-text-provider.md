# 미니 스펙: Gemini 텍스트 공급자 추가

## 목표 (한 문장)
Extract/Decompose(텍스트) 단계에서 Gemini를 공급자로 고를 수 있게, `ILlmProvider`를 구현하는 `GoogleProvider`를 추가하고 `LlmProviderFactory`·`ModelCatalog`·`ProviderCapabilities`에 등록한다.

## 입력 → 출력 (형식과 예시 각 1개)
- 입력: `LlmRequest(Context, System, User, Image?, JsonSchema)` — 기존 `AnthropicProvider`/`OpenAiProvider`와 동일한 계약.
- 출력: `LlmResult(RawJson, InputTokens?, OutputTokens?)` — Gemini `generateContent` 응답의 첫 텍스트 파트를 그대로 `RawJson`에 담는다(해석하지 않음, §9.2 원칙 유지).
- 예시: Extract 단계에서 `providerConfigId`가 Google 설정을 가리키면, `GoogleProvider.CompleteAsync`가 `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`를 호출하고 `candidates[0].content.parts[0].text`를 꺼내 돌려준다.

## 이번에 안 하는 것 (제외 범위)
- Gemini로 이미지 생성(`GoogleImageProvider`)은 기존 그대로 — 이번 변경은 텍스트 전용.
- 3D(Mesh) 관련 Tripo/Meshy는 무관.
- 프론트엔드 코드 변경 없음 — `ProviderCapabilities`에 `TextAnalysis`만 추가하면 `useProviders()`가 자동으로 Gemini를 텍스트 드롭다운에 노출한다(이미 capability 기반으로 필터링됨, `apps/frontend/src/app/queries/useProviders.ts:67-69`).
- 기존 Extract/Decompose 프롬프트 문면(SeedPrompts.cs) 변경 없음 — 공급자만 늘리는 변경이라 프롬프트 텍스트는 그대로.

## 예상 함정
1. **구조화 출력 스키마 형식 차이.** Anthropic은 `OutputConfig.Format.Schema`(JSON Schema dict)를 받는 SDK 타입이 있지만, Gemini REST는 `generationConfig.responseMimeType: "application/json"` + `generationConfig.responseSchema`를 받는다. `request.JsonSchema`(string)를 그대로 문자열로 못 박지 말고 `JsonDocument`로 파싱해 `responseSchema`에 실어야 한다 — 문자열을 그대로 넣으면 Gemini가 스키마를 무시하고 자유 형식 텍스트를 낼 수 있다.
2. **이미지 파트 순서.** `GoogleImageProvider`는 응답에서 텍스트가 이미지보다 먼저 올 수 있다고 이미 방어했다(`ExtractImage`의 파트 순회). 텍스트 어댑터도 마찬가지로 `candidates[].content.parts[]`를 순회해서 첫 텍스트 파트를 찾아야 한다 — 첫 파트가 텍스트라고 가정하면 스키마 위반 응답이 섞였을 때 깨진다.
3. **모델 허용목록.** `ImageModels.Google`처럼 텍스트도 "실제 키로 원격 조회 + 검토된 허용목록 교차" 패턴을 따라야 한다(`ModelCatalog.ListAsync`, `apps/backend/Noxtend.Infrastructure/Llm/ModelCatalog.cs:44-49`가 지금 Google을 만나면 예외를 던짐). 허용목록에 넣을 정확한 Gemini 텍스트 모델 id는 구현 시점에 Google `ListModels` 응답으로 재확인한다(agy CLI가 보여준 `gemini-3.7-flash-*` 등은 agy 자체 별칭일 수 있어 그대로 못 믿음).
4. **재시도 판정.** `AnthropicProvider.IsTransient`는 SDK 예외 타입명으로 판별하지만, Gemini는 raw HTTP라 `GoogleImageProvider.IsTransient`처럼 상태 코드(5xx, 429)로 판별해야 한다 — 패턴을 그대로 가져오되 텍스트 어댑터에서 중복 정의하지 말고 공유 헬퍼로 뺄지 결정 필요.
5. **타임아웃.** `AnthropicProvider`는 SDK 클라이언트에 20분 타임아웃을 직접 건다. Gemini는 `LlmProviderFactory`가 만드는 공유 `HttpClient`("llm" named client)를 쓰므로, 그 클라이언트에 이미 걸린 타임아웃 설정을 재사용하면 되고 어댑터에서 따로 걸 필요는 없어야 한다(§2.2 "공급자 호출은 10분 이상 걸릴 수 있다"와 일치하는지 기존 `HttpClient` 등록 설정 확인 필요).

## 검증 방법
- 단위 테스트: `GoogleProvider.CompleteAsync`에 대해 (a) 정상 응답에서 텍스트 파싱, (b) 텍스트 파트가 뒤에 오는 응답, (c) 후보 없음/거절 응답에서 `ProviderBadResponseException`, (d) 5xx/429에서 `IsTransient=true`인 `ProviderCallFailedException` — 각각 `Noxtend.Tests`에 케이스 추가.
- `LlmProviderFactory`가 `ProviderKind.Google`을 받으면 더 이상 "지원하지 않는 공급자입니다" 예외를 던지지 않고 `GoogleProvider`를 반환하는 테스트.
- `ModelCatalog.ListAsync`가 Google 설정에 대해 허용목록 교차 결과를 반환하는 테스트(Anthropic/OpenAI 목록 테스트와 동일 패턴).
- `ProviderCapabilities.Supports(ProviderKind.Google, ProviderCapability.TextAnalysis)`가 `true`로 바뀌는 테스트.
- 통합: `Llm:UseFake=false` 상태에서 실제 Gemini 키로 Extract 1건을 돌려 `/health`처럼 실 API 왕복 확인 (CLAUDE.md 배포 검증 원칙 — "전부 문구만 바꾼 가설이라 실 API 재검증이 필요" 관례를 여기도 적용, 이번엔 어댑터 자체가 바뀌므로 실 API 검증이 특히 중요).

## 승인
- [x] 사용자 승인 (승인 전 구현 금지)
