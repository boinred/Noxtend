# Reconstruct 회수 기준 테스트·코드 지도 효과 측정 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `ReclaimPlan`의 3D 메시 공정(Reconstruct) 회수 기준을 테스트로 고정하고, 코드 지도(`docs/xHuman/CODEMAP.md`) 도입 전후의 탐색 비용을 측정한다.

**Spec:** 별도 설계 문서 없음. 근거는 [작업 실행 지도](../../xHuman/job-execution.md)의 검증 중 확인한 공백(`ReclaimPlanTests`가 Reconstruct를 검사하지 않음, 클래스 주석이 현재 코드와 다름)이다.

## Global Constraints

- 애플리케이션 코드(`ReclaimPlan.cs`, `TaskSweeper.cs`)는 바꾸지 않는다. 현재 동작을 고정하는 테스트와 낡은 주석만 고친다.
- 기대값: `MeshGenerationOptions.LeaseSeconds = 1000`이면 `TaskKind.Reconstruct`의 방치 기준은 2000초.
- 측정은 읽기 전용이다. 측정용 worktree는 scratchpad에 detached로 만들고 끝나면 제거한다. 실제 AI·유료 호출 없음.

## Review Focus

1. 새 단정이 실제로 Reconstruct 분기를 검사하는가 → Task 1 Step 2 mutation 결과.
2. 측정 두 조건이 지도 유무 외에는 같은가(프롬프트·모델·에이전트 종류) → Task 2 기록.

## 실행 방식

**Subagent-driven.** Task 1은 구현 에이전트 1명과 별도 리뷰 에이전트가 맡는다. 측정(Task 2)은 코드 변경이 없고 Task 1과 독립이라 병렬로 진행한다.

## Task 1: Reconstruct 회수 기준 테스트

**Files:**

- Modify: `apps/backend/Noxtend.Tests/Api/ReclaimPlanTests.cs`
- Modify: `docs/xHuman/job-execution.md` — 60행 `(Reconstruct 기준은 미검증)` 제거

- [x] **Step 1: 테스트** — `EachKindWaitsTwiceItsOwnLease`에서 `new MeshGenerationOptions { LeaseSeconds = 1000 }`을 세 번째 인자로 넘기고 `plan[TaskKind.Reconstruct] == TimeSpan.FromSeconds(2000)`을 단정한다. 메서드 summary에 3D 리스가 분 단위라는 점을 한 줄로 보탠다.
- [x] **Step 2: 실패 확인(mutation)** — `ReclaimPlan.Idle`의 Reconstruct 분기를 잠시 `job.Lease * 2`로 바꿔 새 단정이 실패하는지 확인하고 즉시 되돌린다. 되돌린 뒤 `git diff apps/backend/Noxtend.Api`가 비어야 한다.
- [x] **Step 3: 낡은 주석** — 클래스 summary의 "지금은 Extract 하나로 고정돼 있어 Generate 의 미확인 메시지가 영원히 남는다." 문장을 지운다(현재 모든 종류를 회수함).
- [x] **Step 4: 검증** — `dotnet test apps/backend/Noxtend.Tests --filter 'FullyQualifiedName~ReclaimPlanTests'` 통과 후 Backend 전체 회귀(AGENTS.md 명령, 유료 smoke 제외).
- [x] **Step 5: Commit** — `test(backend): cover reconstruct reclaim threshold`

## Task 2: 코드 지도 효과 측정

**Files:**

- Modify: 이 문서의 실행 기록(코드 변경 없음)

- [x] **Step 1: 조건** — A(도입 전) `d2cad8d`, B(도입 후) `868d0a6`을 각각 scratchpad의 detached worktree로 만든다.
- [x] **Step 2: 실행** — 대표 작업 3개(지도 보정에 쓰지 않은 질문)를 조건×작업마다 2회, 총 12회 실행한다. 같은 에이전트 종류·모델·프롬프트를 쓰고, 각 worktree의 `AGENTS.md`부터 읽고 그 탐색 지침을 따르게 한다.
- [x] **Step 3: 채점** — 에이전트 결과의 총 토큰·도구 호출 수·소요 시간을 모으고, 답의 정확도를 실제 코드로 채점한다.
- [x] **Step 4: 기록** — 결과 표를 실행 기록에 남기고 worktree를 제거한다.

## 실행 기록

- 실행 방식: Subagent-driven, 브랜치 `claude/reconstruct-reclaim-test`, 시작 `868d0a6`.
- Task 1: `dd73930`. 집중 테스트 2개 통과. mutation(Reconstruct를 `job.Lease * 2`로 변경) 시 새 단정 실패(기대 00:33:20, 실제 00:03:20), 되돌린 뒤 `Noxtend.Api` diff 없음. Backend 전체 회귀 1,631 통과·0 실패·0 건너뜀(7분 19초).
- Task 1 수정: `a51167a`. 작업 리뷰에서 Step 3 누락으로 지적된 클래스 요약의 낡은 문장을 제거했다. 범위 재리뷰 ADDRESSED, `ReclaimPlanTests` 2개 통과. 전체 브랜치 최종 리뷰는 "With fixes"였고 이번 수정 묶음에서 반영했다.
- Task 2: Explore 에이전트(sonnet) 12회, 작업당 조건별 2회. 지표는 에이전트 완료 알림의 `subagent_tokens`·`tool_uses`·`duration_ms`다.

| 작업 | 조건 | 평균 토큰 | 평균 도구 호출 | 평균 소요 | 정확도 |
| --- | --- | ---: | ---: | ---: | --- |
| T1 작업 취소 흐름 | A 도입 전 | 77,572 | 9.0 | 37.8초 | 2/2 정확 |
| T1 작업 취소 흐름 | B 도입 후 | 76,275 | 10.5 | 41.2초 | 2/2 정확 |
| T2 migration 추가 | A 도입 전 | 63,925 | 7.5 | 34.9초 | 2/2 정확, 2/2가 스킬 참조의 `--startup-project` 불일치 발견 |
| T2 migration 추가 | B 도입 후 | 63,418 | 5.0 | 21.8초 | 핵심 정확, r1은 `dotnet-tools.json`이 없다고 오독, r2는 `.config` tool manifest 부재만 언급 |
| T3 2D 기준 이미지 생성 | A 도입 전 | 74,106 | 8.0 | 28.9초 | 2/2 정확 |
| T3 2D 기준 이미지 생성 | B 도입 후 | 75,048 | 6.5 | 27.7초 | 2/2 정확 |
| 전체 | A | 71,868 | 8.2 | 33.9초 | |
| 전체 | B | 71,580 | 7.3 | 30.3초 | |

전체 행은 원시 값 6개로 계산했다(예: B 소요 181,539ms / 6 = 30.3초). 작업별 반올림 평균으로 합치면 30.2초로 어긋난다.

개별 실행 범위(최소–최대): 도구 호출 T1 A 8–10 / B 10–11, T2 A 7–8 / B 4–6, T3 A 7–9 / B 6–7. 소요(초) T1 A 35.7–39.9 / B 37.4–45.1, T2 A 30.9–38.9 / B 19.2–24.4, T3 A 25.9–32.0 / B 24.4–31.1.

- 해석: 토큰은 차이 없음(−0.4%). 도구 호출 −10%, 소요 −11%이나 감소는 새 지도가 생긴 T2에서 가장 컸다(호출 −33%, 소요 −38%, n=2라 유의성 없음). T2의 B 1회(r1)는 `dotnet-tools.json`이 없다고 오독했으므로 속도 이득에 확인 생략이 섞였을 가능성이 있다. T1은 B가 오히려 느렸고(호출 +17%, 소요 +9%), T3는 평균이 낮지만(호출 −19%, 소요 −4%) 범위가 겹친다. 토큰 값은 에이전트 시스템 프롬프트 등 고정분이 대부분이라 탐색량 차이에 둔감한 것으로 추정한다(미측정).
- 부작용: 지도가 답 하나를 주자 B는 다른 문서와의 충돌을 보지 않았다. A가 찾은 스킬 참조의 `--startup-project apps/backend/Noxtend.Api`는 `dotnet ef migrations has-pending-model-changes`로 확인한 결과 Design 패키지가 없어 실패하므로 Infrastructure로 고쳤다. B의 오독을 부른 것으로 보이는 지도 문장도 `dotnet-tools.json` 내용을 명시하도록 고쳤다.
- 한계: 조건당 작업별 2회라 통계적 유의성은 없다. 하위 에이전트에는 주 세션의 지침이 함께 주입되므로 두 조건 모두 각 worktree의 `AGENTS.md`를 먼저 읽게 해 차이를 줄였으나 없애지는 못했다. 주 세션에 주입된 AGENTS.md(HEAD)가 `docs/xHuman/CODEMAP.md`를 먼저 읽으라고 안내하므로 A도 그 언급을 접했다. A 에이전트 보고에 적힌 읽은 문서는 AGENTS.md → `docs/xHuman/README.md` → 영역 지도이고 CODEMAP을 인용한 보고는 없었지만, 메인 체크아웃을 읽었을 가능성은 기록으로 배제할 수 없다.
