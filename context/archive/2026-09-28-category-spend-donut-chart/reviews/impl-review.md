<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Category Spend Donut Chart (S-04)

- **Plan**: context/changes/category-spend-donut-chart/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2
- **Date**: 2026-09-29
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated re-run: dotnet test 92/92 pass; npm run typecheck and npm run build pass. All manual items confirmed by user.
Known benign deviations: recharts ^3.10.1; onSelectCategory(id, name); hasTransactions includes active filter; container max-w-2xl; extra loading/empty messages.

## Findings

### F1 — Load-more / refresh responses can race with a filter change

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/routes/home.tsx:303-330 vs 175-205
- **Detail**: The filter effect cancels only its own fetch. handleLoadMore and refreshTransactions have no request token; a slow old-filter response landing after a slice switch appends/overwrites rows from the wrong category.
- **Fix**: Keep a requestSeq ref bumped on every filter change / new page 1; drop stale responses in all three paths.
  - Strength: Fixes all paths uniformly.
  - Tradeoff: A little more state in an already large component.
  - Confidence: HIGH — standard pattern.
  - Blind spot: Not reproduced at runtime.
- **Decision**: FIXED — listGeneration ref drops stale load-more/refresh responses (home.tsx)

### F2 — Chart never refreshes after writes; selected slice can vanish

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/CategorySpendDonut.tsx:37-58, home.tsx:397,421
- **Detail**: Donut fetches once on mount. After add/edit/delete totals go stale; if the selected category's last transaction moves away, all slices dim (0.35) and only the Clear button escapes. Plan specified fetch-on-mount only, so this is a plan gap.
- **Fix**: Add a refreshKey prop bumped by the parent after successful writes; after each reload, if selectedCategoryId is absent from data, call onSelectCategory(null, null).
  - Strength: Chart and list stay consistent.
  - Tradeoff: Extra request per write.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED — refreshKey prop bumped by refreshTransactions; stale selection auto-cleared (CategorySpendDonut.tsx, home.tsx)

### F3 — Drill-down list does not use the chart's inclusion predicates

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Transactions/TransactionEndpoints.cs:40-49 vs Dashboard/DashboardEndpoints.cs:32-38
- **Detail**: Chart counts only Amount<0, non-transfer, categorized rows; the list filter (categoryId+currentMonth, as the plan specified) also shows refunds/income and transfers in that category, so slice total and list can disagree.
- **Fix A**: Accept and label in UI ("all transactions in category this month").
  - Strength: No backend change; matches plan.
  - Tradeoff: Numbers visibly differ.
  - Confidence: MED.
  - Blind spot: Users may distrust totals.
- **Fix B ⭐ Recommended**: When both filters set, also apply Amount<0 && !IsInternalTransfer in the list.
  - Strength: List sums to slice total.
  - Tradeoff: Couples list endpoint to chart semantics; hides refunds in filtered view.
  - Confidence: MED.
  - Blind spot: Whether user wants refunds visible.
- **Decision**: FIXED via Fix B — filtered list applies Amount<0 && !IsInternalTransfer when categoryId+currentMonth set; test added (93/93 pass)

### F4 — Failed filtered fetch leaves previous category's rows under new filter

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/routes/home.tsx:192-195
- **Detail**: On error selectedCategoryId is set but items are stale.
- **Fix**: Clear items on failure (or roll the filter back).
- **Decision**: FIXED — items/hasMore cleared on filtered-fetch failure (home.tsx)

### F5 — "Current month" uses UTC

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/CurrentMonthRange.cs:9
- **Detail**: Around midnight on the 1st in Poland the previous month is shown; documented as accepted in the plan. Direct DateTime.UtcNow prevents boundary tests.
- **Fix**: Accept; optionally inject TimeProvider later.
- **Decision**: FIXED — CurrentMonthRange now rolls over in Europe/Warsaw (UTC fallback) with injectable TimeProvider; CurrentMonthRangeTests added

### F6 — Dashboard endpoint loads tracked entities and detects transfers on every GET

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Dashboard/DashboardEndpoints.cs:28-46
- **Detail**: Accepted in plan (Performance Considerations). AsNoTracking would be a cheap win.
- **Fix**: Add AsNoTracking to the query.
- **Decision**: FIXED — AsNoTracking added; grouping switched to CategoryId (reference grouping broke without tracking)
