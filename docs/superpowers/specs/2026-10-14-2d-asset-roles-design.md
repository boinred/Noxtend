# 2D 타일 작업 에셋 역할 설계

2026-10-14 대화에서 섹션별로 승인받은 설계를 정리한다. 2D 배경 장면 재조립·원본 비교는 두 단계로 나눴고, 이 문서는 1단계인 에셋 역할 분류와 역할별 캔버스만 다룬다. 2단계(서버 이미지 매칭 장면 재조립과 비교 슬라이더)는 이 설계가 끝난 뒤 별도 spec으로 작성한다. 기반 설계는 [2D 배경 스프라이트 설계](2026-10-06-2d-background-sprites-design.md)다.

## 목적

- 타일 작업(`outputKind: tiles`)의 분석이 반복 불가능한 원경·낱개 물체까지 128px 타일로 계획하는 문제를 고친다. 예: 작업 `300f237d-73ba-42ea-8aaf-9fafae4006f6`(아이소메트릭·128px)의 `distant_skyline_tile`은 원경 배경, `terracotta_roof_tile`은 오브젝트여야 한다.
- 한 타일 작업 안에 반복 타일·원경 배경·배치 오브젝트를 함께 계획·생성·검수·내보내기 한다. 기반 설계의 "한 작업은 레이어 또는 타일 중 한 결과 유형" 원칙을 타일 작업에 한해 바꾼다.
- 오브젝트·원경은 타일과 같은 월드 축척으로 출력해 게임에 바로 배치할 수 있게 한다.

성공 기준:

- 분석 v3가 타일 작업의 에셋마다 역할을 내고, 역할·투명도·원경 개수 위반은 재질의(`Retry`)로 처리된다.
- 계획 검수에서 역할과 격자 칸 너비를 바꿀 수 있고, 바뀐 에셋만 다시 생성된다.
- 내보낸 ZIP의 manifest v2에 역할·에셋 캔버스·축척·원본 영역·격자가 들어간다.
- 역할이 없는 기존 작업은 동작이 바뀌지 않는다.

## 범위

포함:

- `SpriteAssetPlan.role`, 계획 수준 `grid.cellWidth`, 에셋별 출력·생성 캔버스와 transform
- 분석 프롬프트 v3, 생성 프롬프트 개정, 파서·도메인 검증
- API 계약, 계획 검수·미리보기·검수·분석 요약 화면, manifest v2

제외(2단계 또는 후속):

- 서버 이미지 매칭 장면 재조립(격자 보정, 칸별 타일 매칭, 오브젝트 인스턴스 탐색)과 원본 비교 슬라이더
- 오브젝트 여러 인스턴스의 배치 데이터
- 기존 작업 재분석 버튼
- 타일 seamless 경계·원경 품질 같은 생성 품질 검사

## 결정 사항

| 항목 | 결정 | 이유 |
|---|---|---|
| 결과 구성 | 타일 작업 하나에 `tile`·`backdrop`·`object` | 원본 장면을 한 작업의 에셋으로 재현하기 위함(사용자 선택) |
| 레이어 작업 | `layer` 역할만 허용, 동작 변화 없음 | 요청 범위가 타일 작업 |
| 원경 개수 | 타일 작업당 최대 1개, 가장 뒤 순서 | 격자 밖 원경은 장면 뒤에 한 장 깔림 |
| 출력 크기 | 타일 기준 월드 축척 | 게임에 바로 배치(사용자 선택) |
| 오브젝트 상한 | 월드 축척에서 한 변 1024px 초과 시 계획 거부 | 축척을 깨지 않기 위해 축소하지 않음 |
| 원경 상한 | 긴 변 1024px로 축소 허용, 비율을 `scale`로 기록 | 원경은 엔진에서 확대해도 무방 |
| 생성 캔버스 | 계획 확정 시 이미지 모델 지원 크기 중 비율이 가장 가까운 값 | 기존 capability 조회 재사용, 작업에 크기 목록을 저장하지 않음 |
| 투명도 | 역할이 결정, 화면 체크박스는 읽기 전용 | 모델·사용자 값과 규칙의 불일치 방지 |
| 기존 작업 | 역할 없음 → 유형 기본값(`layers`→`layer`, `tiles`→`tile`) | 호환 |

## 확인한 현재 구현

- `SpriteAssetPlan`(`apps/backend/Noxtend.Domain/Sprites/SpriteTypes.cs`)에 역할·크기 필드가 없다. 에셋별 시각 속성은 `RequiresTransparency`뿐이다.
- 캔버스는 작업 단위(`SpritePipelineState`의 `SourceCanvas`·`GenerationCanvas`·`OutputCanvas`·`Transform`)다. `SpriteRules.OutputCanvas`는 타일이면 `tileWidth × (아이소메트릭이면 절반)`이다.
- 투명도 규칙·새 에셋 앵커·다이아몬드 layout·미리보기·내보내기 경로가 모두 작업 `OutputKind`로 분기한다(`SpriteRules.Validate`, `PipelineJob.Sprites.cs`의 `ReplaceSpritePlan`·`SpriteApproval`, `RunSpriteGenerationTaskHandler`, `SpritePackageWriter`, `SpritePreview.tsx`).
- `SpritePlanParser`는 `view`·`outputKind`가 작업 설정과 같아야 하며 에셋 키 집합을 고정한다. 분석 프롬프트 변수는 `settings`·`sourceCanvas`, 생성 프롬프트 변수는 `settings`·`asset`·`frame`·`sourceCanvas`·`outputCanvas`(`PromptTemplate`)다.
- 계획 검수는 `PUT plan`으로 에셋 목록 전체를 바꾼다. `GenerationChanged`는 영역·투명도·루프·프레임 수·동작 메모 변경만 이미지 무효화로 본다.
- 3D 배경 분해에는 면을 덮는 표면 파츠와 낱개 오브젝트를 나누는 선례(`BackgroundSurfaceRule`)가 있다.

## 데이터 모델

역할:

- `SpriteAssetRole`: `layer`, `tile`, `backdrop`, `object`.
- 레이어 작업은 `layer`만, 타일 작업은 `tile`·`backdrop`·`object`만 허용한다. `backdrop`은 최대 1개이며 순서가 가장 뒤다.
- 역할별 투명도: `tile`은 아이소메트릭이면 투명(기존 규칙), `object`는 항상 투명, `backdrop`은 불투명, `layer`는 기존 규칙(가장 뒤 제외 투명).

격자:

- 타일 작업 계획에 `grid: { cellWidth }`를 둔다. `cellWidth`는 원본 너비 대비 한 칸(아이소메트릭이면 다이아몬드 한 칸)의 가로 폭이며 0 초과 1 이하다.
- 분석 v3가 추정하고 계획 검수에서 고칠 수 있다. 값이 없는 기존 타일 작업은 `tile` 에셋 원본 영역 너비의 중앙값을 쓴다.

월드 축척과 에셋 캔버스:

- 축척 `s = tileWidth / (cellWidth × 원본 너비 px)`.
- `tile`: 기존 `SpriteRules.OutputCanvas`와 같다.
- `object`: 원본 영역 픽셀 크기 × `s`를 반올림한다. 한 변이라도 1 미만이거나 1024 초과면 계획 검증 실패다.
- `backdrop`: 원본 전체 × `s`. 긴 변이 1024를 넘으면 1024로 맞춰 축소하고 그 비율을 `scale`로 기록한다. 그 외 에셋의 `scale`은 1이다.
- `layer`: 기존 작업 단위 출력 캔버스와 같다.
- 계획 확정(분석 결과 반영·계획 수정) 시 에셋마다 출력 캔버스·생성 캔버스·transform을 계산해 `SpriteAsset`에 저장한다. 생성 캔버스는 이미지 모델 지원 크기 중 비율이 가장 가까운 값이다(`SpriteRules.GenerationCanvas` 재사용). 생성 입력(`SpriteFrameInput`)은 에셋 캔버스를 고정한다.
- 앵커는 `layer`·`backdrop`이면 `(0,0)`, `tile`·`object`이면 출력 캔버스 중심이다. 새 에셋뿐 아니라 기존 에셋의 역할·출력 크기 변경에도 재계산한다. 저장 앵커·생성 프롬프트의 `frame.anchor`·승인 snapshot·manifest가 같은 기준을 사용한다.

저장:

- 계획 JSON에 `role`을 추가한다(없으면 유형 기본값으로 읽음).
- `SpriteAssets`에 에셋 캔버스 JSON(출력·생성·transform·scale), `Jobs`에 `SpriteGridJson`을 nullable로 추가한다. 에셋 캔버스가 없는 기존 행은 작업 단위 캔버스로 읽는다.
- 작업 단위 캔버스 필드는 호환과 `tile` 기준값으로 유지한다.
- `SpriteJsonSerializer`는 개별 계획의 ID·영역·프레임 값과 선택 역할의 enum 유효성을 검사한다. 모든 계획을 임의의 `Layers` 설정으로 검증하지 않는다. 작업 유형별 역할·투명도·원경 개수·순서·격자 등 전체 계획 규칙은 실제 설정과 전체 에셋을 받는 `SpriteRules.Validate`에서 검사한다. 기존 개별 값 검증을 제거하거나 완화하지 않는다.
- 저장 envelope의 `SchemaVersion` 1과 내보내기 payload의 버전은 별개다. 내보내기 입력·manifest는 v1과 v2를 버전별로 검증하며, v2 manifest와 그 입력의 버전이 일치해야 한다. 구형 필드 생략 허용을 새 v2 필수값 누락 허용으로 확대하지 않는다.

## 프롬프트

분석 v3(`AnalyzeSprites` Background 슬롯 개정):

- 에셋마다 `role` 필수. 타일 작업은 루트 `grid.cellWidth` 필수, 레이어 작업은 `grid: null`.
- 분류 규칙을 명시한다.
  - `tile`: 격자에서 끊김 없이 반복되는 바닥·벽면·물·도로 같은 표면. 한 칸 크기의 대표 영역 하나.
  - `backdrop`: 격자 밖 반복되지 않는 원경(하늘·스카이라인·산). 최대 1개, 가장 뒤.
  - `object`: 자기 윤곽을 가진 낱개 물체(지붕·차양·물탱크·전봇대·관목·간판). 대표 인스턴스 하나의 영역.
  - 반례: 이름에 tile이 붙어도 반복되지 않으면 `tile`이 아니다.
- `cellWidth` 정의와 비율 좌표 예시를 v2의 정규화 좌표 안내 방식으로 넣는다.
- 파서는 역할의 유형 허용 여부, `backdrop` 1개 이하, 역할별 투명도 일치, `grid` 범위를 검증하고 위반 시 `Retry(SpritePlanInvalid)`로 다시 묻는다. 값을 조용히 고치지 않는다.
- Fake LLM은 타일 작업에서 `tile`·`backdrop`·`object`를 하나씩 낸다.

생성 개정(`GenerateSprite` 첫 개정):

- 레이어·타일 지침이 섞인 문단을 역할별로 나눈다. `tile`은 기존 다이아몬드 2:1·seamless 지침 유지, `backdrop`은 캔버스를 채우는 불투명 원경과 전경 물체 금지, `object`는 투명 배경 위 해당 물체 하나와 잘림 없음·원본 시점·조명 유지.
- 프롬프트 변수는 바꾸지 않는다. `asset`에 `role`이 포함되고 `outputCanvas`에 에셋 캔버스가 들어간다.

시드 규칙: 기존 seed·migration은 수정하지 않는다. 개정은 슬롯 비활성화 후 `MAX(Version)+1`, Down은 해당 행 삭제 후 최고 버전 재활성화다.

## 생성·검증

- 공급자 요청 크기와 배경은 에셋 생성 캔버스와 역할별 투명도를 쓴다.
- 정규화(`NormalizeSpriteAsync`): 다이아몬드 clip은 아이소메트릭 `tile`에만 적용한다. `object`는 투명 픽셀을 요구한다(기존 투명 요구 검사). `backdrop`은 불투명 요청이며 레이어 작업의 불투명 맨 뒤 레이어와 같은 contain 정규화를 쓴다(cover 정규화는 만들지 않음, 비율이 가장 가까운 생성 캔버스라 여백은 작음).
- 이미지 첨부(`TryAttachSpriteImage`)는 에셋 출력 캔버스 크기와 비교한다.
- 투명 픽셀이 없는 `object` 등 생성 결과 검증 오류는 기존 `Fail(ProviderBadResponse)`를 유지한다. 자동 이미지 재생성을 추가하지 않는다. 분석 JSON의 역할·격자 오류에 대한 재질의와 구분하며, 기존 일시적 공급자 오류의 재시도 정책은 유지한다.

## 상태 전이와 오류

- `GenerationChanged`에 유효 `role`과 계산된 에셋 캔버스를 추가한다. 역할 변경, 또는 격자 변경으로 `object`·`backdrop` 캔버스가 바뀌면 해당 에셋의 이미지·승인을 무효화하고 계획 검수로 돌아간다. 역할만 바뀐 경우 내보내기는 해당 에셋을 포함한 ZIP만 이전 결과로 표시한다. `tile` 캔버스는 격자 영향을 받지 않는다.
- 변경 없음 판정은 계획 목록·저장 격자·유효 에셋 캔버스를 함께 비교한다. 격자만 달라도 저장과 `ReviewRevision` 증가가 필요하다. 캔버스가 그대로인 에셋은 `PlanRevision`·이미지·승인을 유지하고 새 생성 공정을 만들지 않는다. 변경된 캔버스의 대상은 계획 목록이 같아도 무효화 대상에 포함한다.
- 격자는 manifest의 작업 수준 메타데이터이므로 저장 격자가 바뀌면 모든 기존 ZIP을 이전 결과로 표시한다(파일은 보존). 이미지 변경 없이 최종 승인된 대상은 재승인 없이 새로 내보낼 수 있으며, 완료 상태가 기존 ZIP을 현재 결과로 계속 표시하지 않게 한다.
- 생성 중 에셋 변경은 기존 `SPRITE_BUSY`, revision 충돌은 409다. 새 오류 코드 없이 계획 검증 실패 메시지를 추가한다: 원경 1개 초과, 오브젝트 크기 초과, 유형에 없는 역할, 역할과 투명도 불일치, 격자 범위.
- 이미지 모델 지원 크기 조회 실패는 계획 확정 실패로 알리고 재시도하게 한다. 기본 크기로 대체하지 않는다.

## API

- `SpriteAssetPlanRequest`(응답 `Plan` 겸용)에 `role` 추가. 요청에서 생략하면 유형 기본값, 알 수 없는 값은 400.
- 계획 요청에 `grid`, `SpriteResponse`에 `grid`(`{ cellWidth } | null`) 추가.
- 계획 수정 fingerprint는 ID로 정렬한 에셋 목록과 `grid`를 포함한다. 동일 requestId·동일 본문 재전송은 기존 receipt, 같은 requestId·다른 격자는 `SPRITE_REQUEST_CONFLICT`다. 기존 접수 확인은 revision·공급자·모델 조회보다 먼저 수행한다. 요청에서 생략한 `grid`와 명시적 `null`은 같은 입력으로 취급하며, 계산 시 기존 중앙값 규칙을 사용한다.
- `SpriteAssetResponse`에 `canvas`(출력)와 `scale` 추가.
- Frontend `readPlan`·`readState`는 새 필드를 엄격히 읽고, 역할이 없는 구형 응답은 유형 기본값으로 읽는다. `spriteApi.ts`의 요청 직렬화에 `role`·`grid`를 넣는다.

## 화면

- 계획 검수(`SpritePlanReview`, 타일 작업): 에셋별 역할 선택(반복 타일·오브젝트·원경 배경), 역할이 정한 투명도 표시(읽기 전용), 격자 칸 너비 입력, 에셋별 계산 출력 크기(`출력 196×140px`), 서버와 같은 클라이언트 검증과 행 단위 오류·해결 안내. 원본 영역 오버레이에 역할 라벨을 붙인다(색만으로 구분하지 않음).
- draft·저장 기준값·dirty·서버 응답 동기화는 에셋 목록과 격자를 함께 다룬다. 격자만 수정해도 저장할 수 있고, 폴링으로 미저장 격자를 덮어쓰지 않는다. 409에서는 입력을 보존하고 사용자 재조정 없이 새 revision으로 자동 제출하지 않는다. 최신 계획 다시 불러오기는 격자도 함께 복원한다.
- 미리보기(`SpritePreview`): 작업 단위 분기를 에셋 단위로 바꾼다. `tile`은 기존 3×3 반복, `object`·`backdrop`은 자기 캔버스 크기 한 장을 체커보드 위에 표시한다.
- 검수(`SpriteFrameReview`): 에셋 제목 옆 역할 배지.
- 분석 요약(`SpriteAnalysisPanel`): `반복 타일 4 · 오브젝트 3 · 원경 배경 1`처럼 역할별 수.
- 레이어 작업 화면은 바뀌지 않는다.

## 내보내기

- manifest `schemaVersion` 2. 기본 이미지 경로를 역할별 `tiles/`·`objects/`·`backdrop/`·`layers/`로 나눈다.
- 에셋마다 `role`·`canvas`·`scale`·`sourceBounds`, 작업 수준에 `grid`·`tileWidth`를 추가한다. 시트 분할은 에셋 캔버스 기준이다.
- 이미 만든 ZIP은 그대로 두고 새로 내보내면 v2가 된다.
- 기존 승인 snapshot에 새 필드가 없으면 새 내보내기를 접수할 때 유효 역할·작업 단위 캔버스·`scale: 1`과 승인 revision이 일치하는 계획의 `sourceBounds`로 v2 snapshot을 만든다. 승인된 이미지 ID·이름·순서·FPS 등 기존 승인값은 유지하고 재생성·재승인을 요구하지 않는다. 타일 작업의 내보내기 격자는 저장값 또는 중앙값으로 확정하고, 레이어 작업의 `grid`·`tileWidth`는 null이다.
- 이미 접수된 v1 내보내기 공정은 저장된 입력만 사용해 v1으로 재시도한다. 현재 계획으로 재구성하거나 v2로 승격하지 않으며, ZIP 경로와 manifest의 기존 필드 구성을 유지한다. 새 v2 필드가 v1 manifest에 추가되지 않도록 버전별 출력 형태를 검증한다.
- v2 신규 접수는 writer·입력/manifest 저장 검증이 함께 준비된 구현 계획 Task 5에서 활성화한다. 그 전 Task의 기존 v1 회귀를 유지한다.

## 테스트

Fake 공급자 기준이며 유료 smoke는 제외한다.

| 계층 | 확인 |
|---|---|
| Domain | 역할별 투명도, 원경 1개·가장 뒤, 월드 축척 캔버스(1024 상한·`scale`), 역할·격자 변경의 무효화 범위, 역할 없는 계획 기본값 |
| Application | v3 파서(역할·grid 필수, 위반 시 Retry), 에셋 캔버스로 만든 생성 요청 크기·배경, 지원 크기 조회 실패, 계획 수정 시 캔버스 재계산 |
| Infrastructure | 원경 불투명 정규화, 오브젝트 투명 요구, 다이아몬드 clip은 타일만, manifest v2 경로·필드, 프롬프트 migration up/down·`MAX+1`·운영자 슬롯 보존, 영속성 왕복과 구형 행 읽기 |
| API | 새 필드 직렬화, `role` 생략 기본값, 잘못된 역할 400, 구형 계획 응답 |
| Frontend 단위 | wire 파서 새 필드·구형 기본값, 출력 크기 계산, 요청 직렬화 |
| E2E | 타일 작업 3역할 계획 → 생성 → 검수 → 내보내기, 역할 변경 시 해당 에셋만 재생성, 계획 오류 표시, 390px |
| 회귀 | Backend·Frontend 전체 |

보완 검증:

- 실제 SQL에 `tile`·`object`·`backdrop` 계획·캔버스·승인을 저장하고 새 DbContext로 재조회한다. 잘못된 enum·영역 등 개별 값과 잘못된 작업별 역할은 각각 거부한다.
- 동일 요청 재전송, 격자만 다른 requestId 충돌, 격자만 수정한 타일 작업의 revision 증가·이미지/승인 유지·ZIP 이전 표시, 역할·캔버스 변경 후 앵커 일치를 검증한다.
- 구형 승인 작업의 새 v2 내보내기, 접수된 v1의 고정 입력 재시도, 완성된 v2 manifest의 SQL 저장·재조회를 분리해 검증한다.
- 실제 서버 DTO 매핑·서버 JSON 설정으로 생성한 응답을 Frontend의 실제 reader로 읽는다. 새 혼합 작업·구형 작업·구형 승인 snapshot·`grid: null`을 포함하고, 추가로 과거 필드 생략 응답도 읽는다. fixture를 쓰면 서버 생성 방법·기준 revision을 기록한다. 손으로 작성한 Fake 응답은 이 연결 검사를 대신하지 않는다.

실제 AI 확인은 구현 후 사용자 승인 시 1회 별도로 한다. `300f237d`와 같은 원본으로 새 작업을 만들어 하늘선이 `backdrop`, 지붕이 `object`로 분류되는지 보고 비용·결과를 기록한다. 프롬프트 출력 비교는 `agentic-eval` 절차를 따른다.

## 문서

- `DESIGN.md`·`PRODUCT.md`의 2D 배경 결과 설명, `docs/xHuman/frontend.md`·`backend.md`·`providers-and-prompts.md`의 해당 지도를 같은 커밋에서 갱신한다.
- 기반 설계의 "한 작업 한 결과 유형" 문장 옆에 이 설계로 바뀐 범위를 링크한다.
