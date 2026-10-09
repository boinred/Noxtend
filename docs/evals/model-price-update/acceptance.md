# 공통 합격 검사 실행

공통 준비는 모델 가격 기능을 구현하지 않는다. 테스트 host는 실제 API·SQL migration·기존/신규 저장소를 사용하며 네트워크 경계만 원문 fixture로 대체한다. 수집 port나 parser를 Fake로 등록하지 않는다. UI만 명시적인 합성 응답을 사용한다.

## 명령과 전제

저장소 루트에서 .NET 10 SDK·Docker(SQL Testcontainers), Node.js >=24·pnpm 11.9.0·기존 Playwright Chromium을 사용한다. `Microsoft.AspNetCore.Mvc.Testing`은 기존 API/EF package와 같은 10.0.10으로 고정한다. 현재 준비 환경 SDK는 10.0.400, ASP.NET runtime 10.0.11이다. restore는 측정 전에 두 사본에서 완료한다.

```bash
dotnet restore apps/backend/Noxtend.Tests/Noxtend.Tests.csproj
dotnet test apps/backend/Noxtend.Tests/Noxtend.Tests.csproj --filter FullyQualifiedName~ModelPriceUpdateAcceptanceTests
pnpm --filter @nextend/frontend test:e2e tests/e2e/model-price-update-acceptance.spec.ts
```

HTTP 명령은 29건을 수집한다. fixture 무결성 검사는 네트워크 없이 원문/파생 응답의 모든 SHA를 검증한다. 나머지 검사는 기존 SQL 공유 컨테이너에서 검사마다 새 DB를 만들고 host가 migration을 수행한다. 기존 단가 삭제/사전 등록 역시 HTTP를 사용한다. 개발/운영 DB로 대체하지 않는다. Worker를 해제하고 키는 ephemeral Data Protection으로 보호하며 일회용 DB 연결을 우선한다. 서버 시간을 주입한 TimeProvider로 이동해 30분 만료를 기다리지 않고 검증한다.

outbound handler는 지정 공식 host/경로의 GET만 허용하며 미등록 URL/POST는 즉시 실패한다. API 모델 목록의 합성 응답도 가격과 별개로 원문 fixture에 저장한다. 공급자 API 키는 테스트 전용 비밀 아닌 문자열이다. 실제 계정 키·실 API·유료 생성·업로드·모델 호환성 시험은 호출하지 않는다. 참가자가 새 HTTP 구성을 사용하면 평가자는 **host의 연결만** 동일하게 조정하고 변경/해시/이유를 기록한다. 기대값/판정 코드를 구현에 맞춰 바꾸지 않는다.

## 고정 HTTP 사례

| 분류 | 건수 | 관찰 |
| --- | ---: | --- |
| fixture integrity / 기존 CRUD | 2 | SHA 일치 / 실제 API와 일회용 SQL 저장 |
| 정상 5개 공급자 | 5 | 정확한 원문 가격·Standard 조건·원문 해시·단위·unknown/3D 보류 |
| 선택·재전송·성공 후 만료 | 1 | 선택된 신규 행만 저장, 서버 근거/비소급, 같은 receipt/시행일, 다른 본문409 |
| blocked·중복·없는 후보 묶음 | 1 | 400, 묶음 전체 저장0 |
| 동시 적용 | 1 | 200 하나 +409 하나, SQL 신규 행1 |
| 검토 후 현재 값 변경 | 1 | fingerprint conflict409, 추가 저장0 |
| 미적용 만료 | 1 | 30분 TTL/409, 기존 값 보존 |
| 중복 설정+모델 pagination | 1 | ID중복제거, config 관찰 병합, 정확한 조건 정체당 후보1, page2 반영 |
| 정상/partial/failed 모델 목록 미노출 | 3 | 완전 목록만 notInCatalog, 삭제0 |
| 일부 공급자 목록 실패 | 1 | sibling 성공·modelError/priceError 구분·키 비노출 |
| 구조 변경 / 단위 누락 5사 | 10 | 명시적 확인 필요/부분 실패, 임의 적용가능 가격0 |
| 공식 미래 시행 | 1 | 2099 하한, 이른 예약400, 즉시도 미래 저장·비소급 |
| 입력/없는 preview | 1 | 빈/없는/비활성/26설정400, 없는preview404 |

같은 모델에 서로 다른 요금 조건 후보가 존재해도 정상이다. 기대 가격/Standard 조건에 해당하는 후보를 선택하며 모델 하나당 후보 하나를 강제하지 않는다. Tripo는 P1 상세의 멀티뷰/표준 텍스처 50credit와 공개 $0.01/credit 근거를 확인하며 일반 H 표의 가격을 P1에 대입하면 통과하지 않는다. Meshy는 multi-image 2K/meshy-6 30credit를 보존하고 USD 환산을 적용하지 않는다.

## 고정 Fake UI 사례

8건: 기존 전체/5사/미분류 필터와 네트워크0, 독립 필터/숨겨진 선택 초기화/표시된 선택만 적용, 필터에 독립적인 실패 요약과 빈/blocked 구분, 명시적 재수집 선택 초기화, 조회 오류/수동 재시도/자동 retry0, 만료 안내/자동 재요청0, 390px dark와 1440px light의 키보드/포커스/출처 HTML 미실행/overflow 확인. UI의 숫자·해시는 합성이며 원문 parser 정확성 증거로 계산하지 않는다. 관찰 표식·JSON은 brief에 공개되어 있다.

## 측정 전 RED와 남은 검증

미구현 기준에서 fixture integrity·기존 CRUD는 PASS, 신규 HTTP 27건은 `/api/prices/update-previews` POST 405로 RED다. 기존 모델별 GET route와 충돌해 404 대신 405인 것이며 harness 초기화 실패를 기대 RED로 계산하지 않는다. UI 8건은 신규 필터/수집 컨트롤 없음으로 RED다. 기능 구현 후 같은 테스트·해시·기대값으로 실행한다.

공통 검사는 전체 기능 검증을 대체하지 않는다. 참가자 자체 테스트/최종 독립 리뷰는 과거 호출의 미등록/legacy/alias 비용 계산, 계정 간 환산 차단, 상충 요금/다른 조건 저장키 충돌, 실패 중 rollback, 응답 유실 재전송, 취소/timeout/크기/page상한, capability/실행 설정 보존을 검증해야 한다. 전체 비유료 Backend 및 Frontend 회귀는 진행자가 최종 통합 시 별도로 실행한다. 이번 준비에서는 전체 회귀를 실행하지 않는다.

fixture manifest의 `acceptance` 항목은 사례 수와 공통 테스트/host/brief의 SHA를 고정한다. 결과 로그는 저장소 밖에 보관하며 키·HTTP 인증 헤더·전체 원문 dump를 보고에 넣지 않는다.
