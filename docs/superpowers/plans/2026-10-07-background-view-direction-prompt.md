# 배경 Generate 방향 정의 프롬프트 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** 배경 Generate 프롬프트에 캐릭터의 방향 정의 조항을 이식하고, 캐릭터 프롬프트에서 배경 물체에도 유효한 규칙(놓인 자세 유지, 원본에 겹쳐 보이는 다른 파츠 미작화)을 더한 v3를 시드한다.

**Architecture:** `SeedPrompts`에 `BackgroundGenerateV3()`를 추가한다. v2와 같은 Prefix·`RotationContractTemplate`을 쓰고 Tail만 v3로 교체한다. 새 migration은 Generate/Background 슬롯을 비활성화하고 `MAX(Version)+1`로 v3를 활성 행으로 넣는다(`BackgroundSurfacePartsPrompt`와 같은 방식).

**Tech Stack:** .NET 10, EF Core migration(SQL Server), xUnit, Testcontainers SQL Server.

**Spec:** 사용자 요청(2026-10-07) — "배경 프롬프트에도 캐릭터처럼 방향 정의 조항 추가, 캐릭터를 기반으로 배경에 도움이 될 만한 내용 적용". 별도 설계 문서는 없고 이식 기준 문면은 [SeedPrompts.cs](../../../apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs)의 `GenerateCharacterTail`이다.

## Global Constraints

- `BackgroundGenerateV2()`와 그 구성 상수(`GenerateBackgroundV2Prefix`·`GenerateBackgroundV2Tail`) 문면은 바꾸지 않는다. 기존 migration `BackgroundGeneratePromptV2`가 실행 시점에 이 값을 읽는다.
- 캐릭터 문면은 1바이트도 바뀌면 안 된다. `PromptCompositionTests.CharacterGenerateSystem_IsByteIdenticalAfterTheRefactor` 해시는 그대로 통과해야 한다. `RotationContractTemplate`은 수정하지 않는다.
- 배경 어휘만 쓴다: `full-body`, `gender`, `character` 단어가 v3 System에 들어가면 안 된다. 배경색 `#F2F2F2`와 지면 접점 조항은 유지한다.
- 방향 규약은 캐릭터와 같아야 한다(같은 mesh 공급자 슬롯 `Front·Left·Back·Right`로 들어간다): right = 정면 기준 오른쪽 가장자리였던 면이 보이고 정면은 이미지 왼쪽을 향함, left = 그 반대.
- 허용 변수는 기존 Generate 목록 안에서만 쓴다. 새 변수 추가 없음.
- 실제 AI 호출·유료 smoke 금지. 검증은 단위 테스트와 Docker SQL Server migration 테스트로 한다.
- 코드·migration·`docs/xHuman/providers-and-prompts.md` 수정은 같은 커밋에 넣는다.

## Review Focus

1. 방향 규약이 캐릭터와 같은지: right에서 정면이 이미지 왼쪽을 향함.
2. v2·캐릭터 문면 불변: 캐릭터 해시 테스트, v2 메서드 diff 없음.
3. migration이 운영자 버전을 덮어쓰지 않고 Down이 직전 최고 버전을 되살림.

## 실행 방식

**실행 방식: Subagent-driven.** 작업이 하나라 구현 에이전트 1개와 구현에 참여하지 않은 리뷰 에이전트 1개로 진행한다.

## Task 1: 배경 Generate v3 시드·migration·테스트

**Files:**

- Modify: `apps/backend/Noxtend.Infrastructure/Llm/SeedPrompts.cs` — `GenerateBackgroundV3Tail` 상수, `GenerateBackgroundV3System` 조합, `BackgroundGenerateV3()` 공개 메서드
- Create: `apps/backend/Noxtend.Infrastructure/Persistence/Migrations/<timestamp>_BackgroundGeneratePromptV3.cs` (+ `.Designer.cs`, `dotnet ef migrations add BackgroundGeneratePromptV3`로 생성. 모델 변경이 없으므로 snapshot diff가 생기면 원인을 확인)
- Modify: `apps/backend/Noxtend.Tests/Infrastructure/PromptCompositionTests.cs`
- Modify: `apps/backend/Noxtend.Tests/Infrastructure/StrictSchemaComplianceTests.cs` — `"generate"` 항목을 `BackgroundGenerateV3()`로
- Create: `apps/backend/Noxtend.Tests/Infrastructure/BackgroundGeneratePromptV3MigrationTests.cs` — `BackgroundSurfacePromptMigrationTests` 구조를 따른다
- Modify: `docs/xHuman/providers-and-prompts.md` — Generate 단락(“`RunGenerationTaskHandler`가 활성 Generate 프롬프트를 조회하고…”) 뒤에 한 문장

**v3 System 조합**

```csharp
GenerateBackgroundV2Prefix + "\n\n"
+ RotationContractTemplate.Replace("{{original}}", "scene photo") + "\n\n"
+ GenerateBackgroundV3Tail
```

**`GenerateBackgroundV3Tail` 문면 (그대로 사용)**

```text
Rules:
- Draw exactly one object: the part named above. Nothing else.
- Draw it whole and unoccluded, even if the reference image shows it partly
  hidden behind something.
- Anything the reference image shows attached to or layered on this object that
  is NOT in this part's own description — a sign, a lamp, a banner, a poster, a
  pipe, climbing plants — belongs to another part. Do not draw it, even though
  the reference shows it there; continue this object's own surface naturally
  underneath.
- Do not reproduce the surrounding scene, other objects, ground, sky or horizon.
- Keep the palette, lighting direction, colour temperature, material feel and
  rendering style from the art direction above. Siblings drawn from the same
  direction must look like they belong together.
- Keep only the form shading and highlights on the object itself. Do not add a
  cast shadow, contact shadow, drop shadow, ground shadow, floor plane, pedestal
  or support surface beneath or around it.
- Centre the object with a small even margin.
- The required view is the side of the object the camera sees, orbiting it at
  the same height. The front is the face that points at the camera in the
  original scene photo (the face reference image 1 shows, when present):
  front = that face toward the viewer; right = orbit 90° so the side that was on
  the right edge of the front view now faces the viewer and the front face
  points toward the left edge of the image; back = seen from directly behind,
  with the front face not visible; left = orbit 90° the other way so the side
  that was on the left edge of the front view faces the viewer and the front
  face points toward the right edge of the image. Never leave the front face
  turned toward the camera on a side or back view.
- Keep the object's own resting pose identical across all four views — upright,
  lying on its side, leaning, tilted or toppled exactly as reference image 1 (or
  the description) shows it; only the camera orbits around it.
- Show only the required view. Keep camera elevation, distance, focal length,
  object scale and vertical alignment identical across all four directions.
  Rotate only around the object's vertical axis. Do not make a contact sheet.
- Use the exact same edge-to-edge background for every sibling image: one solid
  neutral light gray, #F2F2F2. No gradient, texture, pattern, vignette, horizon,
  transparency or checkerboard.
- The part rests on the ground of the scene: keep its ground-contact silhouette
  and footprint consistent across all four directions — a wall that meets the
  ground in a straight line from the front cannot meet it in a curve from the back.
```

**Note 문자열:** `background-view-direction (2026-10-07) — 캐릭터 방향 정의를 배경 물체 기준으로 이식(정면=원본 사진에서 카메라를 향한 면, 좌우 궤도 규약은 캐릭터와 동일), 놓인 자세 유지, 원본에 겹쳐 보이는 다른 파츠 미작화. 실 API 재검증 대기`

**Migration 계약**

- 새 행 Id `b6000000-0000-4000-8000-000000000005`, `SeededAt = new(2026, 10, 7, 6, 0, 0, TimeSpan.Zero)`, Kind `Generate`, Category `Background`.
- Up: 같은 (Kind, Category)의 모든 행 비활성화 → `MAX(Version)+1`로 v3 활성 행 INSERT. 다른 슬롯은 건드리지 않는다.
- Down: v3 행 삭제 → 같은 (Kind, Category)에서 Version이 가장 큰 행을 활성화(`BackgroundSurfacePartsPrompt.Down`과 같은 방식).

**테스트 (Red 먼저)**

1. `PromptCompositionTests`: `BackgroundGenerateV3_AddsViewDefinition_KeepingSceneWording` — 회전 계약 핵심 문장 3개(`reference image 1 is the FRONT view`, `SAME physical object`, `One-sided surfaces`), `the original scene photo`, `ground-contact silhouette`, `#F2F2F2` 포함; 신규 조항 `front face points toward the left edge`, `front face points toward the right edge`, `Never leave the front face`, `resting pose`, `belongs to another part` 포함; `full-body`·`gender`·`character` 미포함. `ComposedPrompts_LeaveNoUnreplacedTokens`에 v3 추가. 캐릭터 해시 테스트는 수정하지 않는다.
2. `BackgroundGeneratePromptV3MigrationTests`: Up이 활성 Background Generate를 v3 System과 같은 문면으로 하나만 남기고 다른 Generate 슬롯 Id는 그대로임; 운영자 version 9 행이 있으면 v3는 10; Down 후 운영자 version 5 행이 다시 활성.

**체크리스트**

- [ ] Red: 조합 테스트·migration 테스트 추가 후 실패 확인
- [ ] Green: `BackgroundGenerateV3()`·migration 구현
- [ ] StrictSchema 항목 교체, 문서 한 문장 추가
- [ ] 대상 테스트·전체 Backend 회귀 통과 후 한 커밋

**검증 명령**

```bash
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName~PromptCompositionTests|FullyQualifiedName~StrictSchemaComplianceTests|FullyQualifiedName~BackgroundGeneratePromptV3MigrationTests|FullyQualifiedName~SeedPromptsTests'
dotnet build apps/backend/Noxtend.slnx
dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'
```

## 실행 기록

- 실행 방식: Subagent-driven. 구현 에이전트 1개(Task 1), 독립 리뷰 에이전트 1개, 수정 범위 재리뷰 1개.
- Task 1 완료: `c90a625`(시드·migration `20261007063456_BackgroundGeneratePromptV3`·테스트·문서), `fcafb61`(리뷰 Minor 수정: v2 migration 테스트를 v2 시점에 고정, 테스트 주석 정리).
- 계획 외 변경: 기존 `BackgroundGeneratePromptMigrationTests`가 최신 migration까지 올려 v2 버전 번호를 단언하던 부분을 v2 migration 시점으로 고정했다.
- 검증: 대상 테스트 통과, `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'` 1560 통과·0 실패·0 건너뜀(최종 HEAD).
- 미해결: 실제 이미지 모델이 방향 규약대로 그리는지는 실 API로 확인하지 않았다. 롤백(Down) 시 운영자가 더 낮은 버전을 재활성화해 둔 경우 활성 행 충돌 가능성은 기존 배경 migration과 같은 패턴으로 유지했다.
