# Category Average-Deviation Signal Implementation Plan

## Overview

Add the FR-013 signal to the dashboard: for each category in the current month's spend donut, show whether spend so far is **above**, **below**, or **in line with** that category's historical average — once the category has at least 1 prior month of history. Before that, the category shows its amount with no signal. The backend computes the signal (pace-adjusted, ±10% band); the frontend adds a visible per-category list with a badge beside the spend donut.

## Current State Analysis

S-04 is live. `GET /api/dashboard/category-spend` returns current-month spend per category as `CategorySpendDto(CategoryId, CategoryName, Amount)`, with no parameters and no history awareness. The frontend donut shows amounts only in a hover tooltip; there is no visible per-category list and no percentages. Nothing in the backend aggregates past months, and `CurrentMonthRange` only computes the current month.

### Key Discoveries:

- Spend endpoint + shared query helper: `MyFinances/backend/Dashboard/DashboardEndpoints.cs:18-72`. Filter is `UserId`, `CategoryId != null`, `!IsInternalTransfer`, `Amount < 0` (spend), grouped in memory (DB-side GroupBy doesn't translate reliably).
- DTO: `MyFinances/backend/Dashboard/DashboardContracts.cs:4`. The income endpoint reuses it, so it must not gain spend-only fields.
- Month boundary logic (Europe/Warsaw, UTC fallback): `MyFinances/backend/CurrentMonthRange.cs:11-18`, takes an optional `TimeProvider`. `TimeProvider` is **not registered in DI** (no match in `Program.cs`), so tests currently can't control "today" at the endpoint level.
- `Transaction.Date` is a `DateOnly`; spend is negative `Amount`; categories are a fixed, user-independent seed of 14.
- Frontend donut: `MyFinances/frontend/app/components/CategorySpendDonut.tsx` — Recharts `Pie` + `Legend`, palette indexed by array position (`:38-51`), click-to-filter via `onSelectCategory` (`:116-124`); used twice (spend + income) in `app/routes/home.tsx:644-655`.
- Backend tests: xUnit + `AuthApiFactory` (in-memory EF), seeding via `AppDbContext`; pattern in `MyFinances/backend/Tests/DashboardEndpointsTests.cs`. No frontend test framework.

## Desired End State

Opening the dashboard, the spend donut has a list below it with one row per category (name, colour dot, this month's amount). Rows for categories with at least 1 prior month of history carry a badge: "Above average", "Below average" or "In line", plus the pace-adjusted average it was compared against. Rows for categories without prior-month history show the amount only. Clicking a row filters the transaction list exactly like clicking its slice. The income donut is unchanged.

Verify via `dotnet test` (backend logic) and manual dashboard checks with data spread across months.

## What We're NOT Doing

- No budgets or budget-vs-actual (S-05) — the list leaves room for it but adds nothing.
- No signal on the income donut / income endpoint.
- No week-level signal (FR-013 mentions week/month; decision: month only) and no month picker / date ranges (FR-016).
- No statistical (std-deviation) thresholds; no configurable band.
- No frontend test framework (decision: backend tests + typecheck + manual UI checks).
- No new per-user timezone handling; Europe/Warsaw stays the single month/day boundary.

## Implementation Approach

Keep all classification in the backend; the frontend only renders `deviation` and `averageToDate`.

Signal definition (settled during planning):

- **Pace-adjusted comparison.** "Current spend" = the category's spend dated from the 1st through *today* (Warsaw) of the current month. "Average" = the category's spend dated on day-of-month ≤ today's day-of-month in each prior calendar month, averaged over prior months. A past transaction on day 31 is simply outside a day-10 window; no clamping needed.
- **Average window = all calendar months**, from the month of the category's first-ever spend transaction up to last month, **zero months counted**. N = number of those months. If N = 0 (first spend is in the current month) → no signal.
- **Band ±10%, boundaries inclusive for "in line".** Above if current > 1.1 × average; below if current < 0.9 × average; otherwise in line. Average 0 needs no special case: current 0 → in line, current > 0 → above.
- **Known consequence (accepted):** a category whose only prior history is a partial month (first spend late in last month, or a mid-month import) has an average-to-date of 0 and so reads "above" for any spend this month. This follows the agreed rules and is intentionally not suppressed; it is pinned by a test and a manual check.
- Only spend rows (`Amount < 0`, categorized, non-transfer, same user) feed both sides, matching what the donut sums.
- The displayed `Amount` stays the full current-month total (unchanged chart); only the comparison uses the to-date figure. A future-dated current-month transaction therefore counts in the amount but not in the comparison.

## Phase 1: Backend deviation signal

### Overview

Compute the signal and expose it on the spend endpoint, fully covered by xUnit tests. No UI change; income endpoint untouched.

### Changes Required:

#### 1. Expose "today" and make the clock injectable

**File**: `MyFinances/backend/CurrentMonthRange.cs`, `MyFinances/backend/Program.cs`

**Intent**: Endpoint code needs today's Warsaw date (not only the month bounds), and tests need to fix the clock. Add a way to get today's date using the same time-zone resolution, and register `TimeProvider.System` in DI so endpoints can take a `TimeProvider`.

**Contract**: `CurrentMonthRange.Today(TimeProvider? clock = null) -> DateOnly` sharing `ResolveTimeZone()`; `Get()` behaviour unchanged. `builder.Services.AddSingleton(TimeProvider.System)`.

#### 2. Deviation calculation

**File**: `MyFinances/backend/Dashboard/CategoryDeviation.cs` (new)

**Intent**: A pure, unit-testable static calculator holding the rules from "Implementation Approach" — no DB access.

**Contract**: Input: the category's prior-month spend rows (date, positive magnitude), current-month spend-to-date total, and `today`. Output: `(decimal AverageToDate, string Deviation)?` — `null` when there is no prior-month history (N = 0). Wire values of `Deviation`: `"above"`, `"below"`, `"inLine"`.

#### 3. Spend endpoint and DTO

**File**: `MyFinances/backend/Dashboard/DashboardEndpoints.cs`, `MyFinances/backend/Dashboard/DashboardContracts.cs`

**Intent**: The spend endpoint additionally loads the user's earlier spend rows (before the current month start, same exclusions) and attaches the signal per category. The income endpoint keeps `CategorySpendDto`.

**Contract**: New `CategorySpendSignalDto(Guid CategoryId, string CategoryName, decimal Amount, decimal? AverageToDate, string? Deviation)` returned by `GET /api/dashboard/category-spend`; `Amount` semantics unchanged. `AverageToDate`/`Deviation` are both null or both set. The endpoint takes `TimeProvider` for today/month range. Categories appear only if they have current-month spend (as today), so history alone never adds a row. Reuse the existing in-memory grouping approach.

#### 4. Tests

**File**: `MyFinances/backend/Tests/CategoryDeviationTests.cs` (new), `MyFinances/backend/Tests/DashboardEndpointsTests.cs`

**Intent**: Unit tests for the calculator plus endpoint tests with a fixed clock (override `TimeProvider` through the test factory).

**Contract**: `FixedTimeProvider` is currently `private` and nested in `CurrentMonthRangeTests` (`Tests/CurrentMonthRangeTests.cs:8`); move it to a shared internal test helper so the new tests can use it, and override `TimeProvider` per test via `factory.WithWebHostBuilder(b => b.ConfigureServices(...))` (no existing test uses this yet). Cover at least — pace-adjusted (day-10 example: spend 600 vs prior months whose spend by day 10 averaged lower → above); band boundaries (exactly 0.9× and 1.1× → in line, just outside → below/above); zero-spend months counted in the average (Jan 400, Feb 0, Mar 200 → average 200 for N = 3); N = 0 → null signal; average 0 with current > 0 → above, including the partial-first-month case (first spend on the 25th of last month, spend this month by day 10 → above, N = 1); transactions after today's day-of-month in past months excluded; internal-transfer and uncategorized rows excluded from history; per-user isolation of history; month rollover (history includes the immediately preceding month); income endpoint response unchanged (no signal fields).

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- All backend tests pass, including the new ones: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Via Swagger with the test account and transactions in prior months, `GET /api/dashboard/category-spend` returns `averageToDate` and `deviation` for a category with history and `null` for both on a category whose first spend is this month
- `GET /api/dashboard/category-income` output shape is unchanged

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Frontend category list with badge

### Overview

Render a visible per-category list under the spend donut showing the signal; income donut stays as is.

### Changes Required:

#### 1. Extend the chart data type and render the list

**File**: `MyFinances/frontend/app/components/CategorySpendDonut.tsx`

**Intent**: Extend the local `CategorySpendDto` with optional `averageToDate` and `deviation`, and — for `kind === "spend"` only — render a list below the chart while keeping the existing `Legend`. ("Below", not literally beside: the spend and income donuts share a two-column row in `home.tsx`, so a side-by-side list would not fit.) Each row shows the slice's colour dot (same palette index as the slice), category name, this month's amount (existing `formatAmount`), and a badge when `deviation` is present; with the badge, also show the compared average ("avg by this day: …"). No badge and no average text when `deviation` is null.

**Contract**: Badge copy/colour: `above` → "Above average" (warm/red tone), `below` → "Below average" (green tone), `inLine` → "In line" (neutral gray); use existing Tailwind dark-theme classes. Rows are clickable and call the same toggle as `handleSliceClick`, including the dimmed state for non-selected rows when a selection exists. Loading, error and empty states unchanged.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck` (from `MyFinances/frontend`)
- Production build succeeds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- Spend donut shows the list with correct colour dots, names and amounts; rows with prior-month history show the right badge and average, rows without history show no badge
- Clicking a list row filters the transaction list like clicking its slice, and clicking it again clears the filter
- Income donut renders and behaves exactly as before
- Adding/editing/deleting a transaction refreshes the list and badges

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- `CategoryDeviation` calculator: pace-adjustment, ±10% boundaries (inclusive in-line), zero months counted, N = 0, average 0.

### Integration Tests:

- Spend endpoint with a fixed `TimeProvider`: signal fields present/null per history, exclusions (uncategorized, transfers, other users), month rollover; income endpoint unchanged.

### Manual Testing Steps:

1. With the local test account, add manual transactions in a category across 2–3 previous months on varied days, plus current-month ones; confirm badge and "avg by this day" match a hand calculation.
2. Add a category's first-ever spend this month; confirm no badge. Then add one late-in-last-month transaction to another new category and small spend this month by an early day; confirm it reads "Above average" (accepted behaviour, see Implementation Approach).
3. Click rows and slices; confirm filter parity and clearing.

## Performance Considerations

The spend endpoint now reads all of a user's earlier categorized spend rows (single-user MVP, at most a few thousand rows) and aggregates in memory. Acceptable; no indexing or caching work planned.

## References

- Roadmap item: `context/foundation/roadmap.md` (S-06); PRD: FR-013 in `context/foundation/prd.md`
- Prior slice: `context/archive/2026-09-28-category-spend-donut-chart/plan-brief.md`
- Similar implementation: `MyFinances/backend/Dashboard/DashboardEndpoints.cs:34-72`, `MyFinances/backend/Tests/DashboardEndpointsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend deviation signal

#### Automated

- [x] 1.1 Backend builds: `dotnet build` (from `MyFinances/backend`) — 2e185be
- [x] 1.2 All backend tests pass, including the new ones: `dotnet test` (from `MyFinances/backend`) — 2e185be

#### Manual

- [x] 1.3 Via Swagger with the test account and transactions in prior months, `GET /api/dashboard/category-spend` returns `averageToDate` and `deviation` for a category with history and `null` for both on a category whose first spend is this month — 2e185be
- [x] 1.4 `GET /api/dashboard/category-income` output shape is unchanged — 2e185be

### Phase 2: Frontend category list with badge

#### Automated

- [x] 2.1 Type checking passes: `npm run typecheck` (from `MyFinances/frontend`) — c8e3828
- [x] 2.2 Production build succeeds: `npm run build` (from `MyFinances/frontend`) — c8e3828

#### Manual

- [x] 2.3 Spend donut shows the list with correct colour dots, names and amounts; rows with prior-month history show the right badge and average, rows without history show no badge — c8e3828
- [x] 2.4 Clicking a list row filters the transaction list like clicking its slice, and clicking it again clears the filter — c8e3828
- [x] 2.5 Income donut renders and behaves exactly as before — c8e3828
- [x] 2.6 Adding/editing/deleting a transaction refreshes the list and badges — c8e3828
