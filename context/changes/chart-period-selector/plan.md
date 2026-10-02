# Chart Period Selector Implementation Plan

## Overview

The two dashboard donut charts (spend and income) are hard-wired to the current calendar month. Add one shared period selector — "last 30 days", "last 90 days", "selected month", "custom range" — that drives both donuts, the click-to-filter transaction list, and the average-deviation badge. This replaces the dropped S-05 (budget-vs-actual) with better data exploration.

## Current State Analysis

- The chart endpoints take no parameters; the month is computed server-side from the injected clock in the Europe/Warsaw zone (`MyFinances/backend/CurrentMonthRange.cs`, `MyFinances/backend/Dashboard/DashboardEndpoints.cs:123-141`).
- `GET /api/transactions` accepts `skip`, `take`, `categoryId`, `currentMonth`, `kind` (`MyFinances/backend/Transactions/TransactionEndpoints.cs:24-84`). `currentMonth` calls `CurrentMonthRange.Get()` on the system clock, not the injected `TimeProvider`, so tests cannot fake "now" for the list. The sign + non-transfer drilldown rule applies only when `categoryId` and `currentMonth` are both set (lines 52-60).
- The average-deviation calculator (`MyFinances/backend/Dashboard/CategoryDeviation.cs:63-89`) assumes `today` lies inside the displayed month: it is used both for the day-of-month cutoff and for the month count. The history query is `Date < range.Start` for categories that have current-month spend (`DashboardEndpoints.cs:189-205`). Band is ±10% (`UpperFactor` 1.1 / `LowerFactor` 0.9).
- Frontend: `routes/home.tsx` renders two `CategorySpendDonut` instances (l.644-655) and owns click-filter state (`selectedCategoryId`, `selectedKind`, l.168-171). `CategorySpendDonut.tsx:81` fetches with no query string; titles and empty-state copy hard-code "this month". `categoryFilterQuery` (`home.tsx:90-95`) sends `currentMonth=true`.
- The frontend has no test framework; the backend has xUnit with `FixedTimeProvider` and `AuthApiFactory`.

## Desired End State

A period selector sits above the donuts on the dashboard. Defaults to the current month on every load (not persisted). Choosing any period refetches both donuts for that period, clears any slice selection, and the transaction list shown after clicking a slice covers exactly that period, so listed rows reconcile with the slice amount. The spend donut's deviation badge is shown for every period. Verified by backend xUnit tests and a manual run in the browser.

### Key Discoveries:

- The only frontend caller of `currentMonth` is `categoryFilterQuery` in `home.tsx`, so it can be replaced by `from`/`to` without a compatibility shim.
- `CurrentMonthRange.MonthOf` and `Today(clock)` already give Warsaw-aware month boundaries and inclusive `DateOnly` bounds; the new period helper builds on them.
- Existing tests seed boundaries with 2099 dates because the auth cookie expires 30 days after the fake "now" (`DashboardEndpointsTests.cs:224-232`); new tests must follow this.
- Month-rollover risk: dashboard endpoints read the clock once so `today` and the range cannot straddle a boundary (`DashboardEndpoints.cs:156-157`); the list endpoint must do the same.

## What We're NOT Doing

- No per-chart selectors, no persistence of the choice (no localStorage, no URL params).
- No future periods other than the current calendar month (see Implementation Approach).
- No frontend test framework.
- No change to which transactions count (categorized only, internal transfers excluded, sign by kind) and no change to the income donut's lack of a deviation signal.
- No per-user timezone; server keeps Warsaw.
- Not touching the trend line chart (`category-trend-line-chart`) or the roadmap.

## Implementation Approach

Period is expressed on the wire as inclusive `from` / `to` (`yyyy-MM-dd`). The frontend turns each preset into these two dates; the backend validates and consumes them. Omitting both parameters keeps today's behaviour (current month), so unfiltered calls keep working.

Validation (decided): `from <= to`; `from <= today`; `to <= today`, except that the exact current calendar month (`from` = 1st, `to` = last day of the current month) is allowed, so future-dated rows inside the current month are still counted as today. Only one of `from`/`to` given → 400. Violations → 400. "Last 30 days" = today-29 … today (30 days inclusive); "last 90 days" = today-89 … today.

Deviation generalisation (decided): a period that is exactly one full calendar month uses the monthly logic — the current month keeps today's pace-adjusted rule unchanged, a past month compares its full total against the average of the months before it (as-of day = month end, history = rows before the month start). Any other period (last 30/90 days, custom range) of length L days compares its total against the average total of preceding windows of length L, walking back from `from - 1` to the category's first spend, windows with zero spend counted, ±10% band; no signal when the category has no spend before `from`.

Order: backend contract first (phase 1), deviation logic second (phase 2), UI last (phase 3) so each is verified before the next depends on it.

## Phase 1: Period parameter on the chart and list endpoints

### Overview

Introduce a shared period type and make both donut endpoints and the transaction list accept `from`/`to`, validated identically and using the injected clock.

### Changes Required:

#### 1. Period helper

**File**: `MyFinances/backend/CurrentMonthRange.cs` (extend) or a new `MyFinances/backend/PeriodRange.cs`

**Intent**: One place that parses/validates `from`/`to` against the injected clock's Warsaw "today" and returns an inclusive range, or a validation error. Defaults to the current month when both are absent.

**Contract**: `PeriodRange.Resolve(DateOnly? from, DateOnly? to, DateOnly today)` → range `(Start, End)` or error (HTTP 400 problem). Rules as in Implementation Approach. Expose `IsFullCalendarMonth` so phase 2 can pick the deviation mode.

#### 2. Donut endpoints

**File**: `MyFinances/backend/Dashboard/DashboardEndpoints.cs`

**Intent**: `category-spend` and `category-income` read optional `from`/`to`, resolve the period once from the single clock reading, and use it for the amount query in place of the hard-wired current month.

**Contract**: `GET /api/dashboard/category-spend|category-income?from=yyyy-MM-dd&to=yyyy-MM-dd`; response DTOs unchanged. Existing exclusion rules unchanged. The deviation computation still uses its current-month behaviour in this phase (phase 2 changes it).

#### 3. Transaction list

**File**: `MyFinances/backend/Transactions/TransactionEndpoints.cs`

**Intent**: Replace the `currentMonth` flag with `from`/`to`, inject `TimeProvider` so the list shares the dashboard's clock, and keep the sign + non-transfer drilldown rule when `categoryId` is combined with a period.

**Contract**: `GET /api/transactions?...&from=&to=` — `from`/`to` filter `Date` inclusively; drilldown rule applies when `categoryId` and a period (both dates) are set; `currentMonth` is removed. A period alone or `categoryId` alone does not apply the sign/transfer rule (existing behaviour, existing tests are kept and updated for the renamed parameter).

#### 4. Tests

**File**: `MyFinances/backend/Tests/DashboardEndpointsTests.cs`, `MyFinances/backend/Tests/TransactionEndpointsTests.cs`, `MyFinances/backend/Tests/CurrentMonthRangeTests.cs` (or a new `PeriodRangeTests.cs`)

**Intent**: Cover period filtering on both donuts and the list, the validation rules, and month/year boundaries with `FixedTimeProvider`.

**Contract**: Tests for — rows inside/outside `from`/`to` (inclusive both ends); both params absent equals current month; 400 for only one param, `from > to`, `from` in the future, `to` in the future for a non-current-month range; current calendar month accepted with `to` = month end (future-dated row still counted); list drilldown matches the slice for the same period; year-boundary range (Dec→Jan); per-user isolation retained.

### Success Criteria:

#### Automated Verification:

- Backend builds and all tests pass: `dotnet test` (from `MyFinances/backend`)
- New period/validation tests present and passing in the dashboard, transaction and period-helper test classes
- Frontend still typechecks (it does not use the changed endpoints yet): `npm run typecheck` (from `MyFinances/frontend`)

#### Manual Verification:

- Swagger (`/swagger`) shows `from`/`to` on both dashboard endpoints and the transaction list
- Calling the endpoints without parameters returns the same data as before for the current month

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Deviation signal for any period

### Overview

Generalise the average-deviation computation so the spend donut's badge is meaningful for the current month, past months, rolling windows and custom ranges.

### Changes Required:

#### 1. Deviation calculator

**File**: `MyFinances/backend/Dashboard/CategoryDeviation.cs`

**Intent**: Support two modes next to the existing one: a full past calendar month, and an arbitrary window of L days. The current-month path must keep its exact current results.

**Contract**: Month mode — as-of day = `min(today, month end)`, history = rows before the month start, month count between the first spend month and the displayed month; for the current month this is identical to today's logic. Window mode — `n = ceil((from - firstSpend) / L)` windows of L days back from `from - 1`, `average = (all spend before from) / n`, compare the period total against `average` with the existing ±10% inclusive band; `null` signal when there is no spend before `from`. The `AverageToDate` field carries the baseline value for whichever mode applies.

#### 2. Endpoint wiring

**File**: `MyFinances/backend/Dashboard/DashboardEndpoints.cs`

**Intent**: Choose the mode from the resolved period (`IsFullCalendarMonth` → month mode, else window mode), load history relative to the period start instead of the current month start, and cover categories that have spend in the selected period.

**Contract**: History query becomes `Date < period.Start` with the same exclusions; no new response fields.

#### 3. Tests

**File**: `MyFinances/backend/Tests/CategoryDeviationTests.cs`, `MyFinances/backend/Tests/DashboardEndpointsTests.cs`

**Intent**: Pin both new modes and prove the current-month results are unchanged.

**Contract**: Unit tests — past month vs the average of earlier months (including zero-spend months and year boundary); window mode with exact multiples and partial first window; boundary of the ±10% band; no signal without prior spend. Endpoint tests — past month, last-30-days and custom range return the expected `deviation`; existing current-month signal tests untouched and passing.

### Success Criteria:

#### Automated Verification:

- All backend tests pass: `dotnet test` (from `MyFinances/backend`)
- Existing current-month signal tests in `CategoryDeviationTests` and `DashboardEndpointsTests` pass unchanged
- New past-month and window-mode tests pass

#### Manual Verification:

- Against real data, a past month with known spend shows a plausible badge versus the average of earlier months
- A 30-day window with a clearly higher spend than earlier 30-day windows reads "above"

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Period selector in the dashboard UI

### Overview

Add the shared selector, feed the period to both donuts and the click-to-filter list, and replace the hard-coded "this month" copy.

### Changes Required:

#### 1. Period model

**File**: `MyFinances/frontend/app/lib/period.ts` (new)

**Intent**: Single place that turns a selector state into `{from, to, label, isCurrentMonth}` using local `yyyy-MM-dd` dates (same approach as `todayDateInputValue` in `home.tsx:83-89`).

**Contract**: Presets `month` (default, current month), `last30` (today-29…today), `last90` (today-89…today), `custom` (user dates). For the current month `to` is the last day of the month (the one allowed future end); a selected past month uses its own first/last day. Pure functions, no React.

#### 2. Selector UI and state

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Render the selector above the donut grid (preset buttons/select, a `type="month"` input for "selected month" capped at the current month, two `type="date"` inputs for custom capped at today with start ≤ end). Hold the period in local state defaulting to the current month. Clear `selectedCategoryId`/`selectedKind` when the period changes.

**Contract**: `categoryFilterQuery` sends `from`/`to` instead of `currentMonth=true`; the filter banner and empty-state text show the period label; the filter effect (l.176-214) and "load more"/refresh paths use the new query. Reuse the existing input/select styling from the transaction form.

#### 3. Donut component

**File**: `MyFinances/frontend/app/components/CategorySpendDonut.tsx`

**Intent**: Accept the period, include it in the request and in the effect dependencies, and derive titles and empty-state copy from the period label instead of "this month".

**Contract**: New prop for the period (from, to, label, isCurrentMonth); fetch `…?from=&to=`; the deviation badge detail reads "avg by this day" only for the current month and a generic "avg for comparable period" otherwise.

### Success Criteria:

#### Automated Verification:

- Frontend typechecks: `npm run typecheck` (from `MyFinances/frontend`)
- Production build succeeds: `npm run build` (from `MyFinances/frontend`)
- Backend tests still pass: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Default load shows the current month, matching the previous behaviour
- "Last 30 days", "Last 90 days", a past month and a custom range each update both donuts and their titles
- Clicking a slice lists exactly the rows of that category in the chosen period and their sum matches the slice; clicking again clears
- Changing the period clears an active slice selection
- Future months and future custom end dates cannot be chosen; start after end is blocked
- Deviation badge appears for periods with enough history and is hidden otherwise
- No console errors; layout fine on a narrow viewport

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- Period validation (`PeriodRange`) including the current-month exception and year boundary
- `CategoryDeviation` month mode (current unchanged, past month) and window mode, ±10% band edges

### Integration Tests:

- `DashboardEndpointsTests` and `TransactionEndpointsTests` with `FixedTimeProvider` (2099 dates, see Key Discoveries) for period filtering, 400 cases and drilldown parity

### Manual Testing Steps:

1. Open the dashboard with imported data; confirm default is the current month and numbers match the previous release.
2. Cycle through all presets and a custom range; compare slice amounts with the list after clicking a slice.
3. Try invalid inputs (future month, start > end) and confirm they are blocked in the UI and rejected by the API.
4. Check the deviation badge in a past month and a 30-day window.

## Performance Considerations

Queries stay single-user, indexed-by-user aggregations; a custom range up to the full history is one grouped query plus the existing history query. No caching needed at this scale.

## Migration Notes

None. No schema change; `currentMonth` on `/api/transactions` is removed and its only caller is updated in the same change set (phase 1 breaks the frontend list filter until phase 3, so phases 1 and 3 should land in the same branch/PR).

## References

- Archived donut plan: `context/archive/2026-09-28-category-spend-donut-chart/plan-brief.md`
- Archived deviation plan: `context/archive/2026-09-30-category-average-deviation-signal/plan-brief.md`
- Roadmap: parked FR-016 (arbitrary date-range filter) and the S-05 drop note in `context/foundation/roadmap.md`
- Related change: `context/changes/category-trend-line-chart/`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Period parameter on the chart and list endpoints

#### Automated

- [x] 1.1 Backend builds and all tests pass: `dotnet test` (from `MyFinances/backend`) — 9fe16a9
- [x] 1.2 New period/validation tests present and passing in the dashboard, transaction and period-helper test classes — 9fe16a9
- [x] 1.3 Frontend still typechecks (it does not use the changed endpoints yet): `npm run typecheck` (from `MyFinances/frontend`) — 9fe16a9

#### Manual

- [ ] 1.4 Swagger (`/swagger`) shows `from`/`to` on both dashboard endpoints and the transaction list
- [ ] 1.5 Calling the endpoints without parameters returns the same data as before for the current month

### Phase 2: Deviation signal for any period

#### Automated

- [x] 2.1 All backend tests pass: `dotnet test` (from `MyFinances/backend`) — 5724442
- [x] 2.2 Existing current-month signal tests in `CategoryDeviationTests` and `DashboardEndpointsTests` pass unchanged — 5724442
- [x] 2.3 New past-month and window-mode tests pass — 5724442

#### Manual

- [ ] 2.4 Against real data, a past month with known spend shows a plausible badge versus the average of earlier months
- [ ] 2.5 A 30-day window with a clearly higher spend than earlier 30-day windows reads "above"

### Phase 3: Period selector in the dashboard UI

#### Automated

- [x] 3.1 Frontend typechecks: `npm run typecheck` (from `MyFinances/frontend`)
- [x] 3.2 Production build succeeds: `npm run build` (from `MyFinances/frontend`)
- [x] 3.3 Backend tests still pass: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 3.4 Default load shows the current month, matching the previous behaviour
- [ ] 3.5 "Last 30 days", "Last 90 days", a past month and a custom range each update both donuts and their titles
- [ ] 3.6 Clicking a slice lists exactly the rows of that category in the chosen period and their sum matches the slice; clicking again clears
- [ ] 3.7 Changing the period clears an active slice selection
- [ ] 3.8 Future months and future custom end dates cannot be chosen; start after end is blocked
- [ ] 3.9 Deviation badge appears for periods with enough history and is hidden otherwise
- [ ] 3.10 No console errors; layout fine on a narrow viewport
