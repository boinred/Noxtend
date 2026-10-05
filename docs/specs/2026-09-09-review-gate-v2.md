# 미니 스펙: 검수 게이트 v2 — 볼 수 없으면 베낄 수 없다 (review-gate-v2)

> 근거: 2026-09-09 실 API 실측 `F96BE0F7`. 서술 오류(스카프 청색, 자락 오인)와 생성 오류(부츠
> 갑주에 부츠 본체, 바지에 허벅지 스트랩, 측면 크기 불일치)가 이미지 92장($12) 뒤에야 드러났다.
> 사용자 결정: 서술을 사람이 쓰는 건 답이 아니다. D-1·D-2 채택.

## 목표 (한 문장)

모델이 **베낄 수 없게** 참조 이미지를 손보고(서술 = 크롭, 생성 = 마스킹), 서술을 **생성 전에**
사람이 확정하며, 측면 크기를 코드로 맞춘다.

## 원칙

- 사람이 그린 상자가 정본, 사람이 확정한 서술이 정본.
- 프롬프트로 부탁하지 않는다. 픽셀을 지운다.
- 사용자는 읽고 틀린 것만 고친다.

## 결함 ↔ 대응

| 결함 (F96BE0F7) | 층 | 대응 |
|---|---|---|
| 스카프 청색 — 이름의 좌우를 따라 반대쪽을 봄 | 서술 | ① 크롭 |
| 자락이 엉뚱한 파츠 — 상자 45%×34% | 서술 | ① + ③ 에서 발견 → 상자로 돌아가기 |
| 부츠 갑주 아래가 부츠까지 그림 | 생성 | ② 마스킹(상자 밖) |
| 바지에 허벅지 스트랩 | 생성 | ② 마스킹(가리는 파츠 상자 안) |
| 허벅지 갑주 측면 크기 | 생성 | ④ 정규화 |
| 틀린 서술로 $12 지출 | 흐름 | ③ 서술 확정 |

## ① 크롭 — 재작성 입력

- NEW 파츠(서술이 빈 stale 파츠)마다 상자를 잘라 `crop:<이름>` 으로 **추가**. `original` 은 유지.
- 크롭: 정규화 좌표→픽셀, 사방 여백 12%(상자 폭·높이 기준), 이미지 밖 클램프, EXIF 방향 선적용, PNG.
- 코드: `IImageTranscoder.CropAsync(Stream, Bounds, double margin, ct) → byte[]` + Skia 구현.
  `IStage.ExtraImageRegions(job) → (Name, Bounds)[]`(기본 빈 목록). `RewriteDescriptionsStage` 가
  NEW 파츠의 `Placements[0]` 반환. 크롭·첨부는 `RunTaskHandler`(IO 는 핸들러).
- 프롬프트(Rewrite, DB v5): NEW 항목에 — "For each NEW part you get `crop:<name>`, its box with a
  small margin. **Describe what is inside the crop.** Use `original` only for body region and
  surroundings. If crop and name disagree, the crop wins." 기존 "The region coordinates win…"
  문장은 이걸로 대체. User 의 `region → x=…` 줄 유지.

## ② 마스킹 — 생성 참조

- 파츠 P 를 그릴 때 첨부하는 원본에서 흰색(#FFFFFF)으로 지운다:
  - P 의 배치 상자(여러 개면 합집합) **밖** 전부
  - P 의 `occludedBy` 파츠들의 상자 **안**
- 원본이 첨부되는 모든 생성 호출에 적용(정면·비정면 동일). 정면 완성본(reference 1)은 손대지 않음.
- 코드: `IImageTranscoder.MaskAsync(Stream, Bounds keep, IReadOnlyList<Bounds> erase, ct) → byte[]`
  + Skia. 적용은 `RunGenerationTaskHandler.LoadOriginalAsync` 직후. `AssetPart.OccludedBy` →
  상자 조회는 `job.Parts` 에서.
- 프롬프트(Generate, DB v10): 반례 문단 뒤에 — "Flat white areas in the reference are where other
  parts were removed. Draw nothing there; continue this part's own surface underneath."
- 자동 파츠에도 적용(바지·부츠 등). 베이스바디처럼 상자가 화면 대부분이면 밖 마스킹은 사실상 없음.

## ③ 서술 확정 — 2단계 승인

흐름: `상자 확정 → 재작성 → PendingReview(서술 단계) → 편집 → 생성 시작 → 팬아웃`

- 도메인: `PipelineJob.DescriptionsConfirmed`(bool). `PlanGenerationFanOut` 가드 추가 —
  `RequiresReview && !DescriptionsConfirmed` 이면(재작성이 있으면 성공 뒤) `Status = PendingReview`,
  팬아웃 안 함. 위치: `ReviewApproved` 가드 아래, 재작성 성공 가드 뒤.
- `ConfirmDescriptions(now)`: `PendingReview && ReviewApproved` 만. 플래그 true, Running,
  `PlanReadyFollowUpTasks()`.
- `EditReviewDescription(name, text)`: 서술 단계만. 빈 문자열 400. `DescriptionsStale` 에서 제거.
- `ReturnToBoxes()`(D-1 채택): `ReviewApproved = false`. 재작성된 서술은 유지. 재승인 시 stale 만 재작성.
- 서술 단계에서 상자 추가·이동·삭제 → 400 `ReviewPhaseMismatch`.
- 서술 단계는 검수 게이트 작업 **전부**에서 거친다(D-2 채택) — 분해 서술의 오류도 잡는다.
- API: `POST review/confirm-descriptions`, `PUT review/parts/{partId}/description`,
  `POST review/return-to-boxes`. `GET review` 에 `reviewPhase: boxes|descriptions`
  (`ReviewApproved && !DescriptionsConfirmed`). 서술은 이미 응답에 있음(`ReviewResponse.cs:35`).
- 화면(`ReviewGate.tsx`): 서술 단계 = 상자 도구 숨김, 파츠별 서술 편집칸, stale 였던 파츠 상단·칩 구분.
  버튼 `상자 확정`(1차) / `생성 시작` · `상자로 돌아가기`(2차).

## ④ 측면 정규화 — 후처리

- 비정면 뷰 저장 직전(`AttachGeneratedImage` 경로) 정면과 높이를 맞춘다: 비흰색 픽셀 바운딩 박스
  검출(임계 ≥ 250 은 배경) → 정면 박스 높이에 맞게 등비 스케일 → 같은 캔버스 크기, 중앙 정렬, 흰 채움.
- 코드: `IImageTranscoder.MatchHeightAsync(Stream view, Stream front, ct) → byte[]` + Skia.
  저장은 한 번이므로 "생성물 불변" 유지. 정면이 없으면(있을 수 없음) 건너뛰고 로그.
- 독립 슬라이스 — 뺄 수 있다.

## 마이그레이션

- `Jobs.DescriptionsConfirmed` bit NOT NULL DEFAULT 0. **스키마 변경** — Designer 는 최신
  스냅샷 + 새 컬럼, `NoxtendDbContextModelSnapshot` 갱신 필수.
- 재시드: Rewrite(Category NULL, v5) · Generate(Character, v10). 같은 마이그레이션.
- 기존 데이터: Generate 계획된 작업은 "이미 계획됐다" 에서 빠짐. 승인 전 작업은 새 흐름.

## 안 하는 것

- 생성 단계에 크롭 첨부(통째 완성 규칙과 충돌). 상자 테두리 오버레이. 정면 1장 관문(v3).
- 파츠 재분해(자식 작업), 파츠별 3D 선택·면 수, 폼 안내 문구, 재작성의 카테고리 덮어쓰기 결함 — 별도.

## 예상 함정

1. EXIF 방향 — 크롭·마스킹 모두 방향 선적용. 회전 이미지 단위 테스트.
2. 상자 클램프 — 0 폭·높이면 크롭 건너뛰고 로그. 마스킹은 keep 이 비면 원본 그대로.
3. 크롭이 있어도 이름을 따를 수 있다 — 실 API 확인. ③이 비용을 막는다.
4. 흰 구멍을 흰 조각으로 그릴 수 있다 — 실 API 확인. 프롬프트 한 문장이 유일한 방어.
5. 직사각형 구멍이 P 자신의 영역까지 지운다(가리는 파츠 상자가 크면) — 실측 후 여백 축소나 교집합만 지우기로 조정.
6. 공급자 이미지 수 상한 — NEW 파츠가 많으면 크롭 N개. 어댑터 상한 확인, 초과분은 원본만.
7. 이미지 이름이 공급자로 전달되는지 — 안 되면 순서(원본 첫 장)로 구분.
8. 재작성 실패 — 기존 재시도 경로. 실패한 채 서술 단계 진입 없음.
9. `PendingReview` 복귀 — 비종료라 폴링 유지. 화면은 `reviewPhase` 로만 가른다.
10. 동시성 — 편집·확정·되돌리기는 `Jobs.RowVersion`.
11. 서술 단계 상자 편집 API — 도메인 400.
12. 사람이 고친 서술을 stale 에서 빼지 않으면 되돌리기 후 재승인이 덮어쓴다.
13. Designer 오래된 것 복사 금지 — 이번엔 스키마가 바뀐다. `MigrationRegistrationTests` + 스냅샷 대조.
14. E2E 픽스처 1회 승인 가정 — 2단계로.
15. ④ 바운딩 박스 오검출(그림자·안티앨리어싱) — 임계값 테스트. 실패 시 원본 저장(정규화는 best-effort).
16. Note 500자 — Rewrite 389, Generate 316.

## 검증

**무료 · 테스트 코드** (SQL 픽스처는 메모리 사정상 확인 후)
- 크롭·마스킹·정규화 단위(합성 이미지): 좌표 변환, 여백, 클램프, EXIF, 구멍 위치, 높이 일치.
- `ExtraImageRegions` NEW 만. `RunTaskHandler` 요청에 크롭 N개(Fake 캡처). `RunGenerationTaskHandler`
  가 마스킹본을 첨부(픽셀 검사).
- 상태기계: 승인→재작성→서술 단계→확정→팬아웃 / 상자 편집 400 / 되돌리기→재승인 / 편집이 stale 제거.
- 프롬프트 계약: Rewrite "inside the crop"·"the crop wins", Generate "Flat white areas".
- 마이그레이션 등록·스냅샷. E2E 2단계.
- 범위: 이미지가 만들어져 요청에 실리고 상태가 전이되는 것까지.

**유료 · 실 API** (결정 후, 이 캐릭터 새 작업 1회, 서술 안 씀)
- 서술 단계: 스카프 주황 / 자락 = 벨트 아래 천 / 발목 갑주 = 부츠 없음. 틀리면 멈춤(비용 0).
- 생성 후: 부츠 갑주에 부츠 없음 / 바지에 스트랩 없음 / 측면 높이 = 정면.
- 코드로 안 되는 이유: 모델이 크롭·흰 구멍을 어떻게 읽는지는 실제 모델만 안다.

## v3 예고

정면 1장 → 사람 확인 → 3면·3D. 생성 층 오류의 비용을 75%+ 막는다. 이 스펙 뒤.

## 승인
- [ ] 사용자 승인 (승인 전 구현 금지)
