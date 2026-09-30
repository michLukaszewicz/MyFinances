<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Category Average-Deviation Signal

- **Plan**: context/changes/category-average-deviation-signal/plan.md
- **Scope**: Full plan (Phases 1–2 of 2)
- **Reviewed phases**: 1, 2
- **Date**: 2026-09-30
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated criteria re-run during review: `dotnet build` 0 errors; `dotnet test` 119 passed / 0 failed; `npm run typecheck` exit 0; `npm run build` exit 0. Manual rows 1.3, 1.4, 2.3–2.6 were confirmed by the user and each has a matching diff artefact.

## Findings

### F1 — Non-selected list rows dim text below readable contrast

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/CategorySpendDonut.tsx (list row `<button>`, `style={{ opacity: entry.fillOpacity }}`)
- **Detail**: The whole row, including name, amount, badge and average text, is dimmed to 0.35 when another category is selected. That mirrors the slice dimming but makes text hard to read, and the rows are the only keyboard path into the filter.
- **Fix**: Dim only the colour dot (or use ~0.6 for the row) so text stays readable while the selection is still obvious.
- **Decision**: FIXED (Fix now — opacity moved from the whole row to the colour dot only)

### F2 — History query loads every prior spend row, even when unused

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Dashboard/DashboardEndpoints.cs (history query, spend branch)
- **Detail**: The plan accepted loading all earlier spend rows (MVP scale), but the query also runs when there is no current-month spend and covers categories that have none.
- **Fix**: Skip the query when there are no current-month groups and restrict it to those category ids.
- **Decision**: FIXED (Fix now — history query skipped when there are no current-month groups and restricted to those category ids)

### F3 — `today` and the month range read the clock separately

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Dashboard/DashboardEndpoints.cs (`CurrentMonthRange.Get(clock)` and `CurrentMonthRange.Today(clock)`)
- **Detail**: Two `GetUtcNow()` calls per request. A request straddling Warsaw midnight at a month boundary could mix months. Negligible probability.
- **Fix**: Compute `today` once and derive the month range from it (e.g. an overload taking a `DateOnly`).
- **Decision**: FIXED (Fix now — `CurrentMonthRange.MonthOf(DateOnly)` added; endpoint reads the clock once via `Today()`)

### F4 — Test gaps: `Today()` untested and one history-exclusion assertion is vacuous

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: MyFinances/backend/Tests/DashboardEndpointsTests.cs (`CategorySpend_HistoryExcludesInternalTransfersAndUncategorizedRows`), Tests/CurrentMonthRangeTests.cs
- **Detail**: The uncategorized-row half of that test cannot fail (a null-category row never joins the Groceries history); the internal-transfer half is valid. `CurrentMonthRange.Today` has no direct test, e.g. 23:30 UTC on the 9th is the 10th in Warsaw, which drives the day-of-month window.
- **Fix**: Add a `Today` test for the Warsaw-vs-UTC day difference, and split/retarget the uncategorized case.
- **Decision**: FIXED (Fix now — `Today` Warsaw-vs-UTC test and `MonthOf` test added; vacuous uncategorized-row assertion dropped, test renamed to `CategorySpend_HistoryExcludesInternalTransfers`)
