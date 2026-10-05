---
name: karpathy-guidelines
description: Coding standards for writing, reviewing, and refactoring code in this repository. Apply to every source change to keep code simple, explicit, and verifiable. Not for documents or planning-only work.
---

# Karpathy Guidelines

Write the simplest code that provably works. Every rule below exists to keep changes small, readable, and easy to delete.

## Before Changing

- Make assumptions explicit when they affect behavior, scope, or verification. Check the current implementation and relevant project instructions before choosing a solution.
- Define observable success criteria. Distinguish current behavior from design intent and past verification records.
- Choose the smallest change that satisfies the request. Keep C# naming, braces, indentation, and expression conventions consistent with the current files; do not introduce a new C# style or reformat unrelated code.

## Writing

- Implement the simplest thing that satisfies the requirement. No speculative generality, no "might need it later" parameters, hooks, or layers.
- Prefer explicit, boring code over clever code. If a reviewer must pause to decode a line, rewrite the line.
- Keep functions small and single-purpose. Minimize hidden state and side effects; pass data in, return data out.
- Duplication is cheaper than the wrong abstraction. Tolerate two similar copies; abstract only when a third appears and the shape is stable.
- Fail loudly. Surface errors at the boundary where they occur; never swallow exceptions or add silent fallbacks that mask defects.
- Remove dead code, unused flags, and commented-out blocks only when they are part of the requested change; preserve unrelated work.

## Changing

- Keep diffs small and independently verifiable. One behavior per change; unrelated cleanup goes in its own change.
- Match the surrounding code: naming, idioms, comment density, and the repository's Korean/English comment rules.
- Comments explain **why** — a decision, a trade-off, a trap — never restate what the code does.
- Use `//` for ordinary C#/JavaScript/TypeScript comments and finish with a short noun phrase. Use syntax-required JSX/CSS/YAML/shell comments, XML documentation, and tool directives as specified by the root `AGENTS.md`; do not rewrite existing comments in bulk.
- When touching existing code, leave it slightly simpler than found, but never bundle a rewrite into a feature change.

## Reviewing

- Judge by observable behavior and failure modes, not style preference.
- Ask of every abstraction: what concrete duplication does it remove today?
- Ask of every branch: what input reaches it, and what test proves it?
- Flag anything that cannot be verified by a targeted test or a repeatable check.
