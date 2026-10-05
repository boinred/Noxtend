---
name: pdca-cycle
description: Orchestrate this repository's PDCA lifecycle while keeping tool state and document artifacts synchronized. Use for PDCA plan, design, do, analyze, iterate, report, status, archive, cleanup, or recovery from missing or stale active-feature state.
---

# PDCA Cycle

Use `$pdca` as the phase execution engine and add the project-specific safeguards below. Never infer an active PDCA feature solely from documents on disk.

## State Invariant

Keep these two sources consistent after every phase transition:

- Lifecycle state returned by `$pdca status` or the corresponding bkit status tool
- Canonical artifacts under `docs/01-plan`, `docs/02-design`, `docs/03-analysis`, and `docs/04-report`

Use `docs/03-analysis` for Check/Act analysis and `docs/archive` for completed or archived records. `docs/.pdca-status.json` is supporting evidence; file titles or past conversation are not a substitute for live lifecycle status.

An active feature must be registered in lifecycle state, use one stable feature key, reference the expected artifacts, and have those artifacts present on disk. Document headings, checkboxes, or status text do not register or complete a phase by themselves. Do not use direct edits to `docs/.pdca-status.json` as a normal phase transition.

If state and files disagree, pause the requested transition and reconcile them through `$pdca` or supported bkit tools. Report the mismatch and tool failure when safe reconciliation is unavailable.

If the PDCA engine or bkit tools are unavailable, do not simulate a transition by editing status JSON or report a phase as complete. State the tool limitation and evidence checked; continue separately authorized independent work. Use only numeric match rates returned by the analysis tool, never infer a percentage from code or checkboxes.

Several sessions may share the lifecycle state file concurrently. Re-read it immediately before any write, change only the entries for the feature being transitioned, and never bulk-rewrite, reorder, or drop entries owned by other features or sessions.

## Start Every Action

1. Resolve one stable `{feature}` key from the user's request or existing artifacts.
2. Read project PDCA configuration. Use a 95% report threshold and 3 maximum iterations unless project configuration overrides them.
3. Run `$pdca status` and inspect the canonical files for `{feature}`.
4. Reconcile any state/artifact mismatch before advancing.

After every transition, re-read the feature key, returned state, artifact path, and artifact content before reporting the new phase.

## Phase Workflow

### Plan

1. Run `$pdca plan {feature}` before creating or updating the canonical plan.
2. Complete the plan content and its success criteria.
3. Mark the plan phase complete through the supported PDCA transition.
4. Run `$pdca status` again and verify that the feature and plan artifact are registered.

### Design

1. Verify that both the registered plan phase and plan file exist.
2. Run `$pdca design {feature}` before creating or updating the canonical design.
3. Review architecture, interfaces, failure handling, security, and test strategy.
4. Mark design complete only after its review findings are resolved or explicitly accepted.
5. Run `$pdca status` again and verify the registered phase and design artifact.

For plan- or design-only requests, do not modify application source code.

### Do

1. Verify that design is registered as complete and its canonical document exists.
2. Run `$pdca do {feature}`.
3. Use `$tdd-cycle` for implementation and verification.
4. Complete the Do phase only after targeted tests and full regression pass.
5. Run `$pdca status` and verify the transition.

### Analyze, Iterate, and Report

1. Run `$pdca analyze {feature}` after implementation.
2. Stop and report the tool error if analysis fails or returns no numeric match rate.
3. When the match rate is below the configured threshold and iterations remain, run `$pdca iterate {feature}` and repeat analysis.
4. When the match rate reaches the threshold or the maximum iteration count is reached, generate or update `$pdca report {feature}` without waiting for a separate prompt.
5. Run `$pdca status` and verify the match rate, iteration count, report phase, and report artifact.

At the maximum iteration count, report unresolved gaps clearly even though the report is generated.

### Archive and Cleanup

- Archive only when the completion report exists and lifecycle completion criteria are satisfied.
- Run `$pdca archive {feature}` only when requested or when the active workflow explicitly includes archival.
- After archival, verify both the archived state and moved artifacts.
- Run `$pdca cleanup` only on an explicit cleanup request; never treat cleanup as implicit completion.

## Pause Conditions

Pause only for blocking ambiguity, an unsafe or destructive action, missing external credentials, an unrecoverable state/artifact mismatch, an unresolvable verification failure, or an explicit user stop request.
