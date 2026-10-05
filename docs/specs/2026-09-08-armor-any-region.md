# 미니 스펙: 사람이 그린 파츠는 그린 대로 — 부위 전제 없는 서술·작화 + 따로 고정된 강체 자동 분리 (armor-any-region) — v2

> 근거: 2026-09-08 골든셋(`03900D2A`) DB 실측(`docs/specs/character-tuning-progress.md`
> §"남은 것" 조사 — 2026-09-08 main 병합으로 `01-plan/features/character-tuning.plan.md` 에서 이동).
> 사용자 결정(2026-09-08): **자동은 초안, 검수에서 사람이 그린 상자가 정본.** ①+② 채택,
> ③(스카프 자동 분리) 제외, B(택소노미에 스카프 추가) 보류.
>
> **v2 = v1 − 골든셋 맞춤 열거 + Generate 점유 영역 제외 일반화.** v1 은 갑주 규칙을
> "상의·하의·신발·장갑 위"로 타입을 열거하고 관절을 골든셋 부위로 열거했다 — 현재 캐릭터에만
> 맞는 규칙이라는 지적(사용자)을 반영해 원리 하나로 묶었다.

## 목표 (한 문장)

모델러가 검수 화면에서 상자를 그려 파츠를 명시하면 **그 이름·위치·힌트가 가리키는 부위에
맞게 서술·생성되고, 겹치는 기존 파츠의 이미지에서는 그 영역이 빠지며**(②), 스트랩·버클로
따로 고정된 강체는 자동 추출이 초안에서부터 별도 파츠로 뽑아 상자 그릴 일을 줄인다(①).

## 원칙

- 자동 추출은 **초안**이다. 캐릭터마다 다른 분리 의도는 사람이 검수에서 정한다(대칭 파츠
  결론·수동 좌표 모드 미채택과 같은 줄).
- 자동은 **명백한 경우만** — 과분리는 검수에서 삭제 한 번, 미분리는 상자·이름·재작성을 기다리는
  과정이라 과분리가 싸다. 애매한 것(천 매듭·붕대·표면 장식)은 자동에 넣지 않는다.
- 규칙은 **부위·타입을 열거하지 않는다.** "힌트된 파츠 위에 따로 고정된 것", "리그가 굽히는
  관절"처럼 원리로 쓰고, 부위는 예시로만 든다.

## 배경 — 실측이 밝힌 것

| 건 | 실측 | v2 대응 |
|---|---|---|
| #8 오른쪽 발목 가드 | "오른쪽 신발 발목 아래"에 흡수. 서술 "발목 갑주와 단단히 연결". 자동 분리 규칙이 상의·하의 위 갑주만 명시 | ① 원리로 확장 |
| #11 관절 갑주(반영 완료) | 캡 규칙 예시가 어깨·팔꿈치·무릎뿐 — 발목·허벅지 갑주가 걸리면 형태 왜곡 가능 | ②-2 |
| 검수 수동 추가 | `RewriteDescriptions` 는 NEW 파츠에 이름·좌표·카테고리만 줌. 힌트 없음, 갑주 형태 규칙 없음 | ②-1 |
| 하의에 허벅지 스트랩 중복 | Generate 에 "다른 파츠를 그리지 마라"는 있으나 원본 이미지가 첨부돼 겹친 장비를 따라 그림. 벨트·머리카락만 "원본에 보여도 그리지 마라" 반례가 있어 지켜짐 | ②-3 |
| #2 스카프 · #5 자락 | 택소노미에 타입 없음 → 스코프 규칙이 배제 | **③ 제외** — 사람이 상자로 명시, ② 가 받는다 |
| #1 벨트 | 분리됨. 2단 벨트 하단이 "벨트 장식"으로 분류 | 범위 밖(이름 문제) |

## 입력 → 출력

### ② 사람이 그린 파츠가 그린 대로 나온다 (본체)

**②-1 Rewrite (`RewriteDescriptions` DB v4 — note 의 "v4" 는 본문 개정 이력이고 DB `Version` 은 마이그레이션 적용 횟수라 이번이 4번째다. 구현 중 SQL 테스트로 확인해 v5 표기를 정정)**
- `RewriteDescriptionsStage.BuildVariables` 에 `["partHints"] = PartHintCodec.Render(job.PartHints)`
  추가. User 프롬프트에 `The user's part hints: {{partHints}}` 한 줄.
- System 의 NEW 항목에 추가: "Infer the body region from the part's name and its region
  coordinates, cross-checked with the user's part hints. Never assume a location the name
  and region do not support. For rigid armor, apply the same rule as the rest of the pipeline:
  armor over a joint wraps it into a cap; armor over a non-joint region keeps the plate form
  the source shows."

입력 예: `- 오른쪽 발목 갑주 (NEW, needs a description; category: unknown) region → x=0.19
y=0.86 w=0.14 h=0.07` + 힌트 `신발(무릎형) 1, …` → 발목을 앞·옆·뒤로 감싸는 가죽·금속 가드,
`category: "Armor"`, "어깨" 언급 없음.
입력 예: `- 오른팔 스카프 (NEW …) region → x=0.27 y=0.22 w=0.08 h=0.06` → 상완에 감아 매듭
지은 주황색 천, 늘어진 끝단. `category: "Scarf"`.

**②-1 보강 — 좌우 기준과 이름·좌표 충돌** (2026-09-09 실 API 실측, 사용자 승인)

- 실측: 검수에서 그린 `왼쪽 어깨 스카프`(`x=0.249`, 화면 왼쪽 = 착용자 오른팔, 원본은 **주황색**
  천 매듭)의 서술이 "짙은 **청색** 천 스카프"로 나왔다. 모델이 이름의 "왼쪽"을 착용자 기준으로
  읽어 화면 오른쪽(청색 재킷·갑주)을 보고 썼다.
- 원인 둘: ① 좌우가 착용자 기준인지 화면 기준인지 **프롬프트 어디에도 정의가 없다**(전수 grep
  확인). 자동 서술은 모델이 관례로 착용자 기준을 쓴다("착용자 왼쪽 어깨, 화면 오른쪽에 놓이는").
  ② ②-1 이 "이름과 좌표로 판단하라"고만 하고 **둘이 어긋날 때 무엇이 이기는지**를 안 박았다 —
  "사람이 그린 상자가 정본"이라는 이 브랜치 원칙과 어긋나는 갭이다.
- 기대 (`RewriteDescriptions` 재시드): System 의 NEW 항목에 두 문장 —
  "Left and right in a part's name are the **wearer's** left and right, not the viewer's."
  그리고 "**The region coordinates win.** When the name's side does not match what the region
  shows — a name saying 왼쪽 over a region on the viewer's left, which is the wearer's right —
  describe what is actually inside the region and ignore the side implied by the name. A human
  drew that box on the image; it is the source of truth for what this part is."
- 화면 안내(검수 추가 폼에 "좌우는 착용자 기준")는 이번 범위 밖 — 별도 항목.

**②-2 Decompose (`CharacterDecompose` v12) · Generate (`CharacterGenerate` v9) 캡 규칙**
- 현재: "Rigid armor that caps a joint (a shoulder guard or pauldron, an elbow or knee guard) is
  described/drawn as wrapping that joint … closing into a cap."
- 기대: 조건을 "**any joint the rig will bend** (shoulder, elbow, wrist, hip, knee, ankle, neck)"
  로 쓰고 예시는 괄호 안으로. 뒤에 추가: "Armor over a non-joint region (a thigh plate, a
  shin plate, a chest plate, a forearm plate) keeps the plate form the source shows — do not
  force it into a cap. Judge which case applies from the part's name and its placement, never
  from an assumed body location."
- 두 곳 동시 변경 — 한 곳만 바꾸면 서술과 작화가 어긋난다.

**②-3 Generate — 점유 영역 제외 일반화 (`CharacterGenerate` v9, ②-2 와 같은 재시드)**
- 현재: "Do not reproduce other parts …" 일반 문장 + 벨트·머리카락만 "even when the
  reference image shows…" 반례.
- 기대: 벨트 반례의 문법을 일반화한 문단 추가 — "Anything the reference image shows layered
  on top of this part that is NOT in this part's own description — an armor plate, a strap,
  a buckle, a pouch, a knotted cloth, a wrap — belongs to another part. Do not draw it, even
  though the reference shows it there; continue this part's own surface naturally underneath,
  the same way a bottom is drawn with no belt and a head with no hair." 벨트·머리카락 문장은
  예시로 유지.
- 판단 기준은 **이 파츠의 서술**이다(Generate 는 가림 정보를 받지 않는다 — Stages.cs 주석).
  서술은 Rewrite 가 겹친 영역을 뺀 형태로 이미 정리하므로, 서술이 정본이면 작화도 따라온다.

### ① 자동 추출 — 따로 고정된 강체 (`CharacterExtract` v13)

- 현재(라인 890~902 부근): "Rigid armor plating worn over a hinted top or bottom (상의, 하의)
  is its own part too…"
- 기대: "A rigid piece fastened over **any hinted part** as a separate object — held on by its
  own straps, buckles, laces or rivets — is its own part too, split out the same way: a chest
  plate or shoulder guard over a top, a thigh plate over trousers, an ankle guard strapped over
  a boot, a wrist guard over a glove, a bracer over bare forearm skin. A covering whose own body
  is plated (a boot with an armored shaft, a gauntlet that is itself the glove) is NOT a separate
  piece — it stays one segment per joint under the split rule below." 이름은 기존 규칙대로
  위치 기반 한국어("오른쪽 발목 갑주").
- 힌트 스코프 문장(985 "armor above the ankle stays in scope even when only footwear was
  hinted")은 이미 있으므로 참조만 하고 중복 규칙을 얹지 않는다.

입력 예: 골든셋 원본 + 힌트 `신발(무릎형) 1, 하의 1, …` → `…, "오른쪽 신발 발목 아래",
"오른쪽 발목 갑주", …`. 왼다리 붕대·팔의 스카프는 **뽑지 않는다**(강체가 아님 — ③ 제외).

**①-보강 — 관절 분할 규칙과의 경계** (2026-09-08 독립 리뷰 발견 4, 사용자 승인 ⓐ)
- 문제: 기존 관절 분할 규칙("Any rigid armor, footwear, or gloves that visually span a major
  joint … are always split at that joint line")을 문자대로 읽으면 발목 관절 위에 얹힌 발목
  가드도 "발목 위/아래" 로 잘릴 수 있다 — ① 의 기대 출력(`"오른쪽 발목 갑주"` 한 파츠)과
  충돌한다. 어깨 갑주는 골든셋에서 한 덩어리로 나왔으므로 모델이 이미 그렇게 읽고 있을
  가능성이 크지만 문면엔 없다.
- 기대: 관절 분할 규칙 문단 끝에 한 구절 — "A guard that only caps a single joint (a
  pauldron, a knee guard, an ankle guard) is one part named by that joint; the split applies
  to coverings that run along the limb from one side of the joint to the other."
- 뜻: 관절 하나에 씌운 캡(어깨 갑주·무릎 보호대·발목 가드)은 자르지 않고 한 파츠, 이름은
  그 관절로. 자르는 규칙은 팔·다리를 따라 관절 양쪽으로 길게 이어지는 것(부츠·긴 장갑)에만.

### ①-보강 2 — 힌트 스코프가 이기게 (2026-09-09 실측, 사용자 승인)

**실측**: 힌트가 `상의 1, 하의 1, 신발(무릎형) 1, 무기 1, 장갑(팔꿈치형) 1` 뿐인데 Extract 가
`베이스바디`·`머리`·`머리카락` 을 냈다. 셋 다 힌트에 없는 타입이다. 사용자가 파츠를 줄여
비용을 아끼려 한 의도가 무력화된다(검수에서 지우면 생성은 안 되지만, 매번 지워야 한다).

**원인**: 두 문장이 스코프 규칙(994)보다 **앞에서**(974·987) 더 강한 말투로 강제한다 —
"the base body **is always** ONE single part", "the head and the hair are **ALWAYS** two separate
parts … **Return them as distinct entries**". 원래는 "뽑을 때 어떻게 나눌지"(합치지·쪼개지 마라)
규칙인데 문면이 "항상 돌려줘라"로 읽힌다.

**기대 (Extract v14)**: 두 문장을 **조건절**로 바꾼다. 나누는 규칙은 그대로 두고 강제 반환만 뗀다.
- `EXCEPTION — the base body is always ONE single part` →
  `EXCEPTION — when the base body is in scope, it is ONE single part`
- `The head … and the hair are ALWAYS two separate parts … Return them as distinct entries even
  when the hair covers much of the head.` →
  `When the head or the hair is in scope, they are two separate parts — never merge the hair into
  the head, the body, or a hat. This holds even when the hair covers much of the head.`

**문장 순서는 옮기지 않는다.** 조건절이 본질적 해결이고 순서는 경험칙이다 — 둘을 한 번에 바꾸면
무엇이 통했는지 알 수 없다. 조건절로 안 되면 그때 스코프 규칙 뒤로 옮긴다.

### ①-보강 3 — 벨트 자동 분리를 하의·원피스로 좁힌다 (같은 실측, 사용자 승인)

**실측**: 힌트에 벨트가 없고 하의가 있었는데 벨트 5종(벨트·버클·파우치2·메달)이 나왔다.
현재 문면이 `A belt worn over a hinted top, bottom or dress (상의, 하의, 원피스)` 라 **상의만
요청해도** 벨트가 딸려온다.

**기대 (Extract v14, 같은 재시드)**: `over a hinted bottom or dress (하의, 원피스)` — 상의 제외.

**갑주는 그대로 둔다.** 같은 방식으로 조건화하려면 택소노미에 "갑옷" 이 있어야 하는데, 2026-08-18
에 "상의·하의와 부위가 겹쳐 같은 자리가 두 파츠로 중복 렌더된다" 는 실사용 관측으로 뺀 것이다
(사용자 확인, 2026-09-09). 벨트를 **명시 요청**하는 경로(택소노미에 벨트 복원)도 workstream K
결정을 뒤집는 것이라 별도.

### 재시드 마이그레이션 1개

`20260908104531_CharacterArmorAnyRegion` — Extract v13 · Decompose v12 · Generate v9 ·
RewriteDescriptions **v4**(위 정정). 템플릿 `20260904103000_CharacterHandPoseAndJointArmorWrap.cs`
(Up 은 Reseed, Down 은 직전 활성 복구). Rewrite 는 `Category IS NULL` 기본 슬롯이라
캐릭터 3개와 스코프를 나눠 재시드한다(필터 유니크 함정).

### 문서

`docs/specs/character-tuning-progress.md` §"남은 것 — #1·#2·#5·#8" 을 실측 결과·판정·결정으로 갱신.

## 이번에 안 하는 것 (제외 범위)

- ③ 스카프·천 매듭·띠·붕대의 자동 분리 — 볼륨 유무 경계가 모델러 판단 영역. 사람이 상자로 명시.
  **2026-09-09 종결**: 모델러가 "발의 스카프는 붙여서 만드는 게 낫다"고 했다 — 자동 분리를
  하지 않는 현행이 그 의견과 같다. 어깨 스카프처럼 필요한 것만 검수에서 사람이 추가한다.
- B 택소노미에 스카프 타입 추가 — **2026-09-09 하지 않기로 종결**(위 모델러 의견).
- #1 "벨트 장식" 분류 이름.
- 검수 추가 폼의 안내 문구(사람이 서술을 직접 쓸 때 부위를 적으라는 안내) — 별도.
- 실 API 재검증 — 별도 결정(§검증 2). `feature/character` 반영 — 그 뒤.

## 예상 함정

1. **문자열만 고치면 DB 는 옛 문구** (plan §함정 1) → 재시드 필수. Designer + `[Migration]`
   (§함정 2) → `MigrationRegistrationTests`.
2. **note 500자** (§함정 4) → `NoteFitsTheColumn`, 4개 프롬프트 모두.
3. **①의 경계** — "따로 고정된 것" vs "몸체가 갑주인 것"이 모호하면 갑주 부츠가 둘로 쪼개진다.
   양쪽 예를 같은 문단에 둔다(위 문면). 그래도 틀리면 검수에서 삭제 — 그래서 과분리 쪽으로 기운다.
4. **②-2 가 #11(어깨 캡)을 약화시키면 안 된다** — 관절 조건은 유지, 비관절 문장은 덧붙이기만.
5. **②-3 가 이 파츠 고유의 표면 장식(리벳·스티칭·자수)까지 지우면 안 된다** — 기준을 "이 파츠
   서술에 없는 것"으로 두고, 표면 장식은 Decompose 가 서술에 담는다(960~962 규칙). 서술이
   정본이라는 전제가 깨지면(사람이 서술을 너무 짧게 쓰면) 장식이 빠질 수 있다 — 검수에서 확인.
6. **workstream O 교훈** — 규칙을 얹으면 자유변수가 왜곡될 수 있다. ①은 이미 작동 중인 규칙
   (어깨·허벅지 갑주는 골든셋에서 실제로 분리됐다)의 범위 확장이고, ②-3 는 이미 지켜지는 벨트
   반례의 일반화라 위험이 낮다. 그래도 **실 API 검증 전에는 "시드됨"이지 "해결됨"이 아니다.**
7. **Rewrite 변수 추가** → `SeedPromptVariableTests`. User 에 `{{partHints}}` 를 안 넣으면 값이
   조용히 버려진다(v2 사고 재발).
8. **스키마 무변경** — Rewrite·Extract·Decompose 응답 스키마 그대로. strict 400(§함정 3) 회피.
9. `PromptCompositionTests` 해시 갱신 — 의도적 문면 변경이므로 정상 절차.
10. 버전 기대치 — `CharacterPromptSeedMigrationTests` Extract 12→13, Decompose 11→12,
    Generate 8→9, 운영자 시나리오 13→14.
11. **사람이 서술을 직접 쓴 파츠는 Rewrite 대상이 아니다** (`AddReviewPart`: description 이
    있으면 stale 로 두지 않음) — ②-1 은 서술을 비운 경우에만 작동한다. 직접 쓴 서술이 부위를
    안 담으면 ②-2·②-3 만 남는다. 폼 안내는 범위 밖(위 제외).
12. **Generate 는 가림 정보를 받지 않는다** — ②-3 는 서술만 근거다. Rewrite 가 겹친 영역을 뺀
    서술을 만들지 못하면(재작성 실패 후 옛 서술로 진행 등) 중복이 남는다. 재작성 실패는 이미
    재시도 대상(`RewriteDescriptions` HttpRequestException 재시도 실측).
13. **① 과 관절 분할 규칙의 충돌** (리뷰 발견 4) — 캡 종류 가드가 관절에서 잘리는 것. ①-보강
    문구로 경계를 긋는다. 실 API 에서 "발목 갑주가 둘로 안 잘리는지" 를 따로 본다.
14. **좌표를 우선하라고 해도 모델이 이름을 따를 수 있다** (②-1 보강) — 이번 실측이 그 사례다.
    문구를 넣는 것으로 보장되지 않으므로, 실 API 에서 같은 상자를 다시 그려 색·부위가 맞는지
    본다. 안 되면 이름 자체를 화면 기준으로 강제하는 화면 안내로 옮긴다(프롬프트로 이기려
    하지 않는다 — workstream O 교훈).

## 검증 방법

**1. 무료 · 테스트 코드 (로컬, 외부 호출 없음)**
- `CharacterPromptSeedMigrationTests` — 활성 하나씩, 기대치 13/12/9.
- `MigrationRegistrationTests`, `NoteFitsTheColumn`, `SchemaListsEveryPropertyAsRequired`,
  `SeedPromptVariableTests`, `PromptCompositionTests`(해시 갱신) 통과.
- 문면 계약 테스트 추가: Extract 에 "fastened over any hinted part" + "armored shaft" 반례 존재 /
  Decompose·Generate 캡 규칙에 "any joint" 와 "non-joint" 존재 / Generate 에 "NOT in this part's
  own description" 존재 / Rewrite System 에 "part hints" 와 "joint", User 에 `{{partHints}}`.
- 확인 범위: **문면이 DB 에 시드되고 변수가 흐르는지까지.** 모델이 규칙을 지키는지는 못 본다.

**2. 유료 · 실 API (사용자 결정 후)** — 골든셋 원본 1회, 검수에서 상자 2개 추가, 약 ₩20,000.
- ① 자동: Extract 응답에 발목 갑주가 별도 항목 **하나**(발목 위/아래로 잘리지 않음 — 함정 13).
  붕대·스카프는 없음(③ 제외 확인).
- ② 검수에서 "오른팔 스카프"·(자동이 못 뽑았다면) "오른쪽 발목 갑주" 상자 추가 → 서술이
  부위·색을 맞게 씀 / 부츠 이미지에 가드 없음 / 하의 이미지에 허벅지 스트랩 없음 / 팔 세그먼트
  이미지에 스카프 없음 / 어깨 갑주는 여전히 캡 / 허벅지 갑주는 판.
- 왜 코드로 안 되나: 문면·변수까지는 테스트가 보지만, 모델이 원본을 보고도 겹친 장비를 안
  그리는지·부위를 이름과 좌표로 맞게 짚는지는 실제 모델을 불러야 안다(`FakeLlmProvider` 는
  문면을 읽지 않는다 — §함정 5).

## 승인
- [x] 사용자 승인 (2026-09-08, ①+② 채택 · 구현은 하위 모델 위임)
