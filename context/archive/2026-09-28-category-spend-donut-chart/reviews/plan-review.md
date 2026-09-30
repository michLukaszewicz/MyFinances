<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Category Spend Donut Chart (S-04)

- **Plan**: context/changes/category-spend-donut-chart/plan.md
- **Mode**: Deep
- **Date**: 2026-09-28
- **Verdict**: REVISE → all findings fixed in-plan during triage; plan is now SOUND
- **Findings**: 1 critical, 2 warnings, 1 observation (all FIXED)

## Verdicts (at time of review, before fixes)

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | FAIL |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | PASS |

## Grounding

Grounding: 6/6 paths ✓, 4/4 symbols ✓, brief↔plan ✓ (`docs/reference/contract-surfaces.md` absent — surface-touch check skipped)

## Findings

### F1 — Click-to-filter silently breaks on Load More / after add-edit-delete

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Phase 2, Change #3 (home.tsx wiring)
- **Detail**: `handleLoadMore` (home.tsx:252-266) and `refreshTransactions` (home.tsx:268-277) build their own `/transactions?skip=...&take=...` query strings from local closure state only, with no channel for `selectedCategoryId`/`currentMonth`. `refreshTransactions()` runs on every write path (submitTransaction, handleDelete). As planned, clicking "Load more" while filtered would mix unfiltered page-2+ data under a filtered page-1; any add/edit/delete while filtered would silently revert the list to unfiltered — breaking the plan's own core promised interaction.
- **Fix**: Thread `selectedCategoryId`/`currentMonth` into `handleLoadMore` and `refreshTransactions` themselves, not just a separate effect for the initial filtered fetch.
- **Decision**: FIXED — Phase 2 Change #3 Contract now has an explicit "Critical" paragraph requiring both functions to append the filter params when `selectedCategoryId` is set.

### F2 — DB-side GroupBy+Sum is an unverified pattern this codebase already flagged as risky

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 1, Change #2 (DashboardEndpoints.cs)
- **Detail**: The plan specified a DB-side `GroupBy`+`Sum`+join. The codebase's only prior `GroupBy` (`Import/ImportEndpoints.cs:69`) carries a comment explicitly avoiding DB-side `GroupBy` because it "doesn't reliably translate," materializing to memory first instead. No test in the repo exercises a DB-translated `GroupBy` against InMemory or Npgsql.
- **Fix A ⭐ (applied)**: Materialize the filtered transactions to a list first (`Include(Category)` + `ToListAsync()`), then `GroupBy`/`Sum` in LINQ-to-Objects — mirrors the established `ImportEndpoints.cs` precedent, zero translation risk, negligible cost at one user's one-month volume.
- **Fix B**: Keep DB-side GroupBy+Sum, verify via an early test, fall back to Fix A if it fails to translate.
- **Decision**: FIXED via Fix A — Phase 1 Change #2 Contract now has an explicit "Critical" paragraph requiring the in-memory grouping pattern.

### F3 — Dashboard endpoint adds a third O(n²) full-table transfer-detection scan, but Performance Considerations said "None"

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Plan's "Performance Considerations" section
- **Detail**: `TransferDetectionService.DetectAsync` (`Categorization/TransferDetectionService.cs:17-58`) loads every not-manually-flagged transaction and runs an O(n²) nested loop on every call. The new dashboard endpoint calls it a third time (alongside queue/handled), on what's likely the most-visited page — the plan's Performance Considerations section didn't mention this at all.
- **Fix**: Name the cost explicitly in Performance Considerations and note it's accepted as fine for MVP transaction volumes.
- **Decision**: FIXED — Performance Considerations section rewritten to name the O(n²) cost and the third-hot-path concern explicitly.

### F4 — Color palette size/wrap behavior unspecified

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2, Change #2 (CategorySpendDonut.tsx)
- **Detail**: The plan said colors come from "a small fixed palette array indexed by position" without specifying it must cover all 12 seeded categories or defining wrap behavior if shorter — risking duplicate slice colors.
- **Fix**: Note in the Contract that the palette should have at least 12 entries, cycling via `index % palette.length` as a safety margin.
- **Decision**: FIXED — Contract now specifies a minimum 12-entry palette with explicit modulo indexing.
