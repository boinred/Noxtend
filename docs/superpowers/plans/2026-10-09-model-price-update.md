# 모델·단가 업데이트 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development`. 사용자와 정한 실행 방식을 유지하며 작업별 새 구현자·독립 리뷰어를 사용한다. 아래 체크박스는 구현 실행 전이며 완료 표시가 아니다.

**Goal:** 5개 공급자의 모델·가격 변경을 수집해 공급자별로 필터링하고, 검토한 항목만 기존 비용을 보존하며 적용한다.

**Architecture:** 기존 공급자 조회·단가 관리 계층을 재사용하고 공식 자료 수집, 서버 보관 변경안, 원자적 선택 적용을 연결한다. 수집 후보는 실행 가능한 모델 목록과 구분하며 관리자 화면의 기존 단가 목록과 변경안에 공급자 필터를 제공한다.

**Tech Stack:** .NET 10·EF Core SQL Server·기존 HTTP/JSON 계약, React 19·TanStack Query·기존 UI·Vitest·Playwright. HTML 문서 해석에는 아래에서 지정한 parser 한 개만 추가한다.

**Spec:** [모델·단가 업데이트 설계](../specs/2026-10-09-model-price-update-design.md)

**기준:** 2026-10-09, `main`의 `1c230beab48f7192d5bd39fa576dc227c819d32f`. 계획 작성만 요청됐으며 코드 구현·실험·DB 적용·유료 호출은 시작하지 않았다. 이 계획은 진행자용이고 A/B 참가자에게 그대로 전달하지 않는다.

## Global Constraints

- 대상: OpenAI·Anthropic·Google의 현재 텍스트/이미지 영역, Tripo·Meshy의 3D. 사용자가 선택한 흐름은 버튼 수집 → 변경안 확인 → 선택 적용이다.
- 기존 2D 화면·진행 패널을 바꾸지 않는다. 신규 모델 발견은 실행 호환성·투명 출력·크기의 승인과 다르다.
- 기존 수동 단가 CRUD·시드와 저장값을 보존한다. 수집 단가만 비소급으로 추가하며 미래 공식 시행일보다 먼저 적용하지 않는다.
- Meshy 계정별 USD 환산을 전역 단가에 적용하지 않는다. 표현 불가 요금·미확인 가격은 이유를 표시하고 적용을 보류한다.
- 키는 Infrastructure 경계에 두고 고정 공식 URL만 조회한다. 수집 중 DB transaction을 열지 않으며 일반 검증은 저장 응답/Fake만 사용한다.
- JSON은 `System.Text.Json`, HTML은 `AngleSharp 1.8.4`로 계획한다. [공식 변경 기록](https://github.com/AngleSharp/AngleSharp/blob/devel/CHANGELOG.md)에서 릴리스를 확인했다. 실행 준비에서 실제 package restore를 확인하고 양쪽 버전을 동일하게 고정한다. 브라우저·JS 실행·CSS 확장·LLM 추출기를 추가하지 않는다.
- `karpathy-guidelines`·`tdd-cycle` 적용, C# 기존 스타일 유지, 관련 문서와 코드 같은 커밋. 전체 작업 트리 stage·push·merge·배포는 이 계획의 완료 조건이 아니다.
- Task 1은 공통 준비, Task 2–6은 각 팀에서 순서대로 수행, Task 7은 비교·통합이다. 구현자와 리뷰어는 겹치지 않으며 같은 파일을 동시에 수정하지 않는다.

## Review Focus

- 공급자 미분류·같은 모델 ID의 다른 공급자: 표시 필터가 비용 키를 암묵적으로 바꾸거나 단가를 합치지 않아야 함 — Task 2·5·6.
- 새 날짜 버전·미래 시행 행·legacy alias: 신규 등록 때문에 과거 비용이나 과거 미등록이 바뀌지 않아야 함 — Task 2·5.
- 성공 응답 유실 후 만료·동시 다른 요청: 최초 적용 결과 재사용, 다른 본문 충돌, 중복·부분 저장 차단 — Task 5.
- 공식 표의 단위/등급 변경·3D 옵션·계정별 환산: 모르는 값을 합리적으로 보이는 숫자로 축약하지 않아야 함 — Task 3·4.
- 선택 후 필터 변경·느린 재조회·필터된 빈 목록: 숨겨진 항목 적용 차단, 선택 초기화 안내, 실패를 빈 결과로 숨기지 않음 — Task 6.

## 공통 계약과 파일 책임

아래 신규 파일·타입은 제안이며 현재 존재하는 구현으로 해석하지 않는다. 실제 기존 경로는 계획 작성 시 확인했다.

| 책임 | 계약 |
| --- | --- |
| 가격 메타데이터 | `ModelPrice`에 `ProviderKind? Provider`, `bool AllowHistoricalFallback`, `string? SourceEvidenceJson` 추가. legacy 기본값은 `null/true/null`, 수집 행은 명시 공급자·`false`·근거 JSON |
| 가격 후보 | 신규 `Tuning.Domain/Prices/PriceUpdateTypes.cs`: `PriceUpdateCandidate`는 ID, 공급자, 정확한 모델 ID, 영역/작업, 요금 조건, 기존 값 fingerprint와 currentTerms, 수집 terms, 출처 배열과 providerConfigIds, 변경 종류, 실행 지원, 적용 불가 이유 보유 |
| 후보 단가 | `PriceUpdateTerms`는 기존 7개 가격 입력값을 보유: input/output, longContextFrom/longInput/longOutput, perImage, 공식 시행일(nullable). credit·환산 근거는 `PriceEvidence`에 별도 보관 |
| 수집 결과 | `ProviderCollectionResult`는 config ID·kind, 상태 `success/partial/failed`, 모델/가격 수집 실패 구분, 수집 시각, 모델 목록 완전성 `complete/partial/failed`, 후보 배열. `PriceEvidence`는 URL·확인 시각·SHA-256·조건·credit/작업·USD/credit의 확인값 |
| 서버 변경안 | `PriceUpdatePreview`: ID, 생성/만료 UTC, 공급자 결과와 중복 제거한 top-level candidates. 공급자 결과는 candidateIds로 후보를 참조한다. 유효시간 30분. 가격·근거·기존 비교값은 서버 snapshot에서 읽음 |
| 수집 port | `IPriceUpdateSource.CollectAsync(Guid providerConfigId, CancellationToken ct) → Task<ProviderCollectionResult>`. 공급자 분기는 Infrastructure 한 구현에서 처리하고 supplier별 parser는 순수 함수로 분리 |
| 저장 port | `IPriceUpdateRepository.SavePreviewAsync(PriceUpdatePreview, ct)`, `GetPreviewAsync(Guid, ct)`, `ApplyAsync(PriceApplyRequest, DateTimeOffset now, ct) → Task<Result<PriceUpdateReceipt>>`. 원자성은 SQL 구현 책임, 검증 규칙은 Domain 함수로 공유 |
| 적용 입력 | `PriceApplyRequest(requestId, previewId, candidateIds, effectiveFrom?)`. `null`은 즉시 적용. 정렬한 후보 ID와 UTC 적용 입력으로 요청 fingerprint 생성 |
| 적용 결과 | `PriceUpdateReceipt(requestId, previewId, appliedAt, items[])`; 각 item은 candidateId·priceId·확정 effectiveFrom. 재전송도 같은 결과 반환 |
| 수집 HTTP | `POST /api/prices/update-previews`, 본문 `{ providerConfigIds: string[] }`, 성공 200 preview. 입력 대상 중복 제거, 빈 배열·존재하지 않거나 비활성인 설정은 400 |
| 적용 HTTP | `POST /api/prices/update-previews/{id}/apply`, 본문 `{ requestId, candidateIds, effectiveFrom: string|null }`, 성공 200 receipt. client 가격·URL은 입력 계약에 없음 |
| 오류 | 신규 `PriceUpdateInvalid` 400, `PriceUpdateNotFound` 404, `PriceUpdateExpired/PriceUpdateConflict/PriceUpdateRequestConflict` 409. 공급자 조회 실패는 preview의 개별 결과이며 빈 성공 후보로 변환하지 않음 |
| Frontend | `createPriceUpdatePreview(input, signal?)`, `applyPriceUpdate(previewId, input, signal?)`; `useCreatePriceUpdatePreview()`, `useApplyPriceUpdate()`는 자동 retry 없음 |
| 필터 | `ProviderPriceFilter = 'all' | ProviderKind | 'unknown'`. 기존 단가와 preview의 필터 상태는 독립. 표시용 순수 필터이며 HTTP·수집 대상·단가 변경 없음 |

`PriceUpdateCandidate`의 변경 종류는 `newModel/priceChanged/unchanged/priceUnknown/notInCatalog`, 실행 지원은 `supported/unverified/unsupported`로 고정한다. 적용 여부는 별도 `blockedReason`으로 표현해 신규 모델 발견과 실행 허용을 혼동하지 않는다. 가격 출처는 항상 plain text/링크로 렌더하고 HTML을 실행하지 않는다. `notInCatalog`는 확인된 공급자의 기존 단가 모델이 완전히 수집된 목록에 없다는 뜻이며 폐기 확정이 아니다. 적용 불가로 표시하고 기존 단가·설정·작업을 보존한다.

## Task 1: 비교 실행 자료와 공통 검증 준비

**Files:** 신규 `docs/evals/model-price-update/brief.md`, `docs/evals/model-price-update/fixtures/manifest.json`, 같은 fixtures 디렉터리의 공급자별 원문 파일, `docs/evals/model-price-update/acceptance.md`; 공통 실행 검사로 신규 `apps/backend/Noxtend.Tests/Api/ModelPriceUpdateAcceptanceTests.cs`, `ModelPriceUpdateAcceptanceHost.cs`, `apps/frontend/tests/e2e/model-price-update-acceptance.spec.ts`. 공통 host에 필요한 기존 test csproj/API 진입점 변경도 측정 전 공통 준비에 포함한다. 참가자 외부 보관 결과는 `.codex`의 이 작업용 산출물 디렉터리를 사용하고 저장소에 로그·키를 넣지 않는다.

**Interfaces:** 입력은 승인된 Spec과 기준 커밋. 출력은 source URL·수집 시각·SHA-256이 있는 동일 fixture 묶음, 구현 위치를 노출하지 않는 요구사항, 평가 기준이다.

- [ ] 공식 공개 자료를 같은 시점에 저장하고 각 공급자의 정상 모델/가격, 미래 가격, 단위/구조 변경 사례를 준비한다. 키가 필요한 목록은 문서화된 계약의 합성 응답으로 표시하고 실 계정 응답인 척하지 않는다. 원문 HTML fixture는 헤더·등급·단위 관계를 보존한다.
- [ ] 정상 fixture의 기대 후보·단가·보류 이유를 사람이 근거와 대조해 `manifest.json`에 기록한다. 가격 숫자를 이 계획의 기억에서 채우지 않는다. parser 내부 함수명에 의존하는 검사는 공통 합격 검사로 사용하지 않는다.
- [ ] 평가자가 실행할 공통 HTTP 검사는 `WebApplicationFactory` 기반 test host에서 실제 API·일회용 SQL DB·원문 fixture를 반환하는 outbound HTTP handler를 연결한다. 필요한 `Microsoft.AspNetCore.Mvc.Testing`은 기존 ASP.NET 버전에 맞춰 restore 후 양쪽에 동일하게 고정한다. 가격 수집기 자체를 Fake로 바꾸지 않고 네트워크 경계만 대체하며 운영 API에 임의 URL 입력을 추가하지 않는다. 테스트는 HTTP 계약과 저장 결과만 관찰하고 참가자 내부 parser 이름에 의존하지 않는다. host의 구현별 연결 차이는 평가자가 기록하며 결과 판단 코드는 수정하지 않는다.
- [ ] 공통 검사는 기존 단가 사전 등록 → 수집 → 선택 적용 → 재전송/충돌/만료 → 단가 재조회 시나리오와 아래 누락·중복·실패 요약 사례를 고정한다. `dotnet test apps/backend/Noxtend.Tests/Noxtend.Tests.csproj --filter FullyQualifiedName~ModelPriceUpdateAcceptanceTests`와 `pnpm --filter @nextend/frontend test:e2e tests/e2e/model-price-update-acceptance.spec.ts`를 양쪽에 동일하게 실행한다. UI 검사는 Fake 응답 기준이며 실제 HTTP/SQL 검사와 결과를 구분한다. 측정 전 기대값·테스트 해시·케이스 수를 고정하고 신규 기능 미구현으로 실패하는 것도 확인한다.
- [ ] neutral brief에는 제품 흐름·필터 규칙·HTTP 계약·합격 기준·공급자 자료만 넣는다. 이 계획의 파일 경로, 진행자용 코드 지도, 해법, 이전 에이전트 보고는 제외한다.
- [ ] 양쪽 사본의 기준 코드·fixture 해시·런타임을 일치시킨다. 사본 준비와 restore는 측정 전 완료하고 공유 작업 트리에서 A/B를 실행하지 않는다. 관련 지침 노출과 xHuman 접근 기록의 수집 가능 여부를 먼저 확인한다.
- [ ] 공통 fixture/요구사항/평가 테스트와 실행 준비만 별도 커밋한다. 양쪽에 동일 커밋을 제공하고 새 참가자로 시작한다. 코드를 만드는 Task 2–6의 구체적 분해는 각 팀이 brief에서 작성하되 기능 범위·상한·리뷰 기회는 고정한다.

## Task 2: 공급자 메타데이터·비소급 가격·공통 타입

**Files:** 수정 `apps/backend/Noxtend.Tuning.Domain/Call/{ModelPrice,ModelPriceBook}.cs`, `Noxtend.Tuning.Application/Prices/PriceHandlers.cs`, `Noxtend.Api/Contracts/TuningResponses.cs`, `Noxtend.Infrastructure/Persistence/Configurations/TuningConfigurations.cs`; 신규 `Noxtend.Tuning.Domain/Prices/PriceUpdateTypes.cs`, `Noxtend.Tuning.Domain/Ports/IPriceUpdateSource.cs`, `IPriceUpdateRepository.cs`; 수정 `apps/frontend/src/domain/tuning/{types,usage}.ts`, `features/screens/admin/PricesScreen.tsx`. 이하 `Noxtend.*` 상대 경로는 `apps/backend/` 기준이다.

**Test:** 기존 `Noxtend.Tests/Domain/ModelPriceBookTests.cs`, `Infrastructure/ModelPriceMigrationTests.cs`, Frontend `domain/tuning/{prices,usage}.test.ts`. migration 이름 `ModelPriceUpdateMetadata`와 생성되는 snapshot을 같은 작업에 포함한다.

**Interfaces:** 기존 생성/수정 입력의 후행 optional provider는 구형 요청과 호환한다. `allowHistoricalFallback`와 근거는 서버 소유이며 수동 편집으로 수입 행의 비소급 속성을 바꾸지 않는다. `ModelPriceDraft`는 응답형 전체 Omit 대신 실제 편집 필드만 명시한다.

- [ ] 기존 행은 fallback=true, 수집 행은 false일 때 시행일 전후 비용·미등록을 검증하는 실패 테스트를 작성한다. 정확한 새 날짜 ID가 미래 단가만 가져도 이전 alias 행을 가리지 않는 사례를 포함한다.
- [ ] `ModelPriceBook.Lookup`은 모델별 유효 시각을 먼저 평가하고 사용 가능한 가장 구체적인 매칭을 선택한다. 과거 소급 대상은 legacy 행뿐이며 미래 수집 행만 있으면 null이다. 기존 날짜 접미사 매칭을 일반 변종 접두사 매칭으로 넓히지 않는다.
- [ ] 응답에 nullable provider·fallback·근거를 추가한다. 기존 등록 모델의 공급자는 현재 seed/검증된 목록에 있는 **정확한 ID 매핑**만 migration으로 채우고 사용자 모델은 null 유지한다. `(Model, EffectiveFrom)` unique는 그대로 두고 공급자 충돌은 적용 규칙에서 차단한다.
- [ ] Frontend 구형 응답 누락은 provider=null/fallback=true로 해석한다. `ModelPriceGroup.current`를 nullable로 바꾸고 미래 수집 행만 있는 그룹은 `예정`으로 표시한다. `modelPriceHint`, 관리자 현재 배지, 3D `meshPriceAt` 호출부를 함께 반영한다.
- [ ] `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~ModelPriceBookTests|FullyQualifiedName~ModelPriceMigrationTests'`와 `pnpm --filter @nextend/frontend test src/domain/tuning/prices.test.ts src/domain/tuning/usage.test.ts` 통과 후 독립 리뷰·지적 수정·관련 문서와 scoped commit.

## Task 3: 텍스트·이미지 공식 자료 수집

**Files:** 신규 `Noxtend.Infrastructure/Llm/PriceUpdates/{PriceUpdateSource,PriceSourceHttp,OpenAiPriceSource,AnthropicPriceSource,GooglePriceSource}.cs`; 수정 `Noxtend.Infrastructure/Noxtend.Infrastructure.csproj`, `InfrastructureServiceCollectionExtensions.cs`. 신규 `Noxtend.Tests/Infrastructure/{PriceSourceHttpTests,TextImagePriceSourceTests}.cs`; test csproj에 공통 fixture 접근을 명시한다.

**Interfaces:** Task 2의 `IPriceUpdateSource` 구현. 각 supplier의 `Parse(Guid configId, IReadOnlyList<PriceSourceDocument> documents) → ProviderCollectionResult`는 네트워크를 호출하지 않는다. `PriceSourceDocument(Url, Role, Body, CollectedAt)`는 Infrastructure 내부 record이고 Role은 models/pricing/modelDetail이다. JSON 모델 목록의 page token/has_more 규약은 해당 공식 자료를 따른다.

- [ ] 저장한 실제 HTML/문서 구조와 합성 모델 목록으로 신규·중복·unknown 가격·Standard/Batch 혼재 테스트를 먼저 작성한다. 기존 실행 허용목록에 없는 모델도 발견 결과에 남기는지 확인한다.
- [ ] JSON은 표준 parser, HTML은 AngleSharp의 DOM parser만 사용한다. 외부 자원 loader·script 실행 없이 문서 제목/모델 ID/표 헤더/단위/행을 연결한다. 정규식 하나로 전체 페이지의 첫 가격을 읽지 않는다.
- [ ] 고정 URL, 요청별 timeout 15초, 응답 8 MiB, 목록 최대 20페이지를 적용한다. 상한 초과·반복 page token·손상 JSON/표는 명시적 부분 실패이며 잘린 목록을 완전 성공으로 표시하지 않는다. redirect는 자동 추적하지 않고 허용된 공식 URL 변화는 소스 레지스트리에서 검토한다.
- [ ] `ProviderCredentialResolver`를 재사용하되 탐색 요청은 기존 10분 목록 캐시를 우회한다. 원격 후보 수집과 기존 runtime capability 확인을 분리하고, 기존 스튜디오 목록을 무분별하게 확장하지 않는다. 한 요청에서 같은 공급자의 공개 가격 문서는 재사용한다.
- [ ] `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~PriceSourceHttpTests|FullyQualifiedName~TextImagePriceSourceTests|FullyQualifiedName~ImageModelCatalogTests'`로 401/429/5xx·timeout·취소·pagination·키 비노출을 검증한다. 독립 리뷰 후 관련 지도와 scoped commit.

## Task 4: Tripo·Meshy 수집과 변경안 판정

**Files:** 신규 `Noxtend.Infrastructure/Llm/PriceUpdates/{TripoPriceSource,MeshyPriceSource}.cs`, `Noxtend.Tuning.Domain/Prices/PriceUpdateRules.cs`; 수정 Task 3의 `PriceUpdateSource.cs`. 신규 `Noxtend.Tests/Infrastructure/MeshPriceSourceTests.cs`, `Noxtend.Tests/Domain/PriceUpdateRulesTests.cs`.

**Interfaces:** `PriceUpdateRules.Compare(IReadOnlyList<ProviderCollectionResult> collected, IReadOnlyList<ModelPrice> current) → IReadOnlyList<ProviderCollectionResult>`는 기존 값 fingerprint와 적용 가능 여부를 채운 결과를 반환한다. 지문에는 해당 모델 매칭에 관여하는 전체 행 ID·시행일·단가·provider·fallback을 정규화해 포함한다. 3D parser는 Task 3과 같은 Parse 계약을 사용한다.

- [ ] Meshy OpenAPI enum 및 공식 가격 HTML, Tripo 모델/가격 HTML fixture로 모델·작업·텍스처·geometry 조건을 읽는 실패 테스트를 작성한다. `latest`를 특정 버전으로 추정하지 않는 사례를 포함한다.
- [ ] credit/작업과 USD/credit를 별도 값으로 보존한다. 계정 종속 환산, 누락 조건, 현재 계산기로 표현 불가한 조건을 보류한다. 모든 공급자 결과가 항상 보류인 stub로 이 작업을 완료하지 않는다. 최소한 명확한 지원 요금과 명확한 보류 사례를 각각 증명한다.
- [ ] 기존 3D 어댑터의 실제 요청 옵션과 가격 조건을 대조한다. 지원되지 않는 새 프로토콜·옵션은 발견 결과에는 남기고 실행 지원 확인 필요로 표시한다. 고정 실행 모델과 잔액 조회를 신규 모델 목록 조회인 것처럼 사용하지 않는다.
- [ ] 같은 공급자의 여러 설정에서 나온 후보는 공급자 종류 + 정확한 모델 ID + 영역/작업 + 정규화한 요금 조건으로 묶는다. 설정별 수집 성공/실패는 별도로 유지하고 동일 단가·공식 시행일이면 후보 하나에 출처와 관찰한 config ID를 합친다. 값이나 시행일이 다르면 어느 하나를 고르지 않고 충돌로 보류한다. 서로 다른 조건의 후보가 동일 `(Model, EffectiveFrom)` 저장 키를 요구하면 함께 적용하지 않는다. 두 설정에서 같은 후보를 수집해도 한 행만 저장되는 경우와 상충 요금의 전체 거부를 검증한다.
- [ ] 선택한 공급자의 모델 목록이 모든 페이지에서 정상 수집됐을 때만 확인된 공급자의 기존 단가 모델과 정확한 ID로 비교해 `notInCatalog`를 만든다. 같은 공급자의 여러 설정 중 하나라도 실패/부분 수집이면 누락 판정을 보류하고, 정상 목록은 합집합으로 비교한다. 미분류 기존 행은 제외한다. 가격표 실패와 목록 실패를 구분하고 목록 미노출만으로 모델 폐기·권한 상실을 단정하지 않는다. 정상 미노출/부분 수집/401/서로 다른 계정 목록 사례에서 오판과 기존 데이터 변경이 없는지 검증한다.
- [ ] 값이 동일하면 unchanged/선택 불가, 다른 공급자의 동일 ID나 공급자 미분류 기존 행과의 불명확한 매칭은 blocked로 반환한다. 출처/요금 등급만 다른 동일 숫자를 근거 없이 같은 정책으로 합치지 않는다.
- [ ] `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~MeshPriceSourceTests|FullyQualifiedName~PriceUpdateRulesTests|FullyQualifiedName~MeshModelCatalogTests'` 통과 후 독립 리뷰·관련 지도·scoped commit.

## Task 5: 서버 변경안 저장·원자적 적용·HTTP 계약

**Files:** 신규 `Noxtend.Tuning.Domain/Prices/{PriceUpdatePreview,PriceUpdateApplication}.cs`, `Noxtend.Tuning.Application/Prices/PriceUpdateHandlers.cs`, `Noxtend.Infrastructure/Persistence/Repositories/EfPriceUpdateRepository.cs`, `Configurations/PriceUpdateConfiguration.cs`, `Noxtend.Api/Contracts/PriceUpdateContracts.cs`; 수정 `Noxtend.Infrastructure/Persistence/NoxtendDbContext.cs`, `InfrastructureServiceCollectionExtensions.cs`, `Noxtend.Api/Controllers/PricesController.cs`, `Contracts/ApiResults.cs`, `Noxtend.Domain/Common/Result.cs`. migration `PriceUpdatePreviews`와 snapshot 포함.

**Test:** 신규 `Noxtend.Tests/Application/PriceUpdateHandlerTests.cs`, `Infrastructure/PriceUpdatePersistenceTests.cs`, `Api/PriceUpdatesControllerTests.cs`.

**Interfaces:** `CreatePriceUpdatePreviewHandler.HandleAsync(IReadOnlyList<Guid> ids, ct) → Task<Result<PriceUpdatePreview>>`; `ApplyPriceUpdateHandler.HandleAsync(PriceApplyRequest, ct) → Task<Result<PriceUpdateReceipt>>`. 서버 시각은 주입한 `TimeProvider` 사용. DTO는 공통 계약 표 그대로이며 enum wire 값은 기존 provider 표기와 일치한다.

- [ ] preview 조회가 운영 단가를 바꾸지 않는 것, 공급자별 부분 실패, 적용 입력 변조·중복 ID·다른 request body 거부 테스트를 먼저 작성한다. 대상 설정 ID는 중복 제거 후 1–25개, 수집 전체 상한은 120초로 고정하고 미수집 공급자를 성공으로 표시하지 않는다.
- [ ] `PriceUpdatePreviews`에 ID·CreatedAt·ExpiresAt·schemaVersion=1의 typed payload JSON을 저장한다. 적용 이력 테이블은 RequestId PK·PreviewId·request fingerprint·receipt JSON·AppliedAt을 보유한다. 단가 행의 근거 JSON은 preview 정리와 독립적으로 남긴다. 스케줄러/자동 삭제는 추가하지 않는다.
- [ ] SQL `Serializable` transaction에서 성공 request 재조회 → 본문 동일성 → 미적용 snapshot 만료 → 현재 단가 지문 재확인 → 도메인 검증 → 선택 단가와 receipt 저장 순서를 구현한다. 재전송은 만료 전후 모두 저장 결과를 반환한다. 동시 unique 충돌은 새 context로 승자 결과를 읽고 동일 요청이면 반환, 그 외는 409; 다른 DB 오류를 성공으로 숨기지 않는다.
- [ ] 즉시 적용은 각 후보의 `max(now, officialEffectiveFrom)`으로 확정한다. 명시 시각이 현재/공식 시행일 하한보다 이르면 400이다. 선택 묶음 하나라도 stale·blocked이면 전체 거부한다. 현재값과 동일해진 후보를 다시 새 행으로 넣지 않는다. 서로 다른 candidate ID가 같은 가격 행을 대상으로 하는지도 transaction 안에서 재검증해 후보 중복으로 인한 일부 저장을 차단한다.
- [ ] HTTP/DI와 기존 `{ data, error }`를 연결한다. 취소는 전파하고 snapshot 성공 후 apply 실패를 수집 실패와 혼동하지 않는다. `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~PriceUpdateHandlerTests|FullyQualifiedName~PriceUpdatePersistenceTests|FullyQualifiedName~PriceUpdatesControllerTests'`에서 실제 SQL 경쟁/rollback과 response 유실 재전송을 검증한 후 독립 리뷰·지도·scoped commit.

## Task 6: 관리자 업데이트 화면과 공급자 필터

**Files:** 이 작업의 상대 경로는 `apps/frontend/` 기준이다. 신규 `src/domain/tuning/priceUpdates.ts`, `src/infra/api/priceUpdateApi.ts`, `src/app/queries/usePriceUpdates.ts`, `src/features/screens/admin/PriceUpdatePanel.tsx`; 수정 `src/domain/tuning/types.ts`, `src/infra/api/tuningApi.ts`, `src/app/queries/{useTuning,keys}.ts`, `src/features/screens/admin/PricesScreen.tsx`, `src/features/screens/admin/adminStyles.ts`. 신규 `src/domain/tuning/priceUpdates.test.ts`, `src/infra/api/priceUpdateApi.test.ts`, `tests/e2e/price-updates.spec.ts`, `tests/e2e/priceUpdatesFakeApi.ts`; 수정 기존 `tests/e2e/fakeApi.ts`, `tests/e2e/prices-admin.spec.ts`.

**Interfaces:** `filterModelPrices(prices, filter)`와 `filterPriceUpdateCandidates(candidates, filter)`는 입력을 변경하지 않는 표시 함수다. `PriceUpdatePanel`은 독립 preview 필터·선택을 소유하고, 부모 `PricesScreen`은 기존 단가 필터를 소유한다. 화면은 `app/queries`를 통해 접근한다.

- [ ] `all/openai/anthropic/google/tripo/meshy/unknown` 필터, null/누락 provider, 빈 결과를 순수 테스트로 고정한다. 문자열 접두사 추정 없이 정확한 provider 값으로 필터링한다. 기존 공급자 라벨을 재사용한다.
- [ ] 기존 단가 표에 라벨 `공급자`의 select를 추가하고 필터 후 groupPricesByModel을 적용한다. 전체/표시 건수를 구분하며 `useModelPrices`의 error·isError·refetch도 노출해 조회 실패를 미분류/빈 목록으로 숨기지 않는다.
- [ ] 업데이트 패널에 별도의 수집 대상 설정 선택·수집 버튼·공급자별 결과/출처·preview 공급자 필터·선택 적용·예약 시각을 연결한다. supplier filter는 외부 요청을 만들지 않는다. 필터 변경과 새 preview 전환 시 선택을 초기화하고 `공급자 필터가 변경되어 적용 선택을 해제했습니다` 또는 `새 수집 결과로 적용 선택을 해제했습니다`를 status로 알린다.
- [ ] 공급자별 실패 요약은 후보 필터 바깥에 유지한다. 선택 공급자의 `수집 실패`, `정상 수집 결과 없음`, `적용 가능한 항목 없음`을 구분하고 `notInCatalog`는 `목록에서 확인되지 않음 · 확인 필요`로 표시한다. Google 수집 실패 후 OpenAI로 필터를 바꿔도 Google 실패 요약이 남는 공통 E2E와 초기 선택 0건·닫기/선택 해제 시 적용 요청 0회 검사를 포함한다.
- [ ] `현재 표시된 적용 가능 항목 선택`과 적용 버튼의 선택 건수를 제공한다. 화면 필터 변경 시 숨겨지는 항목을 apply payload에 남기지 않는다. 요청 진행 중 필터/선택을 잠그고, 새 동작은 새 requestId, 응답 유실 재시도는 같은 입력을 재사용한다. 자동 mutation retry는 끈다.
- [ ] 가격 응답은 필드·enum·날짜·배열을 읽는 reader로 검증하고 기존 단가 응답의 새 필드 누락만 호환한다. 성공 apply 후 prices·callStats·jobCalls 계열 캐시를 무효화하고 409는 오류를 표시하며 사용자가 재수집하도록 한다. 임의로 새 requestId·새 가격으로 자동 재적용하지 않는다.
- [ ] Fake E2E에서 5개 공급자+legacy 미분류, 숨겨진 선택 0건 전송, 필터 시 네트워크 수집 0회, 일부 실패/만료/중복/재시도, 390px·1440px·키보드·light/dark를 검증한다. `pnpm --filter @nextend/frontend test src/domain/tuning/priceUpdates.test.ts src/infra/api/priceUpdateApi.test.ts` 및 `pnpm --filter @nextend/frontend test:e2e tests/e2e/price-updates.spec.ts tests/e2e/prices-admin.spec.ts` 통과 후 독립 리뷰·관련 지도·scoped commit.

## Task 7: 비교 평가·선택 결과 통합·전체 회귀

**Files:** 신규 `docs/evals/model-price-update/results.md`; 수정 선택 구현의 영향받은 코드 지도 `docs/xHuman/{providers-and-prompts,frontend,backend}.md`와 필요한 경우 `job-execution.md`, 이 계획의 실행 기록. 실제 변경된 영역만 갱신한다.

**Interfaces:** 입력은 A/B 독립 결과·원시 로그·공통 acceptance 결과다. 출력은 조건명을 가린 비교표와 통합 후보이며 한 번의 실험을 일반적인 향상률로 표현하지 않는다.

- [ ] 새 A/B 참가자는 같은 brief와 fixture로 실행한다. 양쪽 모두 Subagent-driven이며 같은 과제 수와 리뷰 기회를 제공한다. A는 xHuman 읽기/갱신만 실험용 예외, B는 관련 지도 참조·갱신. 상세 진행자 계획과 해결책 전달은 금지한다.
- [ ] 제안 상한은 팀당 구현·작업 리뷰 120분, 실행 검증 45분이다. 시간 초과도 결과에 남기고 한쪽에만 연장하지 않는다. 환경 준비·서버/컨테이너 대기·진행자 통합 시간은 별도 집계한다. 토큰 계측이 불가능하면 토큰/비용을 미확인으로 남긴다. 이 상한은 소요시간 예측이 아니며 계획 승인 후 실행 시작 전에 고정한다.
- [ ] 각 구현·리뷰 로그에서 xHuman 노출·다른 사본 접근·새 웹자료 사용을 확인한다. 참가자 출력에 없는 자동 맥락까지 확인할 수 없으면 그 한계를 결과에 명시한다. A/B 라벨을 가린 새 리뷰어가 Task 1에서 해시를 고정한 공통 검사와 실제 diff를 대조한다. 각 참가자의 자체 테스트 결과는 공통 검사 결과와 별도 기록한다.
- [ ] 동시 E2E 서버/SQL 자원 경합을 피해서 두 결과의 전체 회귀를 순차 실행한다. Backend: `dotnet build apps/backend/Noxtend.slnx` 및 `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`. Frontend: `pnpm test && pnpm lint && pnpm typecheck && pnpm build && pnpm test:e2e`.
- [ ] 합격 항목/실패/미실행, 리뷰 수정 횟수, 불필요한 변경, 총 작업·검증 시간, 환경 차이를 함께 보고한다. 속도만으로 구현을 선택하지 않는다. 선택 후보를 통합한 뒤 변경된 부분은 재리뷰·영향 회귀하고, 두 결과를 무조건 합쳐 사용하지 않는다.
- [ ] 문서·코드 동기화, `pnpm docs:check`, `git diff --check`, scoped commit을 완료한다. 실 공급자 검증·merge/push·배포는 실행된 증거가 있을 때만 별도로 보고한다.

## 요구사항 추적과 자체 검토

| 요구 | 담당 작업 |
| --- | --- |
| 5개 공급자 모델·단가 수집과 공식 근거 | 1·3·4 |
| 신규 모델/실행 지원/단가 표현 가능성 구분 | 2·3·4 |
| provider 저장·legacy 미분류·필터·실패 요약 유지 | 2·6 |
| 목록 미노출의 보수적 판정·기존 데이터 보존 | 1·4·6 |
| 계정 간 동일 후보 중복 제거·상충 요금 보류 | 1·4·5 |
| snapshot·30분·멱등·원자성·동시성 | 2·5 |
| 과거 비용·미등록·alias·미래 시행일 보존 | 2·5 |
| 필터 전환 시 숨겨진 선택 차단·오류·접근성 | 6 |
| 동일 외부 자료·공통 채점·정보 노출 제한·비교 결과 | 1·7 |
| 전체 회귀·독립 리뷰·최종 지도 동기화 | 각 작업·7 |

자체 검토: Spec 요구를 위 표에 배치했고, 신규/기존 경로와 경로 기준을 구분했다. 공통 메서드·DTO·enum은 계약 표 한 곳을 기준으로 한다. Task 2의 nullable current와 Task 6의 filter 때문에 기존 관리자/단가 힌트가 영향을 받으므로 해당 호출부·회귀를 포함했다. 설계 조사자나 이 계획 작성자를 비교 참가자로 재사용하지 않는다.

## 실행 기록

- 검토 기준 커밋: `49a98ac`. 커밋 후 목록 미노출·계정 간 후보 중복·공통 실행 검사·필터와 독립된 실패 요약의 누락을 보완했다. 테스트 파일과 패키지 추가는 후속 구현 계획이며 이번 문서 작업에서 실행하지 않았다.
- 계획 작성: 기본 설계 승인과 공급자 필터 요구를 반영했다. 기본 설계 문서도 같은 동작으로 수정했다.
- 실행 방식: Subagent-driven. 2026-10-09 사용자 실행 승인 후 독립 worktree `codex/model-price-update`에서 공통 준비 시작. A/B 측정은 공통 자료·검사 확정 후 시작하며 유료 호출·운영 DB 적용은 범위 밖이다.
- 현재 검증: 두 문서 대상 `pnpm docs:check` 통과, 참조 기존 경로 존재 및 두 문서의 줄 끝 공백 없음 확인. 기능 테스트는 실행하지 않았다.
- Task 1 공통 준비 진행 중이다. 실행 중 완료·실제 명령 결과·수정·미해결 항목을 이 절에 기록한다.
