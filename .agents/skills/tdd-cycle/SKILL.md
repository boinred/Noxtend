---
name: tdd-cycle
description: Implement and verify repository code changes through small Red-Green-Refactor cycles. Use for feature additions, behavior changes, bug fixes, refactoring, and test work. Do not use for plan- or design-only requests that must not modify source code.
---

# TDD Cycle

Implement the user's latest natural-language request in the smallest verifiable increments. Preserve unrelated work and use the repository's existing test conventions.

## Plan → Design → Implement → Verify

1. Plan: establish the requirement and baseline.
   - Use the complete user request and incorporate later steering without discarding the active objective.
   - State only assumptions that affect behavior, scope, or verification.
   - Inspect relevant code, tests, project commands, and the working tree.
   - Define observable success criteria before editing.
   - Stop before source changes when the request is plan- or design-only.

2. Design: select the smallest existing-code solution.
   - For API/DB/queue/UI changes, settle the contract, state transitions, failures, cancellation, retry, compatibility, and verification method before implementation.
   - Small changes need only the four-stage rationale in the work report. Do not create a feature document or abstraction for every edit.
   - For changes across layers or decisions affecting operations/data, update existing Plan/Design/ADR artifacts when needed. Use `$pdca-cycle` only when changing PDCA artifacts or lifecycle state.
   - Parallelize independent files or checks; assign one owner and sequence shared API contracts, DB schemas, and PDCA state writes.

3. Implement — Red: demonstrate the missing behavior.
   - Select one small behavior or one reproducible defect.
   - Add or adjust the narrowest useful test first.
   - Run the targeted test and confirm that it fails for the expected reason.
   - If an automated test is impractical, define and run the closest repeatable executable check before implementation; report the limitation.

4. Implement — Green: make the smallest change that passes.
   - Change only files required by the failing behavior.
   - Follow `$karpathy-guidelines` and the repository comment rules.
   - Run the targeted test until it passes.
   - Do not mix unrelated cleanup or speculative abstractions into the change.
   - Never hide failures as an empty list, successful response, zero cost, or invented progress. Verify relevant existing error handling within the changed scope.

5. Implement — Refactor without changing behavior.
   - Remove duplication or clarify names only when the passing implementation benefits from it.
   - Re-run the targeted test after each material refactor.

6. Verify completion.
   - Run all tests directly related to the changed behavior.
   - Read `$noxtend-workflow` and its [verification reference](../noxtend-workflow/references/verification.md), then run full regression for each stack the change touches after targeted tests pass.
   - Backend general regression excludes paid smoke tests: `dotnet test apps/backend/Noxtend.slnx --filter 'FullyQualifiedName!~TripoSmokeTests&FullyQualifiedName!~SimilaritySmokeTests'`.
   - Frontend (repository root): `pnpm test`, `pnpm lint`, `pnpm typecheck`, `pnpm build`, `pnpm test:e2e`. API contract changes touch both stacks.
   - Treat the work as complete only when both targeted and full regression checks pass.
   - If credentials, services, environment failures, or unrelated existing failures prevent verification, report the exact command and failure instead of claiming completion.
   - Distinguish implemented behavior, automated checks, manual observations, and real API verification. Skipped or uncollected tests are not passing evidence; identify a pre-existing failure only with baseline evidence.

Ordinary documentation, `AGENTS.md`, or formatting configuration changes do not need a new behavior test or PDCA phase. Use the relevant repeatable configuration, link, command, scope, and whitespace checks from the verification reference instead.

## Execution Rules

- Use agent-native execution in Codex sessions.
- Use `Tools/TddAgent` only for CLI, API, or MCP automation that explicitly requires an external provider runtime.
- Never overwrite, revert, or include unrelated user changes.
- Keep each Red-Green-Refactor slice independently understandable and verifiable.
