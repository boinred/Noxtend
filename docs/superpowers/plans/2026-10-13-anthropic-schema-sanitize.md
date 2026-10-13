# Anthropic 구조화 출력 스키마 정리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Anthropic 모델로 실행한 공정이 프롬프트 스키마의 미지원 키워드 때문에 400으로 실패하지 않도록, `AnthropicProvider`가 요청 전에 스키마를 Anthropic 구조화 출력이 받는 형태로 정리한다.

**Spec:** 별도 설계 문서 없음. 근거는 로컬 API 로그의 `AnalyzeSprites` 실패(`output_config.format.schema: For 'array' type, property 'maxItems' is not supported`, 모델 `claude-haiku-5-5`)와 [Anthropic structured outputs 문서](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)의 JSON Schema 제한(숫자 범위·문자열 길이·복잡한 배열 제약 미지원, 공식 Python·TypeScript SDK는 제거 후 클라이언트 검증)이다.

## Global Constraints

- 정리는 `AnthropicProvider` 전송 직전에만 적용한다. 저장된 프롬프트 스키마, OpenAI·Google 어댑터, `LlmCalls` 기록 형식은 바꾸지 않는다.
- 제거 키워드: `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `minLength`, `maxLength`, `maxItems`, 값이 0·1이 아닌 `minItems`. 유지: `type`, `enum`, `const`, `anyOf`, `allOf`, `$ref`, `$defs`, `required`, `additionalProperties`, `items`, `properties`, `format`, `description`, 0·1인 `minItems`.
- `properties`·`$defs`·`definitions` 아래의 키는 속성·정의 이름이다. 이름이 `minimum`·`maxLength` 등이어도 지우지 않고 그 값(하위 스키마)만 재귀 정리한다.
- 제거한 제약은 각 공정 파서·도메인 규칙이 검증해야 한다. 2D 분석은 `SpritePlanParser`·`SpriteRules`가 이미 개수 1~12, 이름, `fps` 1~30, `motionNotes` 500자를 검사한다.
- 검증은 단위 테스트와 Backend 전체 회귀로 한다. 실제 Anthropic 호출·유료 smoke는 하지 않는다.

## Review Focus

1. 이름 보존: `properties` 아래 `"minimum": {"type":"number"}` 같은 속성은 남고, 그 하위 스키마의 `maximum`은 지워지는가 → Task 1 테스트.
2. 재귀 범위: `items`, `anyOf`·`allOf` 배열, `$defs`, 중첩 `properties`까지 정리되는가 → Task 1 테스트.
3. 검증 공백: 제거되는 제약을 쓰는 시드 스키마별로 파서가 같은 제약을 검사하는지 → Task 1 보고서의 대조표.

## 실행 방식

**Subagent-driven.** 작업 하나를 구현 에이전트가 맡고 별도 리뷰 에이전트가 검토한 뒤 전체 변경을 최종 리뷰한다.

## Task 1: Anthropic 스키마 정리

**Files:**

- Create: `apps/backend/Noxtend.Infrastructure/Llm/AnthropicJsonSchema.cs` — `internal static class`, `Dictionary<string, JsonElement> ForStructuredOutput(string jsonSchema)`
- Modify: `apps/backend/Noxtend.Infrastructure/Llm/AnthropicProvider.cs` — `OutputConfig.Format.Schema`에 위 함수 결과를 사용
- Create: `apps/backend/Noxtend.Tests/Infrastructure/AnthropicJsonSchemaTests.cs`
- Modify: `docs/xHuman/providers-and-prompts.md` — Anthropic 어댑터 설명에 전송 전 스키마 정리와 파서 검증 책임 추가, 확인 기준 갱신

- [x] **Step 1: 실패 테스트** — 시드 `SpriteAnalyzeSchema`(현재 `SeedPrompts`의 2D 분석 스키마 원문)를 넣으면 결과 JSON 어디에도 `maxItems`·`minLength`·`minimum`·`maximum`·`maxLength`가 없고 `minItems: 1`, `enum`, `required`, `additionalProperties: false`는 남는지 단정한다. 별도 사례: `properties`에 `minimum`·`maxLength`라는 이름의 속성, `items` 안 `minItems: 3`(제거), `anyOf` 배열과 `$defs` 안의 `exclusiveMinimum`(제거), 키워드가 없는 스키마는 의미상 그대로. 빌드 실패 또는 단정 실패를 확인한다.
- [x] **Step 2: 구현** — `JsonNode`로 파싱해 재귀 정리 후 `Dictionary<string, JsonElement>`로 돌려준다. 주석은 짧은 명사구.
- [x] **Step 3: 연결** — `AnthropicProvider.BuildRequest`가 새 함수를 쓰게 한다.
- [x] **Step 4: 대조표** — `SeedPrompts.cs`의 스키마 중 제거 키워드를 쓰는 것마다 해당 공정 파서·도메인 규칙이 같은 제약을 검사하는지 보고서에 표로 남긴다. 2D 분석 외 공백은 고치지 않고 보고한다.
- [x] **Step 5: 검증** — 새 테스트 통과 후 `dotnet build apps/backend/Noxtend.slnx`와 Backend 전체 회귀(유료 smoke 제외). `pnpm docs:check`, `git diff --check`.
- [x] **Step 6: Commit** — `fix(llm): strip unsupported schema keywords for anthropic`

## 실행 기록

- 실행 방식: Subagent-driven, worktree `Noxtend-worktrees/anthropic-schema`, 브랜치 `claude/anthropic-schema-sanitize`, 시작 `2db8d35`.
- 원인 확인: 로컬 Kubernetes `noxtend/api` 로그에서 `AnalyzeSprites` 공정이 `claude-haiku-5-5`로 2회 시도해 모두 Anthropic 400(`maxItems` 미지원)으로 실패. 이미지 생성 단계는 정상.
- Task 1: `0e4c32d`. 새 테스트 4건 RED(빌드 실패 CS0103) → GREEN. Backend 전체 회귀(유료 smoke 제외) 1,801 통과·0 실패·0 건너뜀(8분 53초). 문서 검사·`git diff --check` 통과. 독립 리뷰 Approved.
- 검증 대조: 2D 분석(`SpritePlanParser`·`SpriteRules`), 팔레트·scaleReference, placements 1~20, Similarity 6축·점수·길이는 서버가 같은 제약을 검사한다. Similarity 보정 값 범위는 파싱이 아니라 후보 생성(`SceneAdjuster`) 시점에 검사된다.
- `pattern`: 시드의 단순 정규식(`^#[0-9A-Fa-f]{6}$`, `^P[0-9]{2}$`)은 Anthropic 문서상 지원 범위라 유지했다.
- 미확인: 실제 Anthropic 호출로 수정 후 요청이 받아들여지는지는 확인하지 않았다. 로컬 API 파드는 아직 수정 전 이미지다.
