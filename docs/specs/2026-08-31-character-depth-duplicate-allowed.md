# 결정 기록: 캐릭터 분해에서 depthOrder 중복을 허용한다

작성일 2026-08-31 · 상태 **결정됨 / 미구현**

## 한 줄 요약

캐릭터의 최종 산출물은 **파츠 세트**이지 조립된 장면이 아니므로, 파츠 간 앞뒤 순서를
강제하는 `PART_DEPTH_DUPLICATE` 하드 실패를 캐릭터 경로에서는 걷어낸다.

## 관측된 문제

실 job의 `decompose` 공정이 `PART_DEPTH_DUPLICATE`로 3회 재시도를 전부 소진하고 죽는다.
화면에서는 파츠분해가 계속 진행 중인 것처럼 보인다.

```
공정 892e7b7a-... 실패 (모델 gpt-5.6-luna)
Noxtend.Domain.Job.PartValidationException: 깊이 순서 4 가 중복됩니다: 왼쪽 어깨 갑주, 귀걸이
공정 892e7b7a-... 실패 (모델 gpt-5.6-luna)
Noxtend.Domain.Job.PartValidationException: 깊이 순서 2 가 중복됩니다: 머리카락, 오른손 검
```

DB 상태 — 같은 job에서 Decompose 두 개가 모두 소진되었다.

| 공정 | 상태 | 시도 | 실패 사유 |
|---|---|---|---|
| Analyze ×2 | Succeeded | 1 | — |
| Extract ×2 | Succeeded | 1 | — |
| Decompose `CD5F678A` | Failed | 3/3 | `PART_DEPTH_DUPLICATE` |
| Decompose `892E7B7A` | Failed | 3/3 | `PART_DEPTH_DUPLICATE` |

workstream L(`76b15f1`, 2026-08-28)에서 같은 증상을 프롬프트 tie-break 규칙으로 완화하려
했으나(`docs/specs/2026-08-28-decompose-depth-tiebreak.md`), 재발했다. 프롬프트는 지시이지
강제가 아니므로 모델이 따르지 않으면 그대로 실패한다.

## 왜 모델이 못 지키는가

중복으로 묶인 파츠들을 보면 **서로 겹치지 않는 파츠들**이다.

- 왼쪽 어깨 갑주 ↔ 귀걸이
- 머리카락 ↔ 오른손 검
- (workstream L 관측) 벨트 버클 · 검 2자루 · 목걸이 · 귀걸이가 전부 `1`

어깨와 귀는 겹치지 않는다. 왼쪽 어깨와 오른손도 겹치지 않는다. 겹치지 않는 두 파츠
사이에 "어느 쪽이 앞이냐"는 답이 정해지지 않는 질문이다. 현재 규칙
(`SeedPrompts.cs:697`)은 그 답을 강제한다.

```
- depthOrder: 1 is nearest to the viewer. Assign a strict ranking across every
  part — never the same value twice, even when several parts sit at genuinely
  similar visual distance ...
```

즉 모델의 실수가 아니라 **요구 자체가 캐릭터 형상에 맞지 않는다.** 캐릭터에서
`depthOrder`가 실질적으로 뜻하는 것은 카메라 거리가 아니라 **몸에서 바깥으로 나가는
레이어 순서**(베이스바디 → 옷 → 갑주·벨트 → 장신구)이고, 이 축에서는 서로 다른 부위의
파츠에 전순서를 매길 수 없다.

## 결정의 근거 — 캐릭터의 최종 요구사항은 파츠 세트다

**이것이 이 결정의 핵심이며, 배경과 캐릭터가 갈리는 지점이다.**

### 근거 1. 캐릭터 조립은 설계된 적이 없다

배경 조립은 정식 사이클을 밟았다 — `docs/archive/2026-08/scene-assembly/`에 plan ·
design · analysis · report가 모두 있고, `SceneLayoutComposer`에
`Design Ref: scene-assembly §3.2 — 이 설계의 심장` 주석이 붙어 있다.

캐릭터 조립은 대응 문서가 없다. `character-studio.design.md`의 "조립"은
`화면 조립·/character 전환` — UI 배치를 뜻하며 3D가 아니다. 프론트엔드에서도
`useSceneLayout`을 쓰는 화면은 `BackgroundStudioScreen` 하나뿐이고, 캐릭터 화면은
`depthOrder`를 읽지 않는다.

### 근거 2. 추출 프롬프트가 리깅을 산출물로 명시한다

`ExtractCharacterSystem`이 관절마다 파츠를 쪼개는 이유를 직접 밝힌다.

```
a joint needs two independently posable pieces on either side of it for 3D rigging,
never one mesh spanning across it
```

관절 양쪽이 독립적으로 움직여야 하기 때문이다. 이 파이프라인의 산출물은 DCC 툴·게임
엔진으로 넘어가 스켈레톤과 웨이트를 받을 **파츠 세트**다. 브라우저 안에서 붙여 보이는
것은 목적이 아니다.

### 근거 3. 파츠 간 치수 정합을 보장하지 않기로 이미 정했다

벨트 등 파츠 간 치수 정합을 프롬프트로 강제하지 않고 모델러 검수에 위임하기로 확정한
바 있다. 파츠는 각각 따로 4방향 이미지를 만들고 각각 3D화하므로 치수가 맞을 보장이
없다. 자동 조립은 치수 정합을 전제하므로, 이 결정과 양립하지 않는다. 조립을 하려면
치수 정합 결정부터 뒤집어야 하고 그것은 훨씬 큰 작업이다.

## 결정

1. **캐릭터 파이프라인에 3D 조립 단계를 이번 스코프에서 만들지 않는다.** 최종 산출물은
   파츠 세트다. (조립 뷰 자체를 영구히 배제하는 것은 아니다 — 아래 "보류된 후속 옵션"
   참고)
2. 따라서 캐릭터 분해에서 **`depthOrder` 중복을 허용한다.** `PART_DEPTH_DUPLICATE`로
   공정을 실패시키지 않는다.
3. **배경은 현행 유지.** 배경에서 `depthOrder`는 실제 소비처가 있다.

### 배경에서 중복 금지가 필요한 이유 (유지 근거)

`SceneLayoutComposer.cs:60`이 값을 z좌표에 직접 넣는다.

```csharp
// 같은 깊이는 DepthOrder 로 앞뒤를 가른다 — 작을수록 앞 (B-05)
var z = -FarDistance * (1 - depth) - (part.DepthOrder ?? 0) * 0.1;
```

두 파츠가 같은 값이면 z가 정확히 같아져 3D에서 겹친다. 배경은 물체가 공간에 흩어져
있어 앞뒤 순서가 자연스럽게 정의되므로, 모델에게 무리한 요구도 아니다.

## depthOrder 소비처 전수 조사 (2026-08-31 기준)

| 위치 | 용도 | 캐릭터 영향 |
|---|---|---|
| `SceneLayoutComposer.cs:60` | z좌표 계산 | 없음 — 배경 전용 |
| `GetSceneLayoutHandler.cs:69` | 위 계산에 값 전달 | 없음 — 배경 화면만 호출 |
| `PartsOverlay.tsx:205` | 목록 번호 표시 `1. 몸통` | 없음 — 배경 화면 |
| `PartGallery.tsx:237` | 상세 표시 | 없음 — 배경 화면 |
| `JobResponse.cs:244` | API 응답 필드 | 값은 실려 나가나 캐릭터 화면이 읽지 않음 |
| `PipelineJobConfiguration.cs:155` | DB 저장 | 저장만 |
| `PipelineJob.cs:706` | **중복 검증** | 이번 결정 대상 |

## 이번에 하지 않는 것

- **`depthOrder` 필드 자체를 캐릭터 스키마에서 제거하지 않는다.** 프롬프트 · JSON
  스키마 · DB · API 응답이 모두 엮여 변경 범위가 크고, 나중에 조립이 필요해지면
  되돌리기 어렵다. 값은 계속 받아 저장하되 **중복을 실패 사유로 삼지 않는다**.
- 배경(`DecomposeV3System`) 규칙은 건드리지 않는다.
- 재시도 횟수 상한(`MaxAttempts`)이나 백오프 정책은 건드리지 않는다.
- 프롬프트의 tie-break 문구(workstream L)는 그대로 둔다 — 모델이 지키면 더 나은 값이
  나오고, 안 지켜도 이제 실패하지 않는다. 지시에서 강제로 격하되는 것뿐이다.
- 추출 프롬프트에 나열 순서 지시를 넣지 않는다. 검토했으나 제외했다 — 이미 분리
  규칙으로 긴 프롬프트에 요구를 더하면 **분리 규칙 준수율을 위협**하고, 분리 규칙이
  순서보다 훨씬 중요하다.

## 보류된 후속 옵션 — 캐릭터 조립 뷰 (미착수)

이번 결정과 별개로, 캐릭터에도 배경처럼 "전체를 한 화면에 모아 보는" 조립 뷰를 붙일지
검토했다. **이번 스코프에서는 하지 않기로 했다** — job을 안 죽게 만드는 것과 새 뷰를
만드는 것은 별개 크기의 작업이고, 지금은 전자만 한다. 다만 다음에 조립 요구사항이 실제로
들어오면 이어서 쓸 수 있도록 검토 내용을 남겨둔다.

### 이게 가능한 이유

배경의 `SceneLayoutComposer`는 **외부 호출이 없는 순수 계산**이다(주석: *"결정적
계산이다 — 입력은 배치·깊이·지평선뿐"*). 캐릭터에도 같은 형태의 입력이 이미 있다 —
Decompose가 파츠별 `placements`·`depthOrder`를 내고, Analyze가 장면 명세를 낸다.
엔드포인트(`GET api/jobs/{jobId}/scene-layout`)에도 카테고리 제한이 없어 지금도 캐릭터
job에 응답은 한다(값이 배경 전제로 계산되어 틀릴 뿐).

즉 **API 비용 없이(LLM도 3D 공급자도 추가 호출 안 함) 붙일 수 있다.**

### 왜 배경 공식을 그대로는 못 쓰는가

`SceneLayoutComposer.cs`의 계산은 배경의 두 전제 위에 있다.

```csharp
var footY = bounds.Y + bounds.H;                       // 아래일수록 카메라에 가까움 (원근)
var depth = Math.Clamp((footY - horizonY) / span, ...);
var z = -FarDistance * (1 - depth) - (part.DepthOrder ?? 0) * 0.1;
instances.Add(new SceneInstance(part.PartId, ordinal, x, 0, z, rotationY: 0, scale));
//                                                        ↑ y 항상 0 — 바닥에 서 있다고 가정
```

캐릭터 파츠(귀걸이 등)는 바닥에 서 있지 않고 이미지 아래쪽에 있다고 카메라에 가까운
것도 아니다. 이 공식을 그대로 쓰면 귀걸이가 지평선 근처에 거대하게 배치되는 식으로
틀어진다. **캐릭터 전용 Composer가 별도로 필요하다** — 다만 원근 항을 빼고 `depthOrder`
(레이어 순서)만 z로 쓰면 되므로 배경보다 더 단순하다.

### 이번 결정과의 관계 — depthOrder 중복을 어떻게 다룰지가 갈린다

조립 뷰를 붙이면 `depthOrder`에 실제 소비처가 다시 생긴다. 중복이 있으면 배경처럼 z가
겹친다. 이번 결정(중복 허용)과 맞물려 두 갈래가 있다.

- **서버 Composer가 중복을 렌더링 편의로 처리** (권장 방향): 캐릭터 전용 Composer 안에서
  중복된 파츠끼리만 z를 살짝 벌려 그린다. 도메인 검증(`ValidatePartDetails`)은 안
  건드리므로 boinred 영역 밖이다. `SceneAssemblyView`의 원칙(*"브라우저는 계산하지 않고
  그린다 ... 정본은 서버가 저장한 조립 명세"*)도 지킨다.
- **클라이언트에서 처리**: 위 원칙에 어긋나므로 채택하지 않는다.

이렇게 하면 중복 허용 결정을 뒤집지 않고도 조립 뷰를 얹을 수 있다 — 저장되는 값은
중복이 있는 그대로 두고, 화면에 그릴 때만 임의로 갈라 보여준다는 뜻.

### 이번에 조립 뷰를 만들지 않기로 한 이유

1. **지금 급한 것과 크기가 다르다.** job이 죽는 문제는 검증 조건 하나로 끝나지만, 조립
   뷰는 새 Composer + 뷰 전환 UI + (겹침 처리 방식 확정)이 필요하다.
2. **"미리보기"와 "정본"의 성격이 다르다.** 배경의 조립 명세는 정본(사용자가 그 자리에서
   보는 최종 결과)인데, 캐릭터는 치수 정합을 보장 안 하기로 했으므로 조립 뷰가 있어도
   그건 정본이 아니라 **모델러가 대략을 가늠하는 미리보기**다. 같은 컴포넌트
   (`SceneAssemblyView`)를 성격이 다른 두 용도로 재사용하면 나중에 혼동 여지가 있다 —
   착수 시 이 구분을 스펙에 명시해야 한다.
3. **요구사항이 아직 확정되지 않았다.** "필요할 것 같다"는 논의 단계이지, 어떤 정확도가
   필요한지·모델러 검수 워크플로우에 어떻게 끼는지는 정해지지 않았다.

### 다음에 착수할 때 확인할 것

- 캐릭터 전용 Composer 함수 신설 (원근 항 제거, y 실값 사용, z는 depthOrder만)
- 겹치는 depthOrder를 렌더 시점에 어떻게 벌릴지 (Composer 내부, 도메인 미변경)
- `SceneAssemblyView`를 그대로 재사용할지, 캐릭터용 변형이 필요한지 (입력 형식은 같은
  `SceneInstance`라 재사용 가능성 높음)
- 캐릭터 화면에 뷰 전환 UI 추가 (`?view=scene` 상당)
- "미리보기"라는 성격을 화면 문구·문서에 명시 — 정본으로 오인되지 않게
- 이 문서의 "결정" 섹션 1번(조립 단계를 만들지 않는다)을 갱신 필요

## 예상 함정

1. **구현이 boinred 영역을 건드린다.** 중복 검증은
   `PipelineJob.ValidatePartDetails`(`PipelineJob.cs:703-715`)에 있고, 이 코드와 그
   주변 도메인 규칙의 작성자는 boinred(2026-08-01
   `feat: 공정 진행 표시·계약 위반 재시도·토큰 기반 비용`)다. **착수 전 확인이 필요하다.**
   `DecomposeStage.Interpret`에서 입력을 손질해 우회하는 방법도 검토했으나, 그 함수의
   해당 라인들 역시 boinred 작성분이고 *"유효성 검사는 엔티티가 한다 — 규칙이 한 곳에
   있어야 흩어지지 않는다"* 는 주석 의도에서 벗어난다.
2. **기존 테스트가 깨진다.** `PartValidationTests.ValidationErrorsReachTheTaskAsErrorCodes`
   의 `flavor: "depth"` 케이스가 `PART_DEPTH_DUPLICATE`를 기대한다. boinred가 의도적으로
   못박아둔 것이므로, 카테고리별로 기대값을 나누는 형태가 되어야 한다.
3. **카테고리별 분기를 어디에 둘 것인가.** `ValidatePartDetails`는 현재 카테고리를 보지
   않는다. 캐릭터만 예외로 두려면 분기가 도메인에 들어오는데, 규칙이 카테고리별로
   갈리기 시작하는 첫 사례가 될 수 있다. 설계 판단이 필요하다.
4. **조립이 나중에 필요해질 가능성.** 이 결정은 "현재 시점"의 요구사항에 근거한다.
   캐릭터 조립이 요구사항으로 들어오면 이 문서부터 다시 읽어야 한다. 그때는 치수 정합
   결정도 함께 재검토 대상이고, 위 "보류된 후속 옵션" 절부터 이어서 시작하면 된다.

## 검증 방법

1. **단위**: 캐릭터 분해 응답에 `depthOrder` 중복이 있어도 공정이 `Succeeded`가 되는
   테스트 추가. 배경 응답에 중복이 있으면 여전히 `PART_DEPTH_DUPLICATE`인 테스트 추가.
2. **회귀**: 백엔드 전체 테스트. 단 로컬에는 `dotnet`이 없어 SDK 컨테이너로 돌린다
   (아래 참고).
3. **실 job**: 이번에 죽은 것과 같은 소스 이미지로 캐릭터 job을 다시 돌려 분해가
   통과하는지 확인. 실비용 발생.

### 로컬 테스트 실행 참고

호스트에 `dotnet`이 없다. 백엔드는 Docker로 빌드하므로 테스트도 컨테이너에서 돌린다.

```bash
cd apps/backend && docker run --rm \
  -v "$PWD":/src -w /src \
  -v noxtend-nuget:/root/.nuget/packages \
  -v /var/run/docker.sock:/var/run/docker.sock \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
```

`-v /var/run/docker.sock`을 빼면 Testcontainers 기반 테스트(`*PersistenceTests`,
`TaskConcurrencyTests`, `*MigrationTests`)가 전부 `DockerUnavailableException`으로
실패한다. Skia 네이티브 의존 테스트(`SkiaLoadsTests`, `MeshInputNormalizerTests`)는 이
이미지에서 여전히 실패하므로, 통과 여부 판단에서 제외한다.

## 승인

- [x] 캐릭터 조립을 이번 스코프에서 만들지 않는다 — 최종 산출물은 파츠 세트 (사용자 결정,
      2026-08-31). 조립 뷰는 "보류된 후속 옵션"으로 남김, 요구사항 확정 시 재개
- [x] 캐릭터에서 depthOrder 중복 허용 (사용자 결정, 2026-08-31)
- [ ] `ValidatePartDetails` 변경에 대한 boinred 확인 — **구현 착수 전 필요**
