# 미니 스펙: Decompose depthOrder 동률 처리 규칙 추가

## 배경

`733c2792-...` job의 `decompose` 공정이 3번 다 `PART_DEPTH_DUPLICATE`로 실패했다(실비용
$0.0183). 응답을 까보니 25개 파츠 중 벨트 버클·검 2자루·목걸이·귀걸이처럼 "몸에서 가장
튀어나온 작은 부착물"끼리 `depthOrder=1`로 다섯 개가 뭉쳐 나왔다 — 우연이 아니라 3번 다
같은 패턴이라 구조적 약점으로 판단. 현재 규칙(`SeedPrompts.cs:697`,
`DecomposeCharacterSystem`)은 "1이 가장 가깝고 파츠마다 서로 다른 값" 이라고만 하고,
동률일 때 어떻게 가르라는 지시가 없다.

## 목표 (한 문장)

`DecomposeCharacterSystem` 프롬프트에 depthOrder 동률 tie-break 규칙을 추가해,
시각적으로 우열을 가리기 힘든 파츠들이 같은 값으로 뭉쳐 `PART_DEPTH_DUPLICATE` 재시도
비용이 새는 것을 줄인다.

## 입력 → 출력

**입력**: 현재 규칙 한 줄 —
```
- depthOrder: 1 is nearest to the viewer. Every part gets a distinct value.
```

**출력**: tie-break 지시가 추가된 새 문면. 예시 방향(최종 문구는 구현 시 확정):
```
- depthOrder: 1 is nearest to the viewer. Assign a strict ranking across every part —
  never the same value twice, even when several parts sit at genuinely similar visual
  distance (e.g. a buckle, two swords, a necklace and an earring worn at the front of
  the body). When true depth is ambiguous, break the tie by partRef number, lower
  partRef ranked nearer (P03 before P08).
```
그리고 이걸 새 프롬프트 버전(Decompose v9)으로 마이그레이션에 재시딩한다(workstream L,
`CharacterPromptWorkstreamK` 뒤를 잇는 파일).

**예시**: 위 job과 같은 소스 이미지로 다시 돌리면, 버클/검/목걸이/귀걸이가 `depthOrder`
1·2·3·4·5처럼 partRef 순으로 갈라져 25개 전부 distinct — 검증 통과.

## 이번에 안 하는 것 (제외 범위)

- `DecomposeV3System`(배경·소품 공용, 캐릭터 아닌 경로)은 이번에 안 건드린다 — 실제로
  관측된 실패는 캐릭터 파이프라인뿐이다. 다만 같은 한 줄 규칙을 그대로 공유하고 있어
  이론상 배경·소품(나무 여러 그루, 가로등 여러 개 등)에서도 같은 문제가 날 수 있다는
  점은 함정 항목에 남겨둔다 — 확산 여부는 재발 시 별도 결정.
- Extract·Generate 프롬프트는 안 건드린다 — depthOrder는 Decompose 산출물에만 있는 필드.
- `occludedBy`나 그 외 Decompose 규칙 재작성은 안 한다 — depthOrder 한 줄만 바꾼다.
- 재시도 횟수 상한(`MaxAttempts`)이나 비용 통제 로직 변경은 안 한다 — 이번 접근은
  "모델이 동률을 못 가르는" 근본 원인 자체를 없애는 것이지, 그 위의 재시도 정책을
  건드리는 게 아니다.

## 예상 함정

1. **문면 바이트 동일성 테스트가 반드시 깨진다.** `PromptCompositionTests`가 캐릭터
   문면을 해시로 고정해두고 있어(workstream K에서 확인한 관례), 이 프롬프트를 바꾸면
   그 해시 테스트가 깨진다 — 의도된 변경이니 해시 값도 같이 갱신해야 하고, 놓치면
   "실패를 놓치고 아무 값으로나 덮어써서 통과시키는" 실수가 나기 쉽다.
2. **tie-break가 실제로 지켜지는지는 실 API로만 확인 가능.** 프롬프트 문구를 고쳤다고
   모델이 그대로 따른다는 보장이 없다 — Fake 모드는 진짜 모델을 안 부르므로 검증 불가.
   최소 1회는 실 API 호출로 검증해야 하고, 이것도 실비용(대략 $0.006 수준)이 든다.
3. **`DecomposeV3System`(비캐릭터)도 같은 구조적 약점을 공유한다.** 이번엔 캐릭터만
   고치지만, 배경·소품에서 파츠 수가 늘면 언젠가 같은 `PART_DEPTH_DUPLICATE`가 날 수
   있다 — 지금은 범위 밖으로 뒀다는 걸 기록해둔다.
4. **partRef 순서가 "미학적으로 맞는" 깊이 순서는 아닐 수 있다.** Extract가 파츠를
   나열하는 순서(P01..P25)는 시각적 깊이와 무관하게 정해지므로, tie-break로 억지로
   순서를 만들어도 그 순서 자체가 완벽한 깊이 표현은 아닐 수 있다. 다만 이번 목표는
   "완벽한 깊이 정렬"이 아니라 "검증 통과 + 재시도 비용 방지"라 이 정도 트레이드오프는
   허용 범위로 본다.

## 검증 방법

1. **단위**: `SeedPromptsTests`·`PromptCompositionTests`에 새 문면 해시 갱신 후
   `dotnet test` 타겟 테스트 통과.
2. **마이그레이션**: `CharacterPromptSeedMigrationTests`에 새 버전(Decompose v9) 시딩
   확인 케이스 추가, 이전 버전 비활성화(`IsActive=0`) 확인.
3. **실 API 1회**: 이번에 실패했던 것과 같은 소스 이미지로 Decompose만 다시 호출 —
   25개 파츠의 `depthOrder`가 전부 distinct한지 확인. (실비용 발생 — 진행 전 사용자
   확인)
4. **회귀**: `dotnet test apps/backend/Noxtend.slnx` 전체.

## 승인

- [x] 사용자 승인 (승인 전 구현 금지)
