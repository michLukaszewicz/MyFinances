# Category Trend Line Chart Implementation Plan

## Overview

Add a line chart to the dashboard that shows spend (or income) per category over time. The X axis is bucketed by week, month or year; the Y axis is the summed amount of a category in each bucket; the chart draws one line per selected category. Replaces the dropped S-05 (budget-vs-actual) with better data exploration.

## Current State Analysis

- No endpoint aggregates amounts over time. The only aggregations are the single-period donut sums (`GET /api/dashboard/category-spend|category-income`, `MyFinances/backend/Dashboard/DashboardEndpoints.cs`) and the paged transaction list (`MyFinances/backend/Transactions/TransactionEndpoints.cs`).
- Counting rules are fixed and shared with the donuts: `UserId == userId`, `CategoryId != null`, `!IsInternalTransfer`, `Amount < 0` for spend (shown as positive magnitude) and `Amount > 0` for income; internal-transfer detection runs first (`transferDetection.DetectAsync`, `DashboardEndpoints.cs:153`). `Transaction.Date` is a `DateOnly`; "today" is Europe/Warsaw via the injected `TimeProvider` (`MyFinances/backend/CurrentMonthRange.cs`).
- Frontend: the dashboard is `MyFinances/frontend/app/routes/home.tsx` (donuts at l.644-655); charts use recharts 3.10 (`CategorySpendDonut.tsx`, `PieChart`), so `LineChart` needs no new dependency. Categories come from `app/lib/categories.ts` (`CategoryDto {id, name, kind}`). There is no frontend test framework; the backend has xUnit with `FixedTimeProvider` and `AuthApiFactory`.
- A parallel change, `chart-period-selector`, edits `DashboardEndpoints.cs` and `home.tsx`. This plan must not depend on it and must keep its edits to those two files minimal.

## Desired End State

On the dashboard, below the donuts, a full-width "Category trend" card with controls for granularity (week / month / year), range (from–to), kind (spend / income) and a category checklist. On first open it shows the last 12 months for spend with the 5 highest-spend categories ticked. Changing any control refetches/reshapes the chart; each category keeps a stable colour. Verified by backend xUnit tests and a manual browser run.

### Key Discoveries:

- Exclusion/sign rules and Warsaw "today" already exist and must be reused, not re-derived (`DashboardEndpoints.cs:153-167`, `CurrentMonthRange.cs`).
- Existing tests use 2099 fake-clock dates because the auth cookie expires 30 days after the fake "now" (`DashboardEndpointsTests.cs:224-232`); new tests must do the same.
- Fixed test category GUIDs `00000000-0000-0000-0000-00000000000{1,2,3}` (Groceries, Dining, Transport) are available for seeding.
- Returning series for all categories with data keeps top-5 selection a pure frontend concern.

## What We're NOT Doing

- No dependency on, or reuse of, the shared period selector from `chart-period-selector`.
- No mixing of spend and income on one chart, and no uncategorized or internal-transfer lines.
- No drilldown from a point to the transaction list, no export, no comparison with averages.
- No frontend test framework, no persistence of selections (local state only, resets on reload).
- No per-user timezone; no changes to roadmap numbering beyond adding the item.

## Implementation Approach

Backend adds one endpoint in its own file so it does not collide with the concurrent change:

`GET /api/dashboard/category-trend?granularity=week|month|year&from=yyyy-MM-dd&to=yyyy-MM-dd&kind=spend|income`

Response: the ordered list of buckets plus one series per category that has a non-zero total in the range, each with a zero-filled amount per bucket and a range total. Buckets: ISO weeks (Monday–Sunday, labelled by their Monday), calendar months, calendar years. Buckets overlapping the range edge are clipped (only transactions inside `from`–`to` count); empty buckets are zero. Validation (400): unknown `granularity`/`kind`, `from > to`, `to` after today, or more than 120 buckets. The current, unfinished bucket is allowed. The frontend defaults to the last 12 weeks / 12 months / all years (max 5), picks the 5 series with the highest total as the default selection, and assigns each category a stable colour.

## Phase 1: Category trend endpoint

### Overview

Backend aggregation endpoint with validation and tests.

### Changes Required:

#### 1. Trend endpoint

**File**: `MyFinances/backend/Dashboard/CategoryTrendEndpoints.cs` (new), registered where the dashboard endpoints are mapped (`MyFinances/backend/Program.cs:114` area)

**Intent**: Group the user's qualifying transactions by category and by week/month/year bucket within the requested range, using the donuts' exclusion and sign rules and the injected `TimeProvider` for "today".

**Contract**: Route and parameters as in Implementation Approach, behind the existing authorized `/api` group. Response DTOs go in `MyFinances/backend/Dashboard/DashboardContracts.cs` or alongside the endpoint: buckets (`start`, `end` as dates, clipped to the range) and series (`categoryId`, `categoryName`, `amounts[]` aligned with buckets, `total`). Spend amounts are positive magnitudes. Series ordered by category `SortOrder` (as the donuts), omitting categories with a zero total. Runs transfer detection first, like the donuts. ISO week via Monday-start arithmetic (a week straddling a year boundary is one bucket labelled by its Monday).

#### 2. Tests

**File**: `MyFinances/backend/Tests/CategoryTrendEndpointsTests.cs` (new)

**Intent**: Pin bucketing, clipping, exclusions and validation.

**Contract**: Tests with `FixedTimeProvider` (2099 dates) for — 401 without auth; month, week and year bucketing; Monday-start weeks including a week across a year boundary; clipping of edge buckets to `from`/`to`; zero-filled empty buckets; uncategorized and internal-transfer rows excluded; spend vs income sign and `kind`; per-user isolation; categories with no data omitted; 400 for bad `granularity`/`kind`, `from > to`, future `to`, more than 120 buckets; current unfinished bucket accepted.

### Success Criteria:

#### Automated Verification:

- Backend builds and all tests pass: `dotnet test` (from `MyFinances/backend`)
- New `CategoryTrendEndpointsTests` cover bucketing, clipping, exclusions and the 400 cases
- Frontend still typechecks: `npm run typecheck` (from `MyFinances/frontend`)

#### Manual Verification:

- `/swagger` shows `category-trend` with its parameters
- Calling it against real data returns plausible per-bucket sums for a known category and month

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Line chart on the dashboard

### Overview

New chart component with its controls, placed under the donuts.

### Changes Required:

#### 1. Chart component

**File**: `MyFinances/frontend/app/components/CategoryTrendChart.tsx` (new)

**Intent**: Self-contained card owning its state (granularity, from, to, kind, selected categories) that fetches the trend endpoint and renders a recharts `LineChart` with one line per selected category.

**Contract**: Controls — granularity buttons (week/month/year), two date inputs (`to` capped at today, `from <= to`, a client-side message when the range would exceed 120 buckets), spend/income toggle, category checklist built from the returned series. Defaults: 12 weeks / 12 months / all years (max 5) ending today; first load and each kind/range change selects the 5 series with the highest `total`, later manual ticks are preserved until kind or range changes. Stable per-category colours (deterministic by category id order). Empty state when the range has no data; error state on failed fetch; stale responses discarded like the filter effect in `home.tsx`. DTO mirrored locally with the "Mirrors the backend's …" comment convention. Existing input/button styling reused from `home.tsx` (Tailwind dark theme, `brand-*`).

#### 2. Dashboard placement

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Render the chart as a full-width card below the donut grid for logged-in users only — a minimal insertion, to avoid conflicting with the concurrent period-selector change.

**Contract**: One import and one JSX element; no changes to existing state or effects.

### Success Criteria:

#### Automated Verification:

- Frontend typechecks: `npm run typecheck` (from `MyFinances/frontend`)
- Production build succeeds: `npm run build` (from `MyFinances/frontend`)
- Backend tests still pass: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- First open shows the last 12 months of spend with the 5 highest-spend categories ticked
- Switching to week and year changes the X axis buckets and default ranges correctly
- Unticking/ticking categories adds/removes lines; colours stay stable
- Spend/income toggle switches the available categories and amounts
- A range exceeding the bucket cap and a future end date are blocked with a clear message
- Empty range and API error states render sensibly; no console errors; usable on a narrow viewport
- Dashboard donuts and list behave as before

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- Covered through endpoint tests (bucketing is internal to the endpoint; extract a pure bucket helper only if it keeps tests simple)

### Integration Tests:

- `CategoryTrendEndpointsTests` with `FixedTimeProvider` for bucketing, clipping, exclusions, sign/kind and validation

### Manual Testing Steps:

1. Open the dashboard with imported data and confirm the default chart (12 months, spend, top 5).
2. Switch granularity and kind, change the range, tick/untick categories.
3. Compare one point's value with the sum of the matching transactions in the list.
4. Try invalid ranges (future end, too many buckets).

## Performance Considerations

One grouped query per request over the user's rows in the range; the 120-bucket cap bounds the response size and chart points. No caching needed at this scale.

## Migration Notes

None. No schema change; only a new endpoint and component.

## References

- Related change planned in parallel: `context/changes/chart-period-selector/plan.md` (touches `DashboardEndpoints.cs` and `home.tsx`; keep edits here minimal)
- Archived donut plan: `context/archive/2026-09-28-category-spend-donut-chart/plan-brief.md`
- Exclusion/sign rules: `MyFinances/backend/Dashboard/DashboardEndpoints.cs:153-167`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Category trend endpoint

#### Automated

- [x] 1.1 Backend builds and all tests pass: `dotnet test` (from `MyFinances/backend`) — 8e22707
- [x] 1.2 New `CategoryTrendEndpointsTests` cover bucketing, clipping, exclusions and the 400 cases — 8e22707
- [x] 1.3 Frontend still typechecks: `npm run typecheck` (from `MyFinances/frontend`) — 8e22707

#### Manual

- [ ] 1.4 `/swagger` shows `category-trend` with its parameters
- [ ] 1.5 Calling it against real data returns plausible per-bucket sums for a known category and month

### Phase 2: Line chart on the dashboard

#### Automated

- [x] 2.1 Frontend typechecks: `npm run typecheck` (from `MyFinances/frontend`)
- [x] 2.2 Production build succeeds: `npm run build` (from `MyFinances/frontend`)
- [x] 2.3 Backend tests still pass: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 2.4 First open shows the last 12 months of spend with the 5 highest-spend categories ticked
- [ ] 2.5 Switching to week and year changes the X axis buckets and default ranges correctly
- [ ] 2.6 Unticking/ticking categories adds/removes lines; colours stay stable
- [ ] 2.7 Spend/income toggle switches the available categories and amounts
- [ ] 2.8 A range exceeding the bucket cap and a future end date are blocked with a clear message
- [ ] 2.9 Empty range and API error states render sensibly; no console errors; usable on a narrow viewport
- [ ] 2.10 Dashboard donuts and list behave as before
