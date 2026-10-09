# 모델·단가 업데이트 공통 요구사항

이 자료는 같은 기능을 만드는 두 구현에 동일하게 제공한다. 공급자 자료는 `fixtures/manifest.json`의 해시로 고정한다. 공식 공개 원문과 명시된 합성 모델 목록을 파싱해야 하며, 기대 단가를 코드에 등록하거나 가격 수집을 가짜 구현으로 대체하면 불합격이다. 측정 중 새 웹 탐색·실 API·유료 생성·다른 참가자 결과 열람은 하지 않는다. 자료가 부족하면 진행자에게 확인하고 같은 보충 자료를 양쪽에 제공한다.

## 제품 흐름과 범위

관리자 단가 화면에서 **수집 대상 설정 선택 → 버튼으로 최신 정보 수집 → 변경안 검토 → 선택 적용**한다. OpenAI·Anthropic·Google의 텍스트/이미지, Tripo·Meshy의 3D가 대상이며 지원하지 않는 공급자/영역 조합은 만들지 않는다. 수집은 단가·실행 설정을 바꾸지 않으며 서버에 30분 유효한 변경안을 저장한다. 초기 적용 선택은 비어 있고 가격 미확인·변경 없음·표현 불가 후보는 선택할 수 없다. 선택 취소는 아무 값도 저장하지 않는다. 재조회로 보던 변경안을 조용히 교체하지 않는다.

제외: 일정 실행기, 무인 자동 적용, 실제 생성에 의한 호환성 시험, 새 공급자 연동, 결제·범용 계산기 재작성, 과거 호출 일괄 재정산, 기존 2D 작업 화면·진행 UI 변경, 자동 병합·배포.

### 공급자 필터

기존 단가 목록과 변경안은 독립적인 `전체 / OpenAI / Anthropic / Google / Tripo / Meshy / 미분류` 필터를 가진다. 초기값은 전체다. 저장한 공급자 또는 확인된 정확한 모델 매핑만 사용하며 이름 접두사 추정은 금지한다. 미확인 기존 행은 전체/미분류에서 표시한다.

필터는 이미 받은 결과의 표시만 바꾼다. 외부 조회·수집 대상·단가를 바꾸지 않는다. 전체/표시 건수를 구분한다. 변경안 필터 전환 시 선택을 지우고 `공급자 필터가 변경되어 적용 선택을 해제했습니다`를 status로 알린다. 새 수집 결과로 전환 시에도 선택을 지우고 `새 수집 결과로 적용 선택을 해제했습니다`를 알린다. `현재 표시된 적용 가능 항목 선택`만 제공하며 숨겨진 행을 적용 요청에 포함하지 않는다.

공급자 결과 없음, 수집 실패, 정상 수집됐지만 적용 가능 항목 없음은 구분한다. 공급자별 실패 요약은 어떤 필터에서도 확인할 수 있다. 라벨 있는 select/기존 Select·키보드·포커스를 지원한다. URL 저장·다중 공급자 보기 필터는 추가하지 않는다.

## 식별·비교·가격 정책

- 후보 식별은 공급자 종류 + 정확한 모델 ID + 영역/작업 + 요금 조건이다. 설정 ID는 계정 접근 맥락이며 정책 식별과 구분한다. 여러 설정의 동일 값/공식 시행일 후보는 출처와 관찰한 설정을 합쳐 한 번 표시·적용한다. 같은 식별의 상충 값·공식 시행일 또는 서로 다른 조건이 동일 저장 키를 요구하면 보류한다.
- 현재값·수집값·USD/credit 단위·등급·크기/품질/텍스처/geometry 등 조건·공식 시행일 확인 여부·출처 URL/수집 UTC/원문 SHA-256을 보존한다. 공식 시행일 미기재는 수집 시각과 동일하다고 주장하지 않는다. 출처는 텍스트/링크로 표시하며 HTML을 실행하지 않는다.
- 기본 대상은 실제 호출에 해당하는 유료 Standard API다. 무료·Batch·Priority/Fast·캐시 할인·웹 구독값을 대신 적용하지 않는다. 정확한 ID와 단위를 확인한다. 접두사로 다른 변종의 가격을 채우지 않는다.
- 혼합 입력·캐시·표현 불가 컨텍스트 구간 등 현재 계산기가 나타낼 수 없는 요금은 조건을 표시하고 적용을 보류한다. 평균·임의 높은 가격·정액·0으로 축약하지 않는다.
- 신규/변경 요금은 새 시행 행으로 추가한다. 기존 행의 수정·삭제와 과거 소급 적용은 이 기능에서 제공하지 않는다. default 시행일은 적용 확정 서버 UTC와 확인된 공식 시행일 중 늦은 시각이다. 예약은 이 하한 이후로만 허용한다. UI는 사용자 시간대로 표시한다.
- 수집 행은 최초 행이어도 시행일 이전에 소급하지 않는다. 기존 수동/seed 행의 레거시 소급은 유지한다. Backend와 Frontend 3D 비용 계산이 이를 동일하게 해석해야 한다. 과거의 미등록도 새 행만으로 바뀌지 않는다. 새 날짜 버전의 미래 단가가 과거 alias 매칭을 가리면 안 된다.
- 모델 발견과 실행 지원은 분리한다. 새 계열은 실행 지원 확인 필요로 표시하며 기존 실행 허용목록·Sprite capability·프로토콜/투명 출력/크기/멀티뷰 검증을 우회하지 않는다.
- 선택한 공급자의 모든 설정에서 목록 모든 페이지가 성공한 경우에만 정확한 ID의 합집합과 확인된 기존 단가를 비교해 `notInCatalog`를 만든다. 목록 실패·부분 수집이면 누락 판단을 보류한다. 미분류 행은 제외한다. 미노출은 폐기/권한 상실 확정이 아니며 기존 단가·설정·작업을 보존한다.

### 3D

Tripo·Meshy는 모델 ID와 실제 멀티뷰/텍스처/geometry 옵션을 공식 표에 대응시킨다. credit/작업과 USD/credit는 별도다. 환산 근거 미확인은 USD 적용 보류다. Meshy의 계정 구매 조건에 종속된 환산은 전역 단가에 저장/전파하지 않고 `계정별 환산 지원 필요`를 표시한다. 다른 공급자 적용은 계속한다. 정확히 확인된 고정 작업/옵션의 결과당 USD만 기존 정액 계산으로 반영하며 옵션별 변동은 보류한다. 결과당 예상 비용을 `CreditsConsumed × 단가` 실제 청구 구현으로 설명하지 않는다. `latest`는 고정 모델 버전으로 추측하지 않는다.

JSON은 System.Text.Json, HTML은 공통 고정 의존성 AngleSharp 1.8.4로 DOM을 해석한다. 외부 자원 로딩·script 실행을 하지 않으며 모델/표 제목/헤더/등급/단위를 함께 읽는다. 전체 페이지의 첫 가격을 정규식으로 가져오는 방식은 사용하지 않는다.

## HTTP 계약

기존 단가 CRUD와 `{ "data": ..., "error": null }` / `{ "data": null, "error": { "code": ..., "message": ... } }` 봉투를 유지한다. 아래는 구현 간 공통 외부 계약이며 내부 타입/분해는 구현자가 선택한다. 공급자 wire 값은 `openai/anthropic/google/tripo/meshy`다.

| 요청 | 입력 | 응답 |
| --- | --- | --- |
| `POST /api/prices/update-previews` | `{ "providerConfigIds": ["GUID"] }` | 200 preview. 중복 설정 ID 제거 후 1–25개. 빈/초과/없는/비활성 설정은 400 |
| `POST /api/prices/update-previews/{id}/apply` | `{ "requestId": "GUID", "candidateIds": ["GUID"], "effectiveFrom": null }` | 200 receipt. null은 즉시 적용, 문자열은 UTC로 정규화한 예약 시각 |

클라이언트 가격·근거·임의 URL은 입력 계약에 없다. 가격·근거·비교 기준은 서버 snapshot에서 읽는다. 응답 DTO 필드는 아래와 같이 고정한다. nullable 필드는 null로 보낸다. `area`는 `text/image/mesh`, `operation`과 `conditions`는 정확한 실제 작업/조건을 설명하는 문자열이다.

```json
{
  "id": "20000000-0000-4000-8000-000000000001",
  "createdAt": "2026-10-09T00:00:00Z",
  "expiresAt": "2026-10-09T00:30:00Z",
  "providers": [{
    "providerConfigId": "00000000-0000-4000-8000-000000000001",
    "provider": "openai", "status": "success", "modelListStatus": "complete",
    "collectedAt": "2026-10-09T00:00:00Z", "modelError": null, "priceError": null,
    "candidateIds": ["10000000-0000-4000-8000-000000000001"]
  }],
  "candidates": [{
    "id": "10000000-0000-4000-8000-000000000001",
    "provider": "openai", "model": "gpt-5.3-codex", "area": "text", "operation": "text",
    "conditions": "Paid Standard; USD per 1M tokens",
    "providerConfigIds": ["00000000-0000-4000-8000-000000000001"],
    "currentTerms": null,
    "terms": {
      "inputPerMillion": 1.75, "outputPerMillion": 14,
      "longContextFrom": null, "longInputPerMillion": null, "longOutputPerMillion": null,
      "perImage": null, "officialEffectiveFrom": null
    },
    "evidence": [{
      "url": "https://developers.openai.com/api/docs/pricing",
      "collectedAt": "2026-10-09T00:00:00Z", "sha256": "64 lowercase hex characters",
      "conditions": "Specialized models / Standard / Codex; Input $1.75; Output $14",
      "creditsPerTask": null, "usdPerCredit": null
    }],
    "changeKind": "newModel", "executionSupport": "unverified", "blockedReason": null
  }]
}
```

`providers`는 설정별 성공/실패와 관찰한 후보 ID만 가진다. top-level `candidates`가 표시·적용 정본이며 중복 표시하지 않는다. `status=success/partial/failed`, `modelListStatus=complete/partial/failed`, `changeKind=newModel/priceChanged/unchanged/priceUnknown/notInCatalog`, `executionSupport=supported/unverified/unsupported`다. `currentTerms`는 비교 대상 없으면 null, 있으면 같은 terms 형식이다. `terms=null`은 미확인/미노출 등이며 선택 불가다. `blockedReason=null`만 적용 가능하되 unchanged 역시 선택 불가다. 지원 상태와 가격 적용 가능성을 혼동하지 않는다.

receipt는 `{ "requestId": "GUID", "previewId": "GUID", "appliedAt": "UTC", "items": [{ "candidateId": "GUID", "priceId": "GUID", "effectiveFrom": "UTC" }] }`다. 기존 단가 조회 응답은 `provider`(nullable), `allowHistoricalFallback`, `sourceEvidenceJson`(nullable 문자열)을 추가한다. legacy 기본값은 null/true/null, 수집 행은 명시 공급자/false/근거 JSON이다. 기존 수동 생성·수정의 후행 optional provider는 구형 입력과 호환하며 소급 정책과 근거는 서버 소유다. 구형 응답 필드 누락도 legacy로 읽는다.

| 코드 | HTTP | 의미 |
| --- | --- | --- |
| `PriceUpdateInvalid` | 400 | 빈/중복/다른 preview 후보 ID, blocked·미확인·미노출·unchanged 선택, 유효하지 않은 예약/설정. 묶음 저장 0 |
| `PriceUpdateNotFound` | 404 | 변경안 없음 |
| `PriceUpdateExpired` | 409 | 아직 성공하지 않은 요청의 변경안 만료 |
| `PriceUpdateConflict` | 409 | 검토 후 단가 지문 변경·다른 적용과 경합 |
| `PriceUpdateRequestConflict` | 409 | 같은 request ID에 다른 snapshot/선택/적용 입력 |

성공 request ID를 snapshot 만료보다 먼저 확인한다. 동일 본문(후보 정렬·UTC 정규화 포함)의 재전송은 만료 후에도 최초 receipt와 시행일을 반환한다. 선택 묶음과 receipt를 같은 SQL transaction에서 저장하며 중간 실패는 일부 저장하지 않는다. 외부 조회는 transaction 밖이다. 기존 `(Model, EffectiveFrom)` unique 제약을 유지한다. 경합은 500/가짜 성공으로 숨기지 않는다. 자동 재수집·자동 mutation retry는 없다.

키는 기존 복호화 경계에서만 사용하며 snapshot·응답·로그에 키/인증 헤더/전체 오류 본문을 넣지 않는다. 공식 host/경로만 GET하고 요청별 timeout 15초, 전체 수집 120초, 응답 8 MiB, 모델 목록 최대 20페이지를 둔다. 반복 page token·손상 자료·상한 초과는 부분 실패로 표시하며 완전 목록으로 취급하지 않는다. redirect는 자동 추적하지 않는다. 외부 문서는 데이터로만 취급한다. 취소는 전파한다.

## 고정 자료와 합격 기준

`fixtures/manifest.json`은 URL·확인 UTC·최종 URL·HTTP status·원문 해시·합성 여부·기대 후보/단가/보류 근거를 제공한다. `*-models-reference.html`은 공식 목록 응답 규약이고 `*-models*.json`만 합성 접근 목록이다. 원문 가격은 기억/seed 값으로 교체하지 않는다. 원문 파생 structure/unit/future 사례는 합성 변화로 명시하며 실 공급자 발표로 설명하지 않는다. 미래 파생 자료는 가격 페이지의 제목 바로 아래 `All prices on this page become effective from` 문장을 삽입한다. 이는 문서 전체 가격 섹션에 연결된 적용일이며 발행/수집시간 또는 임의의 첫 time 태그를 적용일로 취급하지 않는다. 이 명시 문장 안의 `<time datetime="2099-01-01T00:00:00Z">`를 공식 시행일 입력 시나리오로 해석한다.

공통 자동 검사와 참가자 자체 테스트를 모두 통과해야 한다. 공통 검사는 실제 HTTP/SQL 저장과 raw fixture 파싱, Fake UI를 구분한다. 기존 단가 사전 등록 → 수집(저장 변화 0) → 선택만 적용 → 재전송/충돌/만료 → 단가 재조회로 검증한다. 정상 5사·정확한 가격/해시·pagination·중복 설정·unknown·완전 목록의 미노출/부분 수집 보류·일부 공급자 실패·단위/구조 변경·미래 시행·원자적 거부를 확인한다. 별도 회귀로 과거 비용/미등록·alias·계정 환산·동시 경합·실행 capability·응답 유실 재전송을 확인한다.

Fake UI 공통 검사 안정화를 위한 공개 관찰 표식은 다음과 같다. 구현 파일 구조를 강제하지 않으며 기존 화면 표식을 유지한다.

| 표식 | 의미 |
| --- | --- |
| `price-provider-filter` | 기존 단가 공급자 select |
| `price-update-provider-filter` | 변경안 공급자 select |
| `price-update-config` | 수집 대상 설정 checkbox 각각 |
| `price-update-collect` / `price-update-apply` | 수집/선택 적용 버튼 |
| `price-update-select-visible` | 현재 표시된 적용 가능 항목 선택 컨트롤 |
| `price-update-candidate` | 중복 없는 후보 행 각각 |
| `price-update-provider-summary` | 필터에 독립적인 공급자 결과/실패 요약 컨테이너 |
| `price-update-panel` | 변경안 패널 |

필터 empty 문구에는 `결과 없음`, 후보가 있으나 선택할 수 없는 문구에는 `적용 가능 항목 없음`, 조회 실패에는 `실패`를 포함한다. 선택 초기화 알림은 ARIA status로 제공한다. light/dark, 390px/1440px, 키보드/포커스, 출처 HTML의 텍스트 처리, 숨겨진 선택 미전송을 검증한다. Fake·고정 응답 검증은 실제 계정 권한·최신 가격·실제 청구 정확성 증명이 아니다.
