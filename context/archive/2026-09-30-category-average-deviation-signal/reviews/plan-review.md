<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Category Average-Deviation Signal

- **Plan**: context/changes/category-average-deviation-signal/plan.md
- **Mode**: Deep
- **Date**: 2026-09-30
- **Verdict**: SOUND (minor warnings; all findings triaged and fixed)
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding
6/6 paths ✓, symbols ✓ (TimeProvider not registered in DI, as the plan states), brief↔plan ✓, Progress↔Phase 10/10 rows ✓

## Findings

### F1 — First history month is usually partial, so the signal often reads "above"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Implementation Approach (average 0 → above)
- **Detail**: A category first used late in last month, with spend this month by an early day, has N = 1 but an average-to-date of 0, so it reads "Above average". Mid-month imports do the same.
- **Fix A ⭐ Recommended**: Keep the rule; pin it with a test and a manual check
- **Fix B**: Suppress the signal when the average-to-date is 0
- **Decision**: FIXED (Fix A — "Known consequence (accepted)" added to Implementation Approach, unit test case and manual step added; brief risk added)

### F2 — The "fixed clock" test helper isn't reusable as planned

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1, change #4
- **Detail**: `FixedTimeProvider` is private and nested in `CurrentMonthRangeTests`; no test uses `WithWebHostBuilder` yet.
- **Fix**: Extract it to a shared internal helper and override `TimeProvider` via `WithWebHostBuilder`.
- **Decision**: FIXED (plan Phase 1 change #4 updated)

### F3 — UI placement wording differs between decision and plan

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Desired End State / Phase 2 vs brief
- **Detail**: User chose "beside donut"; plan said below, brief said beside/below. The two donuts share a row.
- **Fix**: Say "below the donut" in both documents and note why.
- **Decision**: FIXED (plan Phase 2 and brief Key Decisions updated)
