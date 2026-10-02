<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Category Trend Line Chart

- **Plan**: context/changes/category-trend-line-chart/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2
- **Date**: 2026-10-02
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS (one minor drift: colours) |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (automated); manual items 1.4, 1.5, 2.4-2.10 pending |

Automated re-run: `dotnet test` 312 passed, `npm run typecheck` and `npm run build` OK.

## Findings

### F1 — Browser "today" vs Europe/Warsaw "today"

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/CategoryTrendChart.tsx:57,120,127 vs MyFinances/backend/Dashboard/CategoryTrendEndpoints.cs:54
- **Detail**: Default `to` is the browser local date; the server rejects `to` after the Warsaw date. A browser ahead of Warsaw gets 400 shown as the generic error.
- **Fix**: Surface the 400 `title` in the UI, or clamp `to` server-side.
- **Decision**: FIXED

### F2 — Stale error shown on retry

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/CategoryTrendChart.tsx:149-174
- **Detail**: `loadError` is not cleared when a new fetch starts, and rendering is gated on `!loadError`, so after one failure the error stays (no "Loading…") until a fetch succeeds.
- **Fix**: `setLoadError(null)` at the start of `load()`.
- **Decision**: FIXED

### F3 — Colours differ from the donut and may shift on load

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/components/CategoryTrendChart.tsx:33,176-179; CategorySpendDonut.tsx:117
- **Detail**: The plan said colours deterministic by category id order. The trend chart uses the index in the categories list (series index until it loads); the donut uses the position in its own response, so the "same palette keeps colour across both charts" comment is not true and colours can change once categories load.
- **Fix**: Colour by category-list index in both charts (or drop the claim), and wait for categories before rendering.
- **Decision**: FIXED

### F4 — Series order not deterministic for equal SortOrder

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Dashboard/CategoryTrendEndpoints.cs:88-89
- **Detail**: `OrderBy(SortOrder)` has no tie-breaker.
- **Fix**: Add `.ThenBy(name)`.
- **Decision**: FIXED

### F5 — Entities materialised instead of a projection

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Dashboard/CategoryTrendEndpoints.cs:72-81
- **Detail**: `Include(Category)` loads full rows; a `Select` projection would be lighter. Fine at single-user scale.
- **Fix**: Project to CategoryId, name, SortOrder, Date, Amount.
- **Decision**: SKIPPED (projection judged unnecessary at single-user scale)

### F6 — No test for missing/malformed from/to

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: MyFinances/backend/Tests/CategoryTrendEndpointsTests.cs
- **Detail**: Non-nullable `DateOnly from/to` should bind to 400 when absent, but no test pins it.
- **Fix**: Add an InlineData case without `from`/`to`.
- **Decision**: FIXED
