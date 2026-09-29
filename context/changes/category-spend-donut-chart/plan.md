# Category Spend Donut Chart (S-04) Implementation Plan

## Overview

Add a donut chart to the dashboard showing spend share per category for the current month (uncategorized and internal-transfer transactions excluded), where clicking a slice filters the existing transaction list to that category for the current month. This is the slice the PRD's Primary Success Criterion is built around.

## Current State Analysis

- `Transaction` (`MyFinances/backend/Transactions/Transaction.cs:8-43`) already carries everything the chart needs: `CategoryId` (`Guid?`, null = uncategorized), `IsInternalTransfer` (`bool`), and a signed `Amount` (`decimal`) — confirmed negative for real spend via the mBank parser's own tests (`MyFinances/backend/Tests/MBankCsvParserTests.cs:38,51,113-114,129-130`).
- `Category` (`MyFinances/backend/Categorization/Category.cs:6-13`) is a fixed, seeded 12-row list ordered by `SortOrder` (`AppDbContext.cs:59-72`) — not user-editable.
- No aggregation endpoint, no current-month date-boundary logic, and no category/date filter on any existing endpoint exist anywhere in the backend (`Categorization/CategorizationEndpoints.cs`, `Transactions/TransactionEndpoints.cs` both grepped and read in full — see `research.md`).
- The dashboard route (`MyFinances/frontend/app/routes/home.tsx`) already renders a paginated, unfiltered transaction list fed by `GET /api/transactions?skip=&take=`, with no category or date filter UI. No chart library is installed; Recharts is the locked-in pick (`context/foundation/shape-notes.md:139`) but this will be the first chart in the app.
- Amount sign is used directly in the UI today (`home.tsx:564-567`: `amount < 0` → red, else emerald) — the same convention the aggregation endpoint should rely on.

## Desired End State

A logged-in user sees a donut chart above their transaction list on the dashboard, showing the current month's spend broken down by category (uncategorized and internal-transfer transactions excluded, amounts shown as positive spend). Clicking a slice filters the transaction list below to that category, scoped to the current month; clicking the same slice again (or a visible "clear filter" control) removes the filter. If there's no categorized, non-transfer spend yet this month, the chart area shows a message pointing to `/categorize` instead of an empty chart.

### Key Discoveries:

- `Categorization/CategorizationEndpoints.cs:27-47,49-67` establishes the exact `TransferDetectionService.DetectAsync` → then-query pattern that must run before any transaction query that depends on `IsInternalTransfer` being current — the new endpoint must follow this.
- `Transactions/ClaimsPrincipalExtensions.cs:9-21` is the one auth/user-scoping helper every endpoint uses (`principal.GetUserId(userManager)`).
- Every mutating endpoint uses `.AddEndpointFilter(RequireValidAntiforgery)`; the new endpoint is GET-only and needs none of that.
- `Tests/TransactionEndpointsTests.cs` establishes `MethodUnderTest_Scenario_ExpectedResult` naming, `AuthApiFactory` + `TestClientHelpers.CreateAuthenticatedClientAsync`, and direct-DB-seeding helpers as the test conventions to follow.

## What We're NOT Doing

- Budget-vs-actual comparison (FR-017) and the historical-average deviation signal (FR-013) — both are S-05/S-06, separate slices per the roadmap.
- Any date range other than the current month (no month picker, no arbitrary date-range filter — that's the parked FR-016).
- Per-user timezone handling for the month boundary — the boundary is computed from server UTC time; acceptable for this single-user personal tool where no NFR requires timezone precision.
- A separate category-filter dropdown — the only category filter is "click a donut slice" (see Key Decisions in the brief).
- Changing how manually-entered (S-02) transactions store their amount sign — this plan relies on the sign convention already established by import (confirmed negative-for-spend) and used unmodified.
- URL-param-based filter state — the selected category lives in local component state (`useState`), matching the only pattern this codebase currently uses for equivalent toggles.

## Implementation Approach

Backend does the aggregation (GroupBy + Sum, filtered to categorized/non-transfer/current-month/negative-amount) so the frontend never has to paginate through all transactions or duplicate the exclusion rules. A new `Dashboard` feature folder mirrors the existing `Categorization`/`Transactions` two-file (`*Contracts.cs` + `*Endpoints.cs`) convention. The existing `GET /api/transactions` endpoint gains two optional filter params (`categoryId`, `currentMonth`) reusing the same month-boundary helper, so clicking a donut slice can filter the existing list with no new endpoint. On the frontend, a new `CategorySpendDonut` component owns its own fetch/loading/error/empty state and reports slice selection up to `home.tsx` via a callback, which re-fetches the transaction list with the filter applied.

## Phase 1: Backend — category-spend endpoint and transaction list filter

### Overview

Add the aggregation endpoint, a shared current-month boundary helper, and extend the existing transaction list endpoint to filter by category + current month.

### Changes Required:

#### 1. Current-month boundary helper

**File**: `MyFinances/backend/CurrentMonthRange.cs` (new, root `MyFinances.Api` namespace, alongside `AppDbContext.cs`)

**Intent**: Single source of truth for "current month" as a `[Start, End]` `DateOnly` pair, computed from server UTC time, shared by both the new dashboard endpoint and the extended transactions endpoint so they can never disagree on the boundary.

**Contract**: `public static class CurrentMonthRange { public static (DateOnly Start, DateOnly End) Get(); }` — `Start` is the 1st of the current UTC month, `End` is the last day of that month.

#### 2. Dashboard feature folder

**File**: `MyFinances/backend/Dashboard/DashboardContracts.cs` (new)

**Intent**: DTO for the category-spend aggregation response.

**Contract**: `public record CategorySpendDto(Guid CategoryId, string CategoryName, decimal Amount);` — `Amount` is a positive total (spend magnitude, not the raw signed value).

**File**: `MyFinances/backend/Dashboard/DashboardEndpoints.cs` (new)

**Intent**: `GET /api/dashboard/category-spend` — for the current user, runs `TransferDetectionService.DetectAsync` first (same ordering as `CategorizationEndpoints.cs:37,57`), then groups the current month's transactions by category, excluding uncategorized (`CategoryId == null`) and internal-transfer (`IsInternalTransfer == true`) rows, summing only negative (`Amount < 0`) rows as positive spend magnitudes. Categories with zero current-month spend are omitted from the response (no zero-value entries). Results are ordered by `Category.SortOrder` to keep chart/legend ordering stable and consistent with `GET /api/categorization/categories`.

**Contract**:
```
categorization.MapGet is the existing pattern; mirror it:
GET /api/dashboard/category-spend -> 200 OK, CategorySpendDto[]
Filter: t.UserId == userId && t.CategoryId != null && !t.IsInternalTransfer
        && t.Amount < 0 && t.Date >= range.Start && t.Date <= range.End
GroupBy CategoryId, Sum(-Amount), join Category for Name + SortOrder, OrderBy SortOrder.
```

**Critical**: Do this grouping/summing in memory, not as a DB-side `GroupBy`. `Import/ImportEndpoints.cs:62-70` documents this codebase's own precedent — a prior DB-side `GroupBy` was deliberately avoided with the comment "doesn't reliably translate," replaced with `ToListAsync()` first, then `.GroupBy(...)` in LINQ-to-Objects. No test in this repo exercises a DB-translated `GroupBy` against either the EF Core InMemory provider (used by `AuthApiFactory`) or Npgsql. Apply the same pattern here: `Include(t => t.Category)`, materialize the filtered query with `ToListAsync()`, then `GroupBy(t => t.CategoryId)`/`Sum` in memory before ordering by `SortOrder`. The per-user, current-month row count is small enough that this has no meaningful cost.

**File**: `MyFinances/backend/Program.cs`

**Intent**: Register the new endpoint group.

**Contract**: Add `using MyFinances.Api.Dashboard;` and `api.MapDashboardEndpoints();` alongside the existing `api.MapCategorizationEndpoints(); api.MapTransactionEndpoints();` (`Program.cs:110-111`).

#### 3. Extend the transaction list endpoint with category + current-month filters

**File**: `MyFinances/backend/Transactions/TransactionEndpoints.cs`

**Intent**: Let the dashboard's "click a slice to filter" interaction reuse the existing list endpoint instead of a new one. Add two optional query params that compose with the existing `skip`/`take` pagination.

**Contract**: `MapGet("/", async (int? skip, int? take, Guid? categoryId, bool? currentMonth, ...) => ...)` — when `categoryId` is provided, add `.Where(t => t.CategoryId == categoryId)`; when `currentMonth == true`, add `.Where(t => t.Date >= range.Start && t.Date <= range.End)` using `CurrentMonthRange.Get()`. Both filters apply before the existing `OrderByDescending(Date).ThenByDescending(Id).Skip().Take()` and `CountAsync()` (so `hasMore`/`totalCount` reflect the filtered set, not the full list) — apply to the same `query` variable already used at `TransactionEndpoints.cs:36,40,55`.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Existing tests still pass: `dotnet test` (from `MyFinances/backend`)
- New `Tests/DashboardEndpointsTests.cs` covers: categorized/non-transfer current-month spend is summed correctly per category as a positive amount; uncategorized transactions are excluded; internal-transfer transactions are excluded; transactions outside the current month are excluded; a category with zero current-month spend is omitted from the response; results are scoped to the calling user only (another user's transactions never appear); unauthenticated request returns 401
- `Tests/TransactionEndpointsTests.cs` gains tests for: `categoryId` filter narrows results to that category; `currentMonth=true` excludes out-of-month transactions; both filters combined; `hasMore`/total count reflect the filtered set, not the unfiltered list

#### Manual Verification:

- `GET /api/dashboard/category-spend` (via Swagger UI, `dotnet run` then `/swagger`) returns sensible category totals against real imported/categorized data for the current month
- `GET /api/transactions?categoryId=<id>&currentMonth=true` returns only that category's current-month transactions

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — donut chart and dashboard filter wiring

### Overview

Install Recharts, build the `CategorySpendDonut` component, and wire it into `home.tsx` so selecting a slice filters the existing transaction list.

### Changes Required:

#### 1. Add the chart dependency

**File**: `MyFinances/frontend/package.json`

**Intent**: Add Recharts as the charting library, matching the tech-stack hand-off decision.

**Contract**: Add `"recharts": "^3.3.0"` to `dependencies`; run `npm install` from `MyFinances/frontend` to update `package-lock.json`.

#### 2. Donut chart component

**File**: `MyFinances/frontend/app/components/CategorySpendDonut.tsx` (new)

**Intent**: Self-contained component that fetches the current month's category spend, renders it as a donut (`PieChart` + `Pie` with `innerRadius`/`outerRadius`, per-slice colors from a small fixed palette array indexed by position), and reports the selected category up to the parent. Owns its own loading/error/empty states, matching the `text-sm text-gray-400` (loading/empty) and `text-sm text-red-600` (error) conventions from `categorize.tsx:150,152` and `home.tsx:610`.

**Contract**:
```typescript
interface CategorySpendDonutProps {
  selectedCategoryId: string | null;
  onSelectCategory: (categoryId: string | null) => void;
}
// Mirrors the backend's DashboardContracts.cs CategorySpendDto.
interface CategorySpendDto {
  categoryId: string;
  categoryName: string;
  amount: number;
}
export function CategorySpendDonut(props: CategorySpendDonutProps): JSX.Element
```
Fetches `GET /dashboard/category-spend` via `apiFetch` in a `useEffect` on mount (own `loading`/`loadError`/`data` state, not `useLoaderData` — this is a secondary widget, not the route's primary loader data). Empty array → render the "No categorized spend for this month yet." message with a `Link` to `/categorize`. On `Pie`'s slice click: if the clicked slice's `categoryId` equals `selectedCategoryId`, call `onSelectCategory(null)` (deselect); otherwise call `onSelectCategory(categoryId)`. Render a `Legend` and a `Tooltip` (Pie's default tooltip mode is `item`, per Recharts' supported-modes table) showing category name + amount. Visually indicate the selected slice (e.g. reduced opacity on non-selected slices) so the active filter is obvious without extra chrome. The palette array must have at least 12 entries (matching the 12 seeded categories, `AppDbContext.cs:59-72`) so no two categories share a color in the same month; index with `paletteIndex % palette.length` regardless, as a safety margin if the category count ever grows.

#### 3. Wire the component into the dashboard

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Render the donut above the transaction list for logged-in users, and make the list's fetch calls (`clientLoader`'s initial load, `handleLoadMore`, `refreshTransactions`) include `categoryId`/`currentMonth` query params when a category is selected, resetting to page 1 on selection change.

**Contract**: Add `const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);` and a `useEffect` keyed on `selectedCategoryId` that re-fetches page 1 of `/transactions` with `categoryId=<id>&currentMonth=true` appended when set, or the original unfiltered query when cleared — reusing the existing `TransactionListResponseDto` shape and `setItems`/`setHasMore` setters already in the component (`home.tsx:141-144`). Render `<CategorySpendDonut selectedCategoryId={selectedCategoryId} onSelectCategory={setSelectedCategoryId} />` as a new section above the existing `transactionForm`/list block (inside the `user` branch, always shown — independent of whether `hasTransactions` is true, since it has its own empty state). When a filter is active, show a small "Filtering by: {category name} · Clear" affordance near the list (category name is available from the chart's already-fetched data).

**Critical**: `handleLoadMore` (`home.tsx:252-266`) and `refreshTransactions` (`home.tsx:268-277`) currently build their own `/transactions?skip=...&take=...` query strings from local closure state only, with no channel for any external filter — `refreshTransactions` is called from every write path (`submitTransaction` at `home.tsx:344`, `handleDelete` at `home.tsx:368`). Both functions **must** be updated to also append `categoryId=<selectedCategoryId>&currentMonth=true` whenever `selectedCategoryId` is set (e.g. by reading `selectedCategoryId` from the enclosing component scope, since both are already inner functions of the same component). Without this, clicking "Load more" while filtered appends unfiltered results under a filtered first page, and any add/edit/delete while filtered silently reverts the list to the unfiltered view.

### Success Criteria:

#### Automated Verification:

- Frontend type-checks: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- Dashboard shows the donut chart above the transaction list for a logged-in user with categorized current-month spend, with distinct colors per category and a working legend/tooltip
- Clicking a slice filters the transaction list to that category (current month only); clicking it again clears the filter, restoring the full unfiltered list
- "Filtering by: X · Clear" affordance appears when a filter is active and correctly clears it
- With zero categorized, non-transfer spend this month, the chart area shows the empty-state message and a working link to `/categorize`
- Uncategorized and internal-transfer transactions never appear in the chart's totals (verified against a manually inspected set of current-month transactions)
- Dark-theme contrast of the chart colors, legend, and tooltip is acceptable against `bg-gray-950`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `DashboardEndpointsTests.cs`: aggregation correctness (sum, exclusions, zero-value omission), current-month boundary, per-user scoping, unauthenticated access
- `TransactionEndpointsTests.cs` additions: `categoryId` filter, `currentMonth` filter, combined, pagination metadata under filters

### Integration Tests:

- None beyond the existing `WebApplicationFactory`-based endpoint tests above — no cross-service integration surface is introduced.

### Manual Testing Steps:

1. Import and categorize a mix of transactions spanning the current month and a prior month, including at least one internal transfer and one uncategorized transaction.
2. Load the dashboard and confirm the donut only reflects current-month, categorized, non-transfer spend, with amounts matching a manual sum.
3. Click a slice, confirm the transaction list narrows to that category/current month; click again to clear.
4. Delete/uncategorize all current-month categorized transactions and reload — confirm the empty-state message and `/categorize` link appear.

## Performance Considerations

Category-spend aggregation itself runs over at most one user's one month of transactions — negligible. However, the new dashboard endpoint calls `TransferDetectionService.DetectAsync` (`Categorization/TransferDetectionService.cs:17-58`) before querying, same as the existing queue/handled endpoints — this loads every one of the user's not-yet-manually-decided transactions and runs an O(n²) nested-loop match on each call. This is now a third hot path triggering that full scan (alongside `/categorization/queue` and `/categorization/handled`), likely on the most-visited page in the app. Accepted as fine for this MVP's expected single-user transaction volumes; revisit if it becomes a measured bottleneck.

## References

- Related research: `context/changes/category-spend-donut-chart/research.md`
- Categorization endpoint pattern to mirror: `MyFinances/backend/Categorization/CategorizationEndpoints.cs:27-47`
- Existing transaction list endpoint being extended: `MyFinances/backend/Transactions/TransactionEndpoints.cs:24-59`
- Dashboard route being extended: `MyFinances/frontend/app/routes/home.tsx:139-179,546-624`
- Recharts donut chart API (Context7 `/recharts/recharts`, v3.3.0): `PieChart`/`Pie` with `innerRadius`/`outerRadius`/`data`/`dataKey`/`nameKey`; `Cell` deprecated in v3 in favor of a `fill` field per data entry; `Pie`'s `activeIndex` prop removed in the 3.0 migration

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — category-spend endpoint and transaction list filter

#### Automated

- [x] 1.1 Backend builds: `dotnet build`
- [x] 1.2 Existing tests still pass: `dotnet test`
- [x] 1.3 New `DashboardEndpointsTests.cs` covers aggregation correctness, exclusions, zero-omission, per-user scoping, unauthenticated 401
- [x] 1.4 `TransactionEndpointsTests.cs` gains `categoryId`/`currentMonth` filter tests, including combined and pagination-metadata cases

#### Manual

- [x] 1.5 `GET /api/dashboard/category-spend` returns sensible totals against real data via Swagger UI
- [x] 1.6 `GET /api/transactions?categoryId=<id>&currentMonth=true` returns only that category's current-month transactions

### Phase 2: Frontend — donut chart and dashboard filter wiring

#### Automated

- [ ] 2.1 Frontend type-checks: `npm run typecheck`
- [ ] 2.2 Frontend builds: `npm run build`

#### Manual

- [ ] 2.3 Donut chart renders with distinct colors, working legend/tooltip, above the transaction list
- [ ] 2.4 Clicking a slice filters the list to that category/current month; clicking again clears it
- [ ] 2.5 "Filtering by: X · Clear" affordance appears and works correctly
- [ ] 2.6 Empty state (zero categorized current-month spend) shows message + working `/categorize` link
- [ ] 2.7 Uncategorized/internal-transfer transactions confirmed absent from chart totals
- [ ] 2.8 Dark-theme contrast of chart colors/legend/tooltip is acceptable
