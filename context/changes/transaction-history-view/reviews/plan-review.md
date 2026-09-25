<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Transaction History View Implementation Plan

- **Plan**: context/changes/transaction-history-view/plan.md
- **Mode**: Deep
- **Date**: 2026-09-25
- **Verdict**: REVISE (pre-triage) → SOUND (all findings resolved)
- **Findings**: 0 critical, 2 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

Grounding: 10/10 paths ✓, 4/4 symbols ✓, brief↔plan ✓

## Findings

### F1 — Test helper reuse left ambiguous, and actually not directly reusable

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1, item 4 — Integration tests
- **Detail**: `CreateAuthenticatedClientAsync`/`GetAntiforgeryTokenAsync` are `private static` inside `ImportEndpointsTests.cs:24,32` — a new test file cannot call them directly, and no shared fixture file exists. The plan's "reuse (or extract if simpler)" phrasing left this unresolved.
- **Fix**: Extract `CreateAuthenticatedClientAsync` into a shared `Tests/TestClientHelpers.cs` used by both `ImportEndpointsTests` and the new `TransactionEndpointsTests`.
- **Decision**: FIXED (Fix in plan) — plan.md Phase 1 now has an explicit item 4 "Shared test client helper" before the renumbered item 5 "Integration tests".

### F2 — Offset pagination can drift under concurrent inserts

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 item 2 / Phase 2 item 2 — pagination design
- **Detail**: `skip=${items.length}` can drift if a newer-dated transaction is inserted between two "Load more" clicks, causing an already-seen row to reappear on the next page.
- **Fix A ⭐ Recommended**: Accept as a known MVP limitation
  - Strength: Zero extra code; matches this solo, single-user project's scope.
  - Tradeoff: A real (if rare) duplicate-row glitch remains unfixed across concurrent tabs.
  - Confidence: HIGH — narrow edge case for this product.
  - Blind spot: Real-world frequency unverified.
- **Fix B**: Switch to keyset/cursor pagination (`Date`, `Id`)
  - Strength: Immune to insertion drift.
  - Tradeoff: New query/DTO shape, more complexity, no existing precedent.
  - Confidence: MEDIUM — EF Core LINQ translation of composite-key keyset unverified.
  - Blind spot: N/A.
- **Decision**: FIXED (Fix A) — plan.md now has an "Open Risks & Assumptions" section documenting this as an accepted limitation.

### F3 — Redundant "skip" state variable never actually used

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Lean Execution
- **Location**: Phase 2, item 2 — Load-more state
- **Detail**: Plan listed `skip` as tracked state but then computed the next request from `items.length` directly.
- **Fix**: Drop `skip` from the state list; derive it from `items.length` at the call site.
- **Decision**: FIXED (Fix in plan) — plan.md's state list now reads `items`, `hasMore`, `loadingMore`, `loadMoreError`, with a note that `skip` is derived from `items.length`.
