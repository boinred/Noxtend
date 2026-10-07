# 2D 배경 스프라이트 실행·검증 기록

**Goal:** 양쪽 회귀·계약·픽셀·브라우저 관찰과 실 AI 미확인 경계를 검증 가능한 완료 기록으로 남긴다.

**Spec:** [승인 설계](../specs/2026-10-06-2d-background-sprites-design.md)

## 목적과 범위

승인 [설계](../specs/2026-10-06-2d-background-sprites-design.md)와 [구현 계획](2026-10-06-2d-background-sprites.md)의 구현·검증·전체 리뷰 기록이다. 이미지 입력의 2D 배경만 추가하며 기존 3D 흐름을 유지한다. Task15 검증 시작 기준은 `e8c265f67b62114b7db3b0ae472134b2a2c97bbf`, 브랜치는 `codex/2d-background-sprites`다. Task 1..14의 scoped 독립 리뷰가 끝난 뒤 기존 checkout에서 검증했다. 아래 자동 검사·브라우저 Fake 관찰·픽셀 fixture와 실 AI 품질을 구분한다.

## Global Constraints

이미지 입력의 Background·TwoD만 제공한다. 입력12MiB/16,777,216픽셀·생성32MiB·대상1..12·유효프레임≤64·시트4096×4096/2px·ZIP256MiB와 SQL정본/receipt-first/취소불가재개방 제약은 [승인 계획](2026-10-06-2d-background-sprites.md)의 Global Constraints를 따른다. 실제 AI/유료smoke/개발DB적용/배포/push/merge는 미실행이다.

## 현재 구현 계약 대조

| 경계 | 실제 코드·검증 근거 | 확인한 계약 |
| --- | --- | --- |
| C#/TS/Fake HTTP | `Noxtend.Api/Contracts/SpriteContracts.cs`, `Controllers/SpriteJobsController.cs`, `src/domain/sprites/types.ts`, `src/infra/api/spriteApi.ts`, `tests/e2e/spriteFakeApi.ts` | camelCase enum·nullable ID·고정 canvas/anchor·revision·GUID·202 `{data,error}` 접수와 taskIds. 접수 성공은 이미지/ZIP 생성 완료가 아님 |
| 요청·경로 | `SpriteApiTests`, `spriteApi.test.ts` | `POST /api/jobs/sprites`; `PUT .../sprites/plan`; `POST .../plan/approve`, `.../base/approve`, `.../assets/{assetId}/frames/{index}/regenerate`, `.../assets/{assetId}/approve`, `.../exports`; `GET .../images/{imageId}`, `.../exports/{exportId}`. 변경 본문은 requestId/expectedRevision와 해당 DTO 필드만 전달 |
| 종류·라벨·격자·usage | `TaskKind.cs`, `LlmOperationKind.cs`, `backendParity.test.ts`, `PromptGridTests`, `SpriteProviderRequestTests` | Task 7/8/9 = AnalyzeSprites/GenerateSprite/PackSprites, LLM operation 5/6 = AnalyzeSprites/GenerateSprite. 앞선 값 유지. 새 분석·생성은 Background만, pack은 LLM operation/프롬프트/AI 비용 없음. 새 모델 미등록 단가는 null/unknown |
| migration·호환 | `20261006081734_AddSpriteProduction`, `20261006095958_SeedSpriteAnalyzePrompt`, `20261006113049_SeedSpriteGeneratePrompt`, `SpritePersistenceTests` | 상태/이력/receipt 매핑 → 분석 seed → 생성 seed 순서. 메시 없는 구형 작업도 ThreeD, 운영자 기존 프롬프트 슬롯 보존. 개발 DB 적용 없이 격리 Testcontainers에서 검증 |
| 기존 경로 격리 | `SpriteApiTests.EveryLegacyThreeDRoute_RejectsTwoDBeforePlanningOrMapping`, `SceneLayoutHandlerTests`, `SimilarityStartHandlerTests` | TwoD에 3D mesh/review/scene/similarity 요청은 mode 거부. UI 숨김만으로 차단하지 않음. 구형 URL/JSON 읽기 유지 |
| receipt 수명·경합 | `SpriteCommandsHandler.ApplyAsync`, `StartSpriteJobHandler`, `SpriteRequestConcurrencyTests`, `SpritePersistenceTests` | 요청 ID는 전체 job에서 unique, SHA-256 fingerprint·receipt·상태를 같은 SQL transaction에 저장. 기존 요청 조회가 revision/취소/모델 검사보다 먼저이며 job조회 사이 승자도 재조회. 같은 ID 다른 본문409, 새 ID 낡은 revision409. job 삭제 시 owned receipt도 cascade 삭제; 영구 tombstone/TTL 저장소를 제공하지 않음 |
| 생성·공개 | `RunSpriteGenerationTaskHandler`, `TaskExecution`, `SpriteGenerationTests` | immutable Plan/GenerationCanvas/Transform, 기준에는 원본·후속에는 원본+승인 기준, index/frameCount 위상. 실제 PNG/MIME/알파/크기 검사. claim의 자기 RowVersion·lease·현재 슬롯·취소를 최신 SQL과 대조. 갱신 중 시작한 SQL은 종료를 await |
| 종료·내보내기 | `PipelineJob.Sprites.cs`, `SpriteCommandsHandler`, `RunSpritePackTaskHandler`, `SpriteCommandTests` | 전체 요청 대상 승인+pack 성공이면 succeeded, 명시적 subset pack이면 partiallySucceeded. 일부 생성 실패에 쓸 결과가 있으면 pendingReview. 이전 성공/실패 완료 소비 ID는 explicit reopen을 재종료하지 않음. 명시 pack retry는 소비 표식을 초기화 |
| ZIP 존재·삭제 | `SpriteResponse.From`, `EfJobRepository.DeleteIfTerminalAsync`, `DeleteJobHandler`, `SpriteRequestConcurrencyTests.PackDelete_RemovesOwnPngAndZipButPreservesSharedUpload` | 내부 CompletedExportId는 성공/실패 소비 표식. 외부 다운로드는 실제 Exports 행만 제공. 실패 pack은 ZIP 없음. owned PNG/ZIP 삭제, 공유 업로드는 마지막 참조에서 삭제, 기존 결과를 입력으로 쓴 작업은 독립 복사본 유지. DB삭제 뒤 Blob삭제 실패는 경고·고아파일 가능 |

## Task 15: 양쪽 회귀·품질 경계·완료 기록

**Files:** `README.md`, `docs/xHuman/backend.md`, `docs/xHuman/frontend.md`, `docs/xHuman/providers-and-prompts.md`, `apps/backend/Noxtend.Tests/Api/SpriteApiTests.cs`, `apps/backend/Noxtend.Tests/Infrastructure/SpritePackageTests.cs`, 본 실행 기록. 부모의 구현 계획은 Task15 커밋에서 제외한다.

- [x] 구현자 계약 대조·기존 deferred assertion 보강·최종 자동 회귀·브라우저 관찰·Ruling 보존
- [x] 부모의 Task15 scoped 독립 리뷰 판정
- [x] 전체 브랜치 리뷰 지적 수정·최신 회귀·scoped 재리뷰 최종 판정

## Task15 자동 검증 (`703acb9`)

최종 소스 변경은 기존 테스트 두 파일의 assertion 보강뿐이다. 앱 소스 임시 mutation은 RED 확인 직후 원본 바이트로 복원했다. source/self-review 완료 뒤 full 검사를 시작했으며 Backend 명령은 각각 `caffeinate -i`가 해당 dotnet 프로세스 수명 동안만 idle 수면을 막도록 실행했다. SQL 테스트는 기존 `sql-server` shared collection에서 직렬이고 별도 dotnet/DB suite를 동시에 실행하지 않았다. Frontend·브라우저와 Backend는 독립 실행했다.

| 명령 | 결과 |
| --- | --- |
| `dotnet build apps/backend/Noxtend.slnx` | 0 errors, 4 NU1903 warnings, exit0 |
| `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~Noxtend.Tests.Domain\|FullyQualifiedName~Noxtend.Tests.Application\|FullyQualifiedName~Noxtend.Tests.Api\|FullyQualifiedName~Noxtend.Tests.Architecture'` | 1174 collected/passed, failed0, skipped0, exit0 |
| `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'` | 1550 collected/passed, failed0, skipped0, 6m14s, exit0 |
| `pnpm test` | Vitest 50 files, 422 collected/passed, failed0, skipped0, exit0 |
| `pnpm lint` | ESLint/Oxlint/Prettier exit0 |
| `pnpm typecheck` | exit0 |
| `pnpm build` | exit0; 초기 JS gzip113.67kB / 기존 예산122.55kB, 여유8.88kB |
| `pnpm test:e2e` | 294 collected/passed, failed0, skipped0, 5.7m, exit0 |
| `pnpm docs:check` | 최종 exit0; 원래 계획500행 경고 |
| `git diff --check`·로컬 링크·추가 내용 secret pattern 검사 | exit0, 로컬 링크32개 존재, secret pattern0건 |

명령의 `|`는 표 안 Markdown 표시를 위해 이스케이프했다. 실제 filter 문자열은 루트 지침 그대로 한 인수로 실행했다. 임시 원시 log·보고서는 검토용이며 결과·명령·실패 경계·판정은 이 문서와 Git에 보존한다. 전용 SDD 작업폴더는 기록 보존 뒤 정리한다. Vitest 수집은 `src/**/*.test.ts`이고 `.test.tsx` DOM 수집은 주장하지 않는다. DOM은 Playwright Chromium과 Fake 응답으로 확인한다. 유료 `TripoSmokeTests`·`SimilaritySmokeTests`는 filter로 미수집이며 통과/skip에 합산하지 않는다. 실제 설정된 API 호스트 관통·개발 DB migration·유료 AI·배포·push·merge는 미실행이다.

### 기존 deferred 테스트 보강과 실패 기록

- Task10: 정상 request/settings를 먼저 MVC formatter에 바인딩하고 오류 없음을 확인한 뒤 blobKey/sourceUrl을 추가해 binding.HasError를 직접 요구한다. StartSpriteJobRequest의 Disallow attribute 하나만 임시 제거한 mutation RED는 4수집/2통과/2실패/0skip, exit1. null-settings 400으로 unknown-field 검사를 숨길 수 없음을 확인하고 원본 복원했다.
- Task5: 기존 padding test의 실제 입력 MemoryStream 두 개, 기존 layers/tiles ZIP test의 callback 모든 입력이 완료 뒤 CanRead=false인지 확인한다. sheet callback의 await using 하나를 임시 제거한 mutation RED는 3수집/0통과/3실패/0skip, exit1. 원본 복원 후 `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~SpriteApiTests|FullyQualifiedName~SpritePackageTests'` GREEN52수집/52통과/0실패/0skip, exit0. 새 프레임워크·앱 동작 변경 없음.
- 관찰용 scratch harness 첫 실행은 12수집/11통과/1실패, exit1. conflictOnce가 approveErrorOnce보다 먼저 처리되는 기존 Fake를 한 시나리오로 겹쳐 재전송 버튼을 잘못 기다린 timeout이었다. 별도 saved-UUID retry와 409 conflict로 나눠 해당 2건만 다시 확인: 2/2통과. 앱 변경 없음.
- 캡처 첫 패스의 base 화면 일부가 내부 스크롤의 이전 폼 위치여서 해당 섹션으로 스크롤한 source/plan/base/subset/export 4조건을 재캡처했다. 추가 4/4통과. 임시 캡처의 관찰 결과는 아래 표에 보존한다.
- 독립 Python 이미지 검사 첫 초안은 불투명 후경에도 alpha0을 기대해 assertion 실패했다. 모델의 투명 요구가 없는 후경255와 실제 투명 전경0..255를 구분해 조건을 고쳤다. Pillow getdata의 deprecation 경고도 초안에 있었고 수정 검사에서는 getpixel을 사용했다. 제품 결함으로 보고하거나 최초 검사 통과로 계산하지 않음.

실행 기록 첫 `pnpm docs:check`는 plans 디렉터리의 필수 Goal/Spec/Global Constraints/Task Files·체크리스트 형식을 빠뜨려 exit1이었다. 실제 Goal/Spec/제약/Task15 파일·완료/리뷰대기 기록으로 형식을 보완한 최종 재검사는 exit0이며 원래 계획500행 경고만 남았다. 작성용 Python 초안의 Markdown pipe escape SyntaxWarning도 코드 작성 오류 기록이며 제품 경고가 아니다.

기존 Task1..14 작업 중 RED·실패·중단도 최종 pass에 섞지 않는다. Task5 이전 회귀 exit130, Task6 최초1375통과/2실패 및 중간 중단, Task7 저장 후 응답 유실·cleanup RED, Task8 전체1468통과/1실패와1472통과/1실패, Task12 최초focused13통과/7실패·fullunit432통과/2실패·focus race21통과/1실패와 중간 중단, Task13 completed base 재생성·RAF RED 및 중간 full 중단, Task14 초기 bundle141.36>122.55 실패와 draft 좌표22통과/2실패는 해당 수정·review 뒤 해결한 과거 실행이다. Task15 최종 최신 코드 검사가 과거 결과를 대신한다. 436→422 unit 감소는 Task14 nav의 개별20→그룹6 case 집계 변경이며 캔버스 금지·구형 parameter URL·home mode routing·prefetch key 불변 검사는 유지한다.

## 브라우저 관찰과 픽셀·ZIP 검증

자체 실행한 `pnpm preview --port 4173 --strictPort --host 127.0.0.1`은 최신 Task15 build를 사용했다. 먼저4173에 기존 listener가 없음을 확인했고 다른 서버나 컨테이너를 중지하지 않았다. 관찰 종료 후 소유 preview(session72295/PID8209)만 Ctrl+C로 종료(exit130/SIGINT)했고4173 listener가 없음을 확인했다. 이는 테스트 중단이 아니라 완료 뒤 서버 정리다. viewport는1440×900/390×844, 테마는light/dark다. Playwright 조작·Fake 응답과 저장 캡처의 `view_image` 시각 관찰을 조합했으며 실제 터치기기 검증은 하지 않았다.

| 항목 | 관찰한 결과 |
| --- | --- |
| 원본·계획·기준·프레임·내보내기 | 양 테마/폭에서 라벨·컨트롤·이미지·하단 rail이 경계 안에 있고 document scrollWidth≤viewport. 내부 세로 스크롤은 유지. 캡처로 일부 section과 실제 focus ring 확인; 모든 느껴지는 사용성·모바일 touch 성능을 주장하지 않음 |
| keyboard/focus | 대상 입력 focus, 기준 선택 Space, range ArrowRight, mobile menu Enter/Escape 후 trigger focus 복귀. desktop 접힌 rail은 3D/2D 그룹과 구분된 accessible label 유지 |
| reduced motion/재생 | reduce 초기 재생 버튼/정지 상태; 자동 재생 없음. FPS4/8에서 slider2 → 각각 frame2/4, 고정 viewBox320×180. fullE2E는 pause/unmount/마지막 loop 해제의 RAF 취소·재활성 paused도 확인 |
| 메뉴 cold/warm·직접 주소 | desktop MobileStudioMenu chunk request0; mobile direct detail 진입 때1회 로드, 두 번 menu open/Escape에서도 추가 요청0. 클릭까지 지연하는 구조라고 해석하지 않음. 기존 fullE2E는 지연 chunk fallback 뒤 keyboard·Escape도 확인 |
| 오류·재조회 | Fake HTTP401/429/500을 실제없는작업/빈 성공으로 표시하지 않고 각 오류와 작업 다시 조회를 제공; 정상 응답으로 전환 후 기준 검수 회복. fullE2E는 실제404·cached detail refetch 오류의 미저장 name/ROI 유지도 확인 |
| 재전송·409 | start/approval 재전송은 동일 UUID/본문 사용, 새 동작은 새 UUID. 409은 revision2로 조회·충돌 표시, 자동 재제출과 동일 승인 재전송 버튼 없음; 사용자의 새 승인에서 새 UUID+revision2 사용. approval503 Fake는 변경 전 실패이며 서버 commit 후 응답 유실의 증거는 SQL receipt tests |
| subset·reload | 포함1/제외 전경 나무 라벨을 확인한 뒤 export → 부분 성공. reload 뒤 같은 상태와 실제 현재 ZIP 포함1/제외1 링크 유지. 선택 checkbox draft는 reload에서 초기화되며 저장 ZIP 구성을 바꾸지 않음 |
| 다운로드 | browser event의 서버 제안 이름 `sprite-{GUID}.png`, `sprites-{GUID}.zip`. 실제 파일 bytes를 저장해 Python Pillow/zipfile와 browser decoder로 확인; magic bytes만으로 PNG 품질을 주장하지 않음 |

추가 artifact 확보는 transparent PNG1/1·loop ZIP1/1 관찰 테스트를 실행했다. `python3 .superpowers/sdd/2026-10-06-2d-background-sprites/task-15-check-downloads.py`는5ZIP CRC/manifest/inventory·opaque PNG4+transparent PNG1·frame 순서/ID·anchor·2px 픽셀 모두 통과(exit0).

부모 controller도 source-dark1440/plan-light390/frame-dark390/export-light1440/menu-dark390 대표5장을 직접 열어 그룹/준비중/하단4목적지·ROI 카드폭·frame focus/FPS·ZIP포함1/제외1·미승인checkbox 비활성을 시각 관찰했다. HTTP/worker/실기기 검증으로 확대하지 않는다.

독립 파일 검사의 opaque PNG320×180은 alpha255..255이고 transparent PNG는0..255다. subset ZIP4entries, loop ZIP21entries와 CRC·schemaVersion1·twoD·topLeft/pixels·포함/제외 ID·320×180 canvas·레이어 anchor(0,0)·frame 순서/고유image ID·2px rect·실제 extrusion 픽셀을 대조한다. 정적 sheet324×184, 8프레임 sheet2592×184다. 동일 Fake frame 색상은 움직임 품질의 증거가 아니다. fullE2E6조합은 layer320×180/square128×128/diamond128×64의 실제PNGdecode·투명전경/diamond alpha와 ZIPdimension을 검사한다. tile anchor는 `PipelineJob.ReplaceSpritePlan`과 Fake에서 Width/2·Height/2이므로 square(64,64)/diamond(64,32)이고 layer는(0,0)이다. tile ZIPanchor를 이 독립layers파일검사로 검증했다고 주장하지 않는다. 실제 다중 페이지 layout과256MiB 정확한 상한/초과·취소는 Backend `SpriteSheetLayoutTests`·`SpritePackageTests`에서 검증한다. 1024×1024 프레임10개는 첫9개3084×3084와 둘째1028×1028로 분리하고 image ID/frame order 유지; 최대4096×4096. BoundedWriteStream은8192byte chunk로256MiB까지 쓴 뒤 추가1byte 실패와 position 보존을 확인한다. 실제256MiB ZIP·ZIP64와 큰 메모리 사용량 실측은 이 수동 검사에 포함하지 않음.

## 실 AI 품질 검증 기준과 미확인 경계

현재 아래6조합의 유료 생성·smoke·사람의 실AI 결과 검수는 모두 미실행이다. 미래의 별도 사용자 요청에 비용 범위가 승인되면 원본 이미지·모델/프롬프트 버전·호출 내역·판정 이미지를 함께 남긴다.

| view×kind | 입력과 검수 기준 | 계획 이미지 슬롯 수 |
| --- | --- | --- |
| sideView×layers | 횡스크롤 원본과 다른 시점 원본 각1; 요청 횡스크롤 시점으로 재구성, 깊이/가림·앞레이어 실제 alpha·원본비율·contain·고정anchor(0,0) 확인 | 대상당 정적1 또는 loop4/8 |
| topDown×layers | 탑다운 및 다른 시점 원본; 위에서 내려다보는 시점/척도·깊이 순서·합성 빈틈·alpha 확인 | 동일 |
| isometric×layers | 아이소메트릭 및 다른 시점 원본; 카메라/투영 재구성·레이어 고정canvas/anchor·가림 확인 | 동일 |
| sideView×tiles | 횡스크롤 소재와 타 시점 원본; square64/128/256 및 X/Y/Both 경계 연속성·셀 중심anchor 확인 | 동일 |
| topDown×tiles | 탑다운 소재와 타 시점 원본; square 격자 X/Y/Both 색/구조/조명 경계 연속성 확인 | 동일 |
| isometric×tiles | 아이소메트릭 소재와 타 시점 원본; 2:1 diamond64/128/256·셀밖 실제투명·직교 화면축 대신 두 격자축 경계 연속성 확인 | 동일 |

공통 loop4/8은 기준 포함 frameCount, FPS1..30(기본8), notes≤500자, index/frameCount 위상이며 마지막 프레임을 첫 프레임 복제로 만들지 않는다. 원본+승인 기준 참조·같은canvas/anchor/contain에서 형태/위치/색 변화와 마지막→첫 전환의 자연스러움을 사람이 검수한다. 자동trim/회전/프레임별재중앙정렬 없음. 시점 변경은 좌표변환만이 아닌 재구성 품질을 확인해야 한다.

대상N≤12, 유효 프레임 합계≤64다. 첫 정상 생성은 정적N×1, 각loop대상4/8이며 분석1 text operation은 별도다. packaging·FPS/이름/순서 metadata 편집0 AI calls. 현재 `Application/Common/JobOptions.cs`의 MaxAttempts 기본값은3이며 실제 호스트 설정으로 바뀔 수 있다. provider transient retry는 JobOptions.MaxAttempts와 rate limit/lease 정책에 따라 호출을 더 만들고 명시 regen은 새 호출이다. 따라서64는 이미지 슬롯 상한이며 retry/regen 포함 비용·호출 상한이 아니다. 미등록 모델가격은unknown/null,0으로 예산을 계산하지 않는다. 실AI 반복 경계·시점재구성·loop 자연스러움·운영 안정성·배포준비완료를 이 기록으로 주장하지 않는다.

## 검증 경고·제약과 최종 리뷰

SSH.NET2025.1.0 NU1903 high advisory `GHSA-mggc-4xg6-vcxf`와`GHSA-q939-rpr3-3284`, 기존 Skia DrawBitmap CS0618은 시작 baseline에서 재현된 미해결 항목이다. 최종 build incremental 출력에는NU1903 4개이며 targeted 재컴파일에는기존 CS0618도 출력됐다. Frontend async chunk>500kB·NO_COLOR/FORCE_COLOR 경고, 계획500행 경고는 알려진 별도 제약이다. 실제 scoped commit의 `.githooks` staged 문서 검사는 통과했고 Frontend 경로 변경이 없어 lint를 재실행하지 않았다. Git 자동 committer identity 안내도 출력됐으며 전역 Git 설정은 바꾸지 않았다. warning-free를 주장하지 않으며 의존성/예산/환경변수/전역 설정을 변경하지 않았다.

자체검토에서는 작업 범위7파일, 기존 테스트의 실제 binding/disposal 검출성, mutation 원본 복원, nullable/enum/receipt/ZIP계약, 이미지/알파/패딩 해석, Ruling61행 순서·원문 일치, 문서/링크32개와 추가secret pattern0을 확인했다. 앱 기능 변경·새프레임워크·plan staging은 없고 비밀값/공백 결함을 찾지 못했다. 픽셀Fixture/브라우저Fake는 실AI품질을 확인하지 못한다는 제약과 baseline경고는 유지한다.

Task15 scoped 독립 리뷰는 `703acb9`의 7개 변경 파일을 검토하여 Spec compliant / Approved, Critical0 / Important0으로 통과했다. 이후 전체 브랜치 리뷰와 수정 검증은 아래 별도 기록이다. 구현자 자체검토를 독립리뷰로 계산하지 않는다. 부모가 계획checkbox/실행 기록 링크를 관리한다. ignored scratch는 모든 Ruling과 필요한 증거를 본 기록에 보존하고 최종 리뷰 뒤 정리한다.

## 전체 브랜치 리뷰와 한 번의 수정

독립 reviewer가 `8ba352399247733679edcea45fd22e29cbf96a67..23c48aa0f2f606d498ab2ffc84fee12756912ee7`의 39커밋·162개 파일을 검토했다. 판정은 **With fixes, Critical0 / Important3 / Minor1**이다. 기존 최종 green과 새 결함 발견을 구분한다. Backend/API/SQL/이미지/ZIP/UI/nav/E2E/docs를 대조했으며 후속 migration Designer는 첫 모델과 명칭 외 동일성도 확인했다. 기존 suite는 다시 실행하지 않았다.

| 지적 | 결함과 수정 방향 |
| --- | --- |
| I1/P1 | 부분 ZIP 완료가 실행 중 제외 대상보다 먼저 terminal을 만들어 공통 실행기가 늦은 결과 공개를 skip. 현재 공정 실행/대기를 우선 집계하고 저장 ZIP은 즉시 다운로드, 마지막 현재 공정 종료 뒤 export 소비. terminal/lease 보호 유지 |
| I2/P2 | 실제 ZIP의 productionMode/view/outputKind/repeat/layout은 숫자지만 Fake·문서 계약은 문자열. 패키지 전용 BCL camelCase enum converter와 실제 manifest JSON kind/value assertion |
| I3/P2 | loop 조건 때문에 완료/exportReady 정적 대상의 기준 재생성 버튼 누락. 기존 API로 같은 job 재개·해당 승인/current ZIP 무효화·다른 대상 결과 보존 |
| M1/P3 | X/Y 단일 행/열인데 3×3 안내. 기존 Both offset으로 최소3×3 검수 격자, 실제 선택 반복축 안내. 생성·manifest 반복 설정 유지 |

reviewer의 Domain DLL/F# stdin probe는 `job=PartiallySucceeded, terminal=true, sibling=Running/current=true`와 manifest enum `1/0/0/2/0`을 재현했다. 최초 harness FS0597 및 EOF exit1은 통과로 계산하지 않았고 `;;`/`#quit;;`를 보완한 최종 좁은 probe는 exit0이다. 실제 worker/ZIP 통합 실행을 대신하지 않는다. unchanged caller는 공유 fresh commit의 기존 `RunTaskHandler`/`RunGenerationTaskHandler`/`Stages`와 Golden `useJob` 소비 영향만 좁게 확인했다.

기존 deferred assertion Task5/10, readonly, 임시 Ignore/nav 예외는 해결을 확인했다. 과거 warnings는 유지하며 전체 리뷰의 새 지적과 중복 집계하지 않는다. Ruling01..61은 전부 triage했고 후속 판단은 아래 원문에 보존한다. 실 AI 품질/가용성, 운영 DB/배포/Git/인증 승인, SSH 취약성 악용·업그레이드 적합성, 실제256MiB/ZIP64/peak memory, liveAPI/실기기/접근성 전수 인증, Golden 전체 UX와 useJobCalls, 역사적 모든 suite 재실행, 후속 text/character/object/importer/과금 exactly-once, HEAD 이후 수정 완료를 범위 밖으로 둔 각각의 항목은 부모가 아래 Ruling으로 판정했다.

fresh 구현 담당이 I1/I2/I3/M1을 한 번의 fix wave로 맡았다. Backend targeted RED8/8 실패(지연 sibling6·실제ZIP enum2), FE RED8/8 실패(정적 재생성2·선택 축/격자6) 후 최소 수정했다. SQL reload의 목록 순서는 ID 조회로 확인하고 취소된 Pending의 기존 Skipped 계약은 유지했다. 소스 자체검토 뒤 GREEN Backend8/8·sprite E2E40/40·취소 재생성 잠금1/1을 확인했다. 최신 양 스택 전체 회귀와 한 번의 독립 scoped 재리뷰가 모두 통과했다.

최초 BE RED8건 중4건은 fixture의 SQL asset 목록 순서 가정 때문에 승인 이전 assertion에서 실패해 결함 재현 근거에서 제외했다. ID 조회로 고친 확정 RED8건은 실제 지적의 예상 상태·JSON kind 불일치에서 실패했다. 최초 BE 관련 GREEN107건은106통과/1실패였고 Pending 취소의 기존 `Skipped`를 `Canceled`로 잘못 기대한 assertion을 바로잡았다. 이번 nullable assertion 경고도 수정했다. terminal 보호는 변경하지 않았다. 잘못 찾은 `dist/.vite/manifest.json`의 MODULE_NOT_FOUND/exit1은 예산 근거에서 제외했고 실제 기존 bundle 검사 스크립트와 최종 build 결과만 사용했다.

## 수정 후 최신 자동 검증 (`9c74d12`)

`9c74d12d44eb79ee32e9cc67f661e0f9a7df198b`는12파일·207추가/23삭제다. 앱5파일·실제 SQL/ZIP/E2E/Fake와 관련 Backend/Frontend xHuman 문서만 scoped commit했다. 부모의 본 실행 기록은 제외했다. 자체검토 후12파일 SHA-256을 고정하고 전체 회귀 종료·commit 때 동일함을 확인했다. Backend build/test는 직렬, SQL collection 한 번, Frontend는 독립 병렬이다. 앱 코드 변경 뒤 전체 회귀를 한 번 수행했으며 부모의 문서 추가는 앱 소스 변경으로 세지 않는다.

| 명령 | 최신 결과 |
| --- | --- |
| `dotnet build apps/backend/Noxtend.slnx` | exit0, errors0, 기존 NU1903 warnings4 |
| `caffeinate -i dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'` | 1556 collected/passed, failed0, skipped0, exit0, 6m37s |
| `pnpm test` | 50 files, 422 collected/passed, failed0, skipped0, exit0 |
| `pnpm test:e2e` | 296 collected/passed, failed0, skipped0, exit0, 5.8m |
| `pnpm lint`·`pnpm typecheck` | 모두 exit0 |
| `pnpm build` | exit0, 초기JS113.66kB / 기존예산122.55kB, 여유8.89kB |
| `pnpm docs:check`·xHuman 로컬 링크·`git diff --check`·추가행 secret heuristic | exit0, 기존plan500행 경고 유지 |
| 기존 `.githooks/pre-commit` | staged docs + Frontend lint/Prettier 통과, 훅 우회 없음 |

SQL6건은 실제 pack worker·blocking generation worker·EF reload로 Pending/Running × 지연 성공/실패/취소를 확인한다. ZIP 저장 즉시 열기·current slot/task·소비 전null/종료 뒤marker·explicit reopen과 다른 승인/과거ZIP 보존·취소 재개 거부를 검사한다. ZIP2건은 반환 객체만이 아닌 실제 `manifest.json`의 enum JSON kind/value를 검사한다. FE는 정적 전체/부분 ZIP 후 재생성과6개 축/격자의 실제9개 image·안내·취소 잠금을 확인했다. Fake의 subset도 Running/생성 phase와 현재ZIP 다운로드 공존으로 동기화했다. 실AI·실기기·운영 환경을 검증한 의미는 아니다.

## 최종 독립 판정과 기록 보존

fresh reviewer가 `23c48aa..9c74d12`의 단일 수정 커밋·12파일을 재리뷰했다. **I1/I2/I3/M1 모두 ADDRESSED, 새 Critical/Important/Minor0, 미해결 지적0**이다. 다른 코드를 전체 재리뷰하지 않고 named boundary와 수정 diff만 확인했다. SQL/worker 결과 공개·소비 표식·명시 재개·취소, ZIP 실제 JSON, 정적 재생성·잠금,9개 타일·반복축 안내를 대조했다.

재리뷰는 worker의 RED/GREEN/full 원로그와 실제 assertion을 함께 대조했다. 최초 harness/중간 실패는 최종 pass에서 제외하고12개 frozen SHA-256을 재확인해 mismatch0/exit0이었다. 같은 코드의 suite를 반복하지 않았다. 커밋 로그에는 Frontend lint/Prettier 통과가 있으나 staged docs의 별도 출력은 없어 해당 세부 결과는 훅 정의·commit exit0·별도 docs 명령의 직접 출력과 구분한다. 부모의 마지막 docs 검사·scoped 기록 커밋은 앱 소스 변경 없이 수행한다.

부모의 최종 `pnpm docs:check`는 exit0이며 기존 plan500행 경고만 유지했다. 전체 브랜치와 마지막 문서 diff의 `git diff --check`도 exit0, 로컬 링크6개 존재, 추가 내용의 지정 secret token pattern0이었다.73개 Ruling 원문·시간순 일치와 미완료 checkbox0, 마지막 검증 커밋 이후 앱 diff0을 확인했다. secret heuristic을 모든 비밀값 부재의 보장으로 확대하지 않는다.

전체 리뷰의4개 지적은 한 번의 fix wave와 한 번의 scoped 재리뷰로 해결했다. Task1..15의 개별 gate와 전체 리뷰 gate가 완료됐다. 기존 high advisory·실AI/운영·실기기/대형부하 미검증 경계는 그대로다. 모든73개 Ruling 원문을 시간순으로 보존하며 대체된 즉시 취소 제안도 지우지 않는다. 마지막4173 listener 확인은 비어 있었고 새 managed worktree는 만들지 않았다. 전용 `.superpowers/sdd/2026-10-06-2d-background-sprites`만 정리하며 다른 작업폴더·저장소·컨테이너는 건드리지 않는다. 앱 소스·테스트·계약의 정본은 `9c74d12`, 이후 부모 커밋은 최종 계획·실행 기록뿐이다. Git 통합은 사용자 선택 전까지 미실행이다.

## 시간순 Ruling 원문

아래는 scratch `progress.md`의 모든 `Ruling:` 행을 기록 순서 그대로 보존한다. 임시Ignore·transport·실행방식·nav예외 등 이후 대체/제거된 판단과 오판시 비용도 생략하지 않는다.

Ruling: 승인된 기본 Subagent-driven 적용 — 이전 plan의 Native 권장보다 이후 사용자 지침 우선 — 작업별 새 context/review 비용 증가 가능

Ruling: 새 worktree 생성 동의 없이 현재 checkout의 새 codex 브랜치 사용 — 이미 승인된 로컬 구현 범위에서 기존 지침 변경 보존 — 다른 동시 작업과 파일 공유 위험

Ruling: SpriteFrameInput에 immutable SpriteAssetPlan Plan snapshot 추가 — phase/motion/transparency의 mutable 입력 재조회 방지 — 저장 task JSON의 신규 필드 확인 필요

Ruling: FPS/name/order 변경은 PlanRevision 유지, ReviewRevision만 증가 — 승인 spec의 무생성 metadata 변경 보장 — 생성 입력 분류 테스트 필요

Ruling: owned request는 Jobs projection으로 조회, 이미지/export는 job 소유 이력 — EF owned 조회와 대상 제거 뒤 과거 패키지 보존 — FK/cascade SQL 검증 필요

Ruling: Task1에는 초기 state와 Transform만, 이력 factory는 Task2에서 구현 — 독립 빌드와 빈 stub 방지 — Task2 구현 책임 증가

Ruling: 원본 생성 PNG 크기를 고정 GenerationCanvas와 대조 — 고정 변환의 재현성 유지 — 규격이 다른 공급자 출력의 명시적 거부

Ruling: Manifest에 ProductionMode 추가, ZIP은 spec의 asset-{id} 경로 — spec의 필수 계약 보존 — 후속 DTO fixture 갱신

Ruling: 정적 tileOffsets는 Task12에서 rules.ts에 구현하고 Task13이 재사용 — 미리보기 계산 중복 방지 — 구현 순서의 작은 변경

Ruling: Order 오름차순은 back-to-front, diamond와 맨 뒤 이외 layer는 투명 필수 — 사용자 bool의 알파 검증 우회 방지 — 모델 후보 제한

Ruling: 중복 order 거부, 이름 중복 허용 — 뒤 레이어와 합성 순서의 모호성 방지 — 같은 순서의 기존 client 입력은 거부 가능

Ruling: CreateSprites에서 text provider/model 인수 제거 — 분석 공정의 선택과 job state 중복 저장 방지 — Task7이 AnalyzeSprites task에 두 값을 저장해야 함

Ruling: 재현된 EF SpriteCanvas key 오류에 한해 Task1에서 Sprites/ProductionMode 임시 Ignore — Task4 이전 레거시 EF 모델 유지 — Task4의 제거·migration 누락 시 영속화 불가

Ruling: Task2의 실제 EF auto-discovery 실패에 한해 SpriteInput/SpriteExportInput/RequestId 임시 Ignore — 레거시 모델 유지 — Task4가 모두 제거·JSON/column 매핑하지 않으면 sprite task 영속화 불가

Ruling: export 유효성은 포함 approval snapshot/task frozen Input으로 확인하고 unrelated asset 수정은 현재/진행 export 보존 — spec의 영향 대상 한정 무효화 유지, 요청 expectedRevision은 global 유지 — snapshot 구조 비교 누락 시 오래된 package가 current로 인정될 위험

Ruling: plan 대상 추가/제거는 모든 export 자격 무효화 — included/excluded 목록과 전체/부분 종료 의미 갱신 — 이전 package는 history로만 남음

Ruling: SpritePipelineState.CompletedExportId(Guid?)로 이미 소비한 package 완료를 기록 — Phase 덮어쓰기 해결 뒤 explicit reopen이 old current export로 재종료되는 RED2건의 최소 상태 보존 — Task4 저장 누락 시 reload 후 동일 오류 재발

Ruling: Task4는 기존 AcceptSpriteRequest를 재사용하고 자동 configuration 등록 때문에 NoxtendDbContext 무의미한 수정 생략 — 기존 도메인 API/EF 등록 재사용 — 실제 configuration 연결 누락은 model/SQL 테스트로 검출 필요

Ruling: SpriteAsset에 Plan.Id와 동일한 실제 Id를 저장하고 ResetFrames는 동일 index 객체를 갱신 — GUID PK ValueGeneratedNever와 EF composite slot key 안정성 유지 — 두 ID 불일치/slot초과 제거 누락 시 저장 오류 위험, SQL/Domain tests 필요

Ruling: migration scaffold startup을 Infrastructure로 변경 — API에는 EF Design 참조가 없어 지정명령이 실제 거부되고 Infrastructure는 기존 DesignTimefactory/패키지 보유 — 도구의 factory 발견·생성SQL/작동 결과를 확인해야 함; 새 의존성/realDBapply 없음

Ruling: Task5 cap stream은 SpritePackageWriter 내부 internal 타입과 테스트 assembly 가시성 1줄로 직접 검증 — 계획의 작은 chunk 256MiB 경계 테스트를 public API/reflection 없이 구현 — 테스트 assembly의 내부 접근 범위 증가

Ruling: Task6 Files에 SpriteRules.cs 추가 — Interfaces의 GenerationCanvas 구현 대상이 파일 목록에서 빠진 문서 누락 보완 — 공유 규칙 변경의 회귀 범위 증가

Ruling: Task6 명시 sprite 표식은 ImageRequest.Background!=null이며 Google 사전 거부는 이 표식에 한정 — 기존 RunGenerationTaskHandler의 설정 가능한 options.Size가 Google에도 전달되는 실제 경로 보존 — Task8이 모든 sprite 요청에 Opaque/Transparent를 명시하지 않으면 사전 검증 누락 위험

Ruling: 신규 sprite 모델의 미등록 가격은 IsKnown=false/Estimate=null로 검증하고 기존 모델의 >0 가격 보장을 유지 — 전체 catalog의 가격 존재 assertion이 승인 spec의 단가 미확인 표현과 실제 충돌 — 신규 단가 seed 등록 시 이 명시적 unknown test 정책도 갱신 필요

Ruling: 새 sunburst는 Background=null legacy 선택에서도 response_format을 생략하고 output_format=png 사용 — 새 catalog 항목이 기존 3D picker에도 노출되고 공식 API가 GPT image response_format unsupported를 명시 — 기존 모델의 동작 보존과 신규 모델 transport를 구분하는 captured 테스트 필요

Ruling: 앞선 새 모델만 PNG transport 판정을 수정하여 catalog의 GPT image 공통 builder에서 미지원 response_format을 제거하고 output_format=png 명시 — 공식 API가 기존 gpt-image-2에도 같은 미지원 계약을 적용하며 공통 근본 원인 수정이 더 좁음 — 기존 captured payload assertion 변경 필요; 3D quality/size/n/base64·사용자 API는 보존

Ruling: Task6 ImageProviderUsageTests는 기존 검증으로 재사용하고 새 nullablePartId/Kind/배경 기록은 SpriteProviderRequestTests에서 검증 — 실제 호환되는 기존 테스트에 중복 assertion·무의미한 hunk 생성을 피함 — 새 recording 요구 누락이 없는지 task reviewer가 새 테스트/실행 근거 확인 필요

Ruling: Task7 SpriteImageInfo.ContentType은 실제 codec의 PNG/JPEG/WebP MIME을 명시하여 복사 metadata에 사용 — 기존 inspect 결과에 format이 없어 원본 metadata를 신뢰할 위험 — record 생성 지점과 픽셀 fixture의 최소 호환 갱신 필요; 임의 PNG 기본값 없음

Ruling: Task7 기존 PromptGridTests는 고정 row count 대신 실제 enum 전체 비교로 갱신하고 SpritePersistenceTests에 접수 경합·copy 원자성 검증 추가 — 새 operation 누락 방지와 기존 SQL fixture 재사용 — 기존 전체 operation 행 보존 및 loser ownBlob 정리 근거 확인 필요

Ruling: Task7 업로드는 codec MIME과 metadata 일치를 확인해 재사용하고 기존 생성 결과만 독립 복사 — 기존 마지막 SourceImageId 참조 삭제를 우회하는 업로드 원본 누수 제거 — 잘못 선언된 MIME 업로드는 2D 접수 거부; 공유·마지막 삭제·경합 loser 보존 검증 필요

Ruling: 실제 idle 수면 기록에 대응해 해당 test 프로세스 수명만 caffeinate -i로 보조 — 검증 중 호스트 idle 수면 방지와 종료 후 자동 해제 — 수면 외 환경 원인을 해결한 증거는 아니며 timeout·영구 설정·Docker daemon은 변경하지 않음

Ruling: 복사본 정리는 Add 확정 실패와 Save 이후 최신 접수 조회로 미참조가 확인된 경우만 수행하고 조회 불가 시 보존 — commit 후 응답 유실에서 정상 입력 삭제 방지, 기존 AsNoTracking receipt 포트 재사용 — DB 조회 불가에서는 Blob 누수가 남을 수 있으므로 경고·원예외 보존 및 실제 SQL 실패경계 테스트 필요

Ruling: Task9에 request 충돌 상수·Create 동일 ID 본문 충돌과 PackSprites TS wire/label 보완을 명시 — 현재 Result.cs는 revision 코드만 있고 plan9는 별도 request 코드·새 taskkind를 요구 — 기존 Create 오류 기대값 갱신 및 양쪽 parity 확인 필요

Ruling: Task8 호출 기록 EF 저장소는 기존 IDbContextFactory로 작업 Context와 분리하고 Add→Save 공통 계약 유지 — 실제 Recording의 scoped Context가 renewal Reload.Clear·기록 Save와 겹치는 구조 확인 — 새 소유 Context의 확정 disposal과 기록1회·DI·기존 recorder 회귀 필요

Ruling: Task8 OpenAI의 결제·quota429는 공식 error.type/code로 비재시도 분류하고 기존 ProviderHttp의 작은 OpenAI 전용 helper를 텍스트·이미지 양쪽에서 사용 — 기존 양 어댑터의 모든429 재시도가 실제 quota 즉시 실패 요구와 충돌, 같은 파서 복제 회피 — 일반429/5xx·다른 공급자 동작 보존과 sanitized error·malformed-body fallback captured 검증 필요

Ruling: Task8 claim 저장 후 기존 Task.RowVersion을 캡처하고 자기 renewal 저장 성공에서만 갱신하여 최종 공개에 대조 — SQL RED에서 수동 retry의 AttemptCount 초기화와 보존된 StartedAt이 이전 응답의 ABA 오인을 재현, 새 column 없이 기존 영속 토큰 재사용 — 타인의 version을 채택하면 소유권 결함 재발, 병렬 asset의 정상 저장을 과도하게 거부하면 회귀 가능

Ruling: Task10 Files에 기존 ApiResults.cs 오류 매핑을 명시 — request/revision/busy/mode 409와 소유 ID 404 계약은 현재 공통 매핑 파일 보완 없이는 지킬 수 없는 plan 파일 누락 — 기존 오류 매핑 회귀 검사 필요

Ruling: Task8 OpenAI quota captured 테스트는 기존 OpenAiProviderTests의 image/text parameter cases로 양 어댑터를 직접 검증하고 OpenAiImageRequestTests 기존 body 회귀를 재사용 — 같은 오류 파서를 두 파일에 복제하는 테스트와 무의미한 hunk 회피 — parameter image 경로 누락 시 이미지 adapter 검증이 비므로 독립 리뷰가 분기·실행 수 확인 필요

Ruling: Task8 renewal 중지 시 timer Delay만 linked token으로 취소하고 이미 시작한 SQL Reload/Save는 CancellationToken.None으로 마친 뒤 await — 실제 full에서 split query cancellation이 wrapped SQL exception으로 전파되어 정상 body 결과를 실패시키는 결함 재현 — host 취소 중 기존 SQL command timeout까지 진행 쿼리 종료를 기다릴 수 있음, 마지막 fresh 소유권/취소 검사 유지 필요

Ruling: Task9 분석 대기·실행 중 계획 편집을 거부하고 실패 분석 retry는 새 계획 없는 현재 분석에 한정 — 현재 ReplaceSpritePlan의 empty affected 목록과 분석 IsCurrentTask=true가 늦은 분석의 새 계획 덮어쓰기를 허용하는 실제 흐름 확인 — 분석 없이 선행 수동 편집은 제한되며 worker 성공 후 합법적 Replace commit을 막지 않는 검증 필요

Ruling: Task9 DeleteJobHandler·EF/InMemory PNG/ZIP 삭제 수집은 Task4/7의 현재 구현을 end-to-end 검증해 재사용하고 무의미한 소스 hunk 생략 — 기존 IBlobStorage Images 경로가 이미 두 결과를 삭제하고 upload 참조 수를 보존 — 실제 pack 생성 뒤 삭제 검사 누락 시 ZIP 누수를 놓칠 위험

Ruling: Task9 최초 request miss 뒤 job을 읽은 시점에 승자 receipt를 다시 확인하여 state/revision 거절보다 우선 반환 — 실제 interleaving miss→다른 Context commit→fresh job revision에서 중복 접수가 revision conflict로 잘못 거절될 경로 확인 — 추가 DB 조회 비용, winner 이후 cancel 상태에서도 기존 접수를 우선하고 다른 본문 충돌을 보존하는 SQL gate 검증 필요

Ruling: Task11 공유 목록의 실패→빈 성공 fallback을 제거하고 상세 isNotFound를 실제404로 한정해 error/isError를 반환, Task12 새 화면·Task14 홈/기존스튜디오에서 소비 — 실제 현재useJobList catch-all과 useJob query.isError 매핑이 새2D에도 오류를 빈목록/없는작업으로 숨길 경로 확인 — 기존 오류 표시가 바뀌며 legacy 화면의 추가 오류 분기·E2E 회귀 필요

Ruling: Task12 Files에 기존 rules.ts/rules.test.ts의 tileOffsets와 spriteStyles.ts를 명시 — 이미 승인된 정적 타일 계산 및 기존 스타일 파일 패턴의 실제 구현 대상 누락 보완 — Task11 규칙 회귀와 토큰 재사용 확인 필요

Ruling: Task14 공유 작업 query 오류 소비 범위에 기존 CallsScreen도 포함 — 실제 useJobList의 홈 외 유일한 호출자가 jobs.length=0을 빈 성공으로 표시하는 흐름 확인 — 호출 내역 화면의 오류 분기와 기존 E2E 회귀 추가 필요, useJobCalls 자체 계약은 확장하지 않음

Ruling: Task9의 이미 소비한 pack 실패도 기존 CompletedExportId로 기록하고 해당 pack의 명시적 retry에서 소비 표식을 초기화 — 독립 리뷰가 재현한 제외 asset 재생성 뒤 과거 실패의 재종료 차단, 새 영속 column 회피 — retry 초기화·SQL reload 누락 시 정상 재실패/성공 종료를 놓칠 위험

Ruling: Task11 sprite PNG/ZIP URL은 기존 app/queries/media.ts의 재수출 경로를 재사용 — 현재 source/generated/mesh도 이 경계를 사용하며 화면의 infra import를 방지 — URL builder와 export 연결의 계약 검사 필요, 새 wrapper 계층 없음

Ruling: Task10의 3D 모드 차단을 기존 scene-layout 생성·복원과 similarity 접수의 공통 Application 진입점에도 적용 — 실제 빈 Parts를 허용하는 scene 저장과 StartSimilarity의 Any 검사에서 완료 TwoD가 평가 queue까지 진입할 경로 확인, UI 자격 표시는 접수 검증을 대신하지 못함 — 관련 handler의 TwoD 거부·무저장/무dispatch RED/GREEN과 레거시 회귀 범위 증가

Ruling: Task15 README의 3D 전용 소개에 실제 2D 제공 범위를 동기화하고 검증·전체 판단은 연결된 execution.md에 보존 — 현재 README에 sprite 설명이 없고 SDD scratch 정리 뒤에도 구현 증거·판단을 검토 가능하게 유지 — 문서 범위 증가, Fake 검증을 실AI 품질·배포 완료로 과장하지 않는 대조 필요

Ruling: Task11 layerRules의 모든 import 금지를 실제 AGENTS의 순수 Domain 계층 경계로 좁혀 동일 Domain 타입 공유 허용 — job/provider가 sprite 타입을 중복 정의하지 않고 참조하도록 기존 검사의 과한 조건 수정 — 외부 라이브러리·바깥 계층 차단이 약해지지 않는 검증 필요

Ruling: Task11 useProviders의 실패를 삼키는 빈 성공 fallback 제거와 error/isError/refetch 노출 — 새 2D 입력이 기존 훅을 재사용하면서 실제 공급자 조회 실패를 등록 0건으로 오해하지 않도록 공통 원인 수정 — Task12 오류 E2E와 Task14 기존 스튜디오의 명시 오류 표시 필요

Ruling: Task11 Files에 useSprites.test.ts를 추가해 기존 Vitest로 공통 query 오류·signal·polling·409와 안정된 mutation 변수를 검증 — API 본문 테스트만으로 cache/hook 회귀를 확인할 수 없어 실제 Query 옵션 경계를 작은 mock으로 확인 — 라이브러리 내부 동작·실제 브라우저 연결까지 검증한 것으로 과장하지 않는 Task12 E2E 필요

Ruling: Task12 app/queries/errors.ts에 apiErrorStatus(error)를 추가 — 409 충돌과 전송 실패 재시도를 기존 오류 경계에서 구분해 feature의 infra import·임의 status cast·오류 code 사본 방지 — null/HTTP 상태 구분 및 실제 화면 재시도 E2E 필요

Ruling: Task14 기존 결과 진입은 Background/Character가 공유하는 PartGallery의 현재 이미지 Carousel에서 job.id/imageId를 전달 — 동일 이미지 기능을 화면마다 복제하지 않는 공통 진입점, 현재 이미지에만 새 행동 제공 — modal 현재 방향·소유 job/image 쌍과 이동 후 포커스/URL 확인 필요

Ruling: Task12 약600행 spriteFakeApi를 계획의 시나리오·요청 기록·PNG/ZIP fixture 단일 경계에 유지 — 행 수만으로 자율 분할하거나 새 테스트 프레임워크를 만들지 않고 실제 책임·중복은 독립 리뷰로 판단 — fixture와 상태 전이 책임이 얽히면 후속 animation 변경·회귀 원인 파악 비용 증가

Ruling: Task12 select를 기존 Radix 프리미티브로 전환하고 nativeSelect guard 유지 — OS 렌더링 메뉴의 토큰 불일치를 막는 실제 기존 정책과 가장 먼저 재사용할 공통 프리미티브 보존 — 이미 통과한 브라우저 입력 선택·키보드 검사 수정·재실행 비용

Ruling: Task12 navItems.test에 ROUTES.spriteBackground 한 경로만 Task14 메뉴 연결 전 임시 제외 — 순차 계획에서 Task12 URL 구현과 Task14 메뉴 구현의 검사 범위 충돌 해소 — Task14에서 제거하지 않으면 실제 메뉴 누락을 검사에서 숨기는 위험

Ruling: Task12 실제 tileOffsets(width,height,repeat,layout) 내부 signature 유지와 Task12/13 계획 동기화 — 저장된 square/diamond layout을 직접 사용하고 동작·외부 계약 변경 없이 재사용 — view/layout 혼동으로 diamond 축 계산 오류가 생기면 호출부와 격자 테스트 보완 필요

Ruling: Task12 fix round1에서 tileOffsets readonly 반환 선언도 명시 계약에 맞춤 — 동작 변경 없는 한 줄이며 spec signature 불일치를 다음 단계로 넘기지 않음 — 해당 타입이 mutable 사용을 요구하면 기존 호출의 typecheck 보완 필요

Ruling: Task14 GoldenRunsScreen은 사용 인터페이스·의존 영향만 확인하고 명시 Home/Studio/Calls 범위 유지 — 기존 golden 관찰 UI의 관련 없는 변경을 묶지 않음 — 새 쿼리 오류 의미가 실제 깨진 동작을 만들면 조건을 확인하여 최소 범위 보완 필요

Ruling: Task14 MobileStudioMenu.tsx 한 파일 추가로 모바일 Radix 그룹 메뉴 lazy 경계 분리 — 기존 primitives·desktop 구조·초기 JS122.55 예산을 유지하고 새 라이브러리/상태 프레임워크를 도입하지 않음 — 경계 로딩 시 키보드·focus·직접 주소 접근이 깨지거나 desktop에서 불필요 로드되면 해당 회귀·실제 로딩 조건 보완 필요

Ruling: 최종 리뷰의 부분 ZIP 성공은 승인 spec의 부분 성공 종료·명시적 재개·공통 terminal skip 유지에 따라 남은 미종료 공정을 취소하고 이미 생성·승인한 결과는 보존 — SQL에 Running/current를 남긴 terminal을 방지하며 완료 후 추가 제작은 기존 regenerate/retry로 접수 — subset ZIP 완료 시 진행 중이던 제외 대상의 미공개 출력은 폐기될 수 있고 유료 호출이 이미 시작됐으면 비용은 남음

Ruling: 앞선 부분 ZIP 즉시 취소 판단을 대체하여 현재 공정 실행/대기를 export 완료 소비보다 먼저 집계 — spec178의 명시 우선순위·기존 공정 결과 보존·더 작은 상태 전이 수정을 함께 지킴, 저장된 ZIP은 즉시 다운로드 가능하고 마지막 현재 공정 종료 후 부분 성공을 소비 — 남은 공정의 재시도/대기가 길면 job 종료 표시가 ZIP 준비보다 늦어질 수 있음

Ruling: 최종 Minor M1은 spec126과 plan474의 최소3×3 검수 격자를 적용하고 실제 반복축을 안내 — 기존 tileOffsets의 Both 격자를 미리보기에서 재사용하며 생성 설정·manifest의 X/Y/Both는 유지 — 비반복축 경계도 표시하므로 사용자가 해당 축까지 seamless 요구로 오인하지 않도록 선택 축 안내 필요

Ruling: 최종 리뷰의 실 AI 품질·credential별 가용성 Declined 항목은 미확인으로 유지 — 유료 호출 승인 없이 Fake/픽셀 결과를 실 모델 품질로 확대하지 않음 — 실제 alpha·시점·반복·loop 품질 실패는 이후 실 AI 검수에서 드러날 수 있음

Ruling: 운영/개발 DB 적용·배포·push/merge·인증/키/네트워크 승인 Declined 항목은 이번 구현 완료와 분리 — 격리 SQL 검증과 로컬 브랜치 작업만 완료 근거로 사용 — 실제 환경 적용·노출·키 보존·통합은 별도 실행 증거가 필요

Ruling: 기존 SSH.NET advisory 악용 가능성·업그레이드 적합성 Declined 항목은 high 경고를 남긴 별도 유지보수로 기록 — 이번 sprite 기능과 무관한 의존성 교체를 묶지 않고 안전 판정을 하지 않음 — 취약 의존성이 남으므로 실제 SSH 경로·운영 노출에 대한 보안 판단이 필요

Ruling: 실제256MiB ZIP·ZIP64·최대 작업 peak memory/처리시간 Declined 항목은 미실측 유지 — 작은 chunk 상한/픽셀/layout 검증을 실제 대형 부하 증거로 확대하지 않음 — 큰 작업의 자원·시간 한계는 실제 부하에서 추가 확인 필요

Ruling: 실제 API 호스트·터치기기·접근성 전수 인증 Declined 항목은 미검증 유지 — preview/Fake/Chromium의 폭·키보드·focus·reduced-motion 관찰 범위만 인정 — 실제 기기/호스트의 추가 오류나 접근성 문제는 남을 수 있음

Ruling: GoldenRuns 전체 오류 UX·useJobCalls 계약·관련 없는3D 정리 Declined 항목은 승인 Home/Studio/Calls 범위 밖으로 유지 — reviewer가 확인한 Golden의 job-only 소비 영향만 인정 — Golden 전체 품질과 별도 호출 내역 계약까지 승인한 의미는 아님

Ruling: 과거 임시 Ignore/nav 예외와 모든 중간 suite 재실행 Declined 항목은 현재 코드·최종 회귀·Git/RED 기록 대조로 처리 — 제거된 개발 중간 상태를 현재 결함으로 계산하지 않음 — 과거 모든 실행을 새로 재현한 증거는 아님

Ruling: text 입력·2D character/object·engine importer·provider 과금 exactly-once Declined 항목은 후속/제외 범위로 유지 — 승인된 이미지 기반2D background 제작만 구현 완료로 보고 — 해당 추가 기능과 retry/regen 포함 실제 과금 상한은 제공하지 않음

Ruling: HEAD 이후R62–R64 실제 완료 Declined 항목은 fix wave·최신 전체 회귀·한 번의 scoped re-review가 끝날 때까지 pending — 제안과 구현 검증을 구분 — 수정 실패나 새 breakage가 남으면 final cap 판정에 명시 필요

## 2026-10-07 분석 오버레이 통일

- 실행 방식: Native 직접 구현·독립 리뷰. 사용자 첨부의 기존 분석 UI를 기준으로 같은 공통 컴포넌트를 연결하는 결합된 수정이므로 구현 분담 없이 진행
- `SpritePlanReview`의 별도 ROI 박스를 `PartsOverlay`로 교체하고 미저장 이름·좌표를 번호 라벨·대상별 색상 박스·앵커에 반영. 숫자 ROI 편집·저장·승인 계약 유지
- 모바일 캡처에서 기존 퍼센트 간격의 라벨 겹침 확인. 공통 칩을 720px 이하에서 이미지 아래 줄바꿈 목록으로 배치하고 이미지 좌표·편집 프레임 유지
- RED: 공통 오버레이 누락 및 모바일 긴 이름 겹침을 새 E2E로 각각 재현. GREEN: 새 2D·기존 3D·검수 E2E 12개 통과, 데스크톱·390px 캡처 확인
- 독립 리뷰의 모바일 겹침 지적 수정 후 재리뷰 완료. 추가 수정 필요 사항 없음
- 검증: `pnpm test` 422개, `pnpm lint`, `pnpm typecheck`, `pnpm build`, 전체 `pnpm test:e2e` 297개(6.0분) 통과. `pnpm docs:check`·`git diff --check` 통과, 기존 500행 초과 계획 문서 경고 유지
- 범위: Fake API·Chromium 검증. 실 AI 호출·Backend 변경·배포·Git 작업 미실행

### main PR 통합 전 검증

- Backend build·유료 smoke 제외 전체 회귀 1,556개 통과(실패0·건너뜀0, 6분29초). Backend 소스는 기존 검수 완료 HEAD와 동일
- Frontend 422개·E2E297개·lint·typecheck·build 통과 결과 유지. 이후 앱 소스 변경 없음
- 로컬 기동 mock 5개·Bash 문법·커밋 훅 2개·문서/공백 검사 통과. 로컬 실행 개선·개발 API 설정을 포함한 새 변경 독립 검토 완료
- `origin/main`은 기능 브랜치의 조상이며 대상은 사용자 요청의 `main`. 실제 AI·배포 실행 없이 PR 통합 준비
