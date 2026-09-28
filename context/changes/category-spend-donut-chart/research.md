---
date: 2026-09-28T00:00:00+02:00
researcher: Claude (Sonnet 5)
git_commit: 56de3f0e263aa0c6eae10e07e2b933a1061b81ed
branch: main
repository: Kurs 10xDev (MyFinances)
topic: "S-04: category-spend-donut-chart — donut chart of spend share per category for the current month, filterable by category"
tags: [research, codebase, backend-transactions, backend-categorization, frontend-dashboard, charting]
status: complete
last_updated: 2026-09-28
last_updated_by: Claude (Sonnet 5)
---

# Research: S-04 category-spend-donut-chart

**Date**: 2026-09-28
**Researcher**: Claude (Sonnet 5)
**Git Commit**: 56de3f0e263aa0c6eae10e07e2b933a1061b81ed
**Branch**: main
**Repository**: Kurs 10xDev (MyFinances)

## Research Question

What does the codebase already provide — and what is genuinely missing — for building S-04: a donut chart of spend share per category for the current month, filterable by category, excluding uncategorized and internal-transfer transactions? Scope: backend data model/endpoints, frontend dashboard/fetch conventions, charting library choice, and any prior decisions/hand-offs from S-03/S-09.

## Summary

No aggregation endpoint, current-month date-filtering logic, or category-filter UI exists anywhere in the codebase yet — S-04 has to build all three from scratch. The prerequisite data is fully in place: `Transaction.CategoryId` (nullable = uncategorized) and `Transaction.IsInternalTransfer` (bool) both exist and are scoped per-user (`Transactions\Transaction.cs:8-43`), and S-03's plan explicitly hands these two fields to S-04 (`context/archive/2026-09-25-categorization-queue/plan.md:101-102`). The amount sign convention is confirmed negative-for-spend (mBank import stores `-500.00m` for a purchase — `MyFinances/backend/Tests/MBankCsvParserTests.cs:38`), so a spend-share aggregation should filter `Amount < 0` and use the absolute value. Recharts is the already-decided charting library per the tech-stack forward notes (`context/foundation/shape-notes.md:139`) but is not yet installed in `MyFinances/frontend/package.json` — this will be the first chart dependency and the first chart UI in the app (`context/changes/homepage-redesign/plan.md:34-35` explicitly deferred all chart/dashboard-widget work out of that change).

## Detailed Findings

### Backend: Transaction and Category data model

- `Transaction` entity (`MyFinances/backend/Transactions/Transaction.cs:8-43`): `UserId` (Guid, scoping), `AccountId`, `Date` (`DateOnly`), `Amount` (`decimal`, signed — negative = expense, confirmed below), `CategoryId` (`Guid?` — null means uncategorized, per an explicit comment on the field), `Category` nav, `IsInternalTransfer` (`bool`), `TransferFlagManuallySet` (`bool`).
- **Amount sign convention — confirmed**: mBank CSV import parses signed amounts (`MyFinances/backend/Import/MBankCsvParser.cs:164-165`, `AllowLeadingSign`), and the parser's own tests assert negative amounts for real spend rows, e.g. `t.Amount == -500.00m && t.Description == "NA JEDZENIE"` (`MyFinances/backend/Tests/MBankCsvParserTests.cs:38`, also lines 51, 113-114, 129-130). No sign normalization happens anywhere downstream (`TransactionEndpoints.cs`, `CategorizationEndpoints.cs`) — the value is persisted as-is. **Condition**: this is confirmed for mBank-imported transactions only (the only parser implemented so far); manually-entered transactions (S-02) were not re-verified in this pass but use the same `Transaction.Amount` field with no separate sign-enforcement path found.
- `Category` entity (`MyFinances/backend/Categorization/Category.cs:6-13`): `Id`, `Name`, `SortOrder`. Fixed, seeded, not user-owned (comment at `Category.cs:3-4`). 12 categories seeded via `HasData` in `AppDbContext.cs:59-72` (Groceries, Dining & Takeout, Transport, Housing & Utilities, Health, Shopping, Entertainment, Travel, Subscriptions, Income, Fees & Charges, Other), each with a deterministic GUID and `SortOrder`.
- FK config: `AppDbContext.cs:27-56` — non-unique `(UserId, Hash)` index on `Transaction`; both `Account` and `Category` FKs use `DeleteBehavior.Restrict`.

### Backend: existing endpoints and what's missing

All endpoints are mounted under `/api` with `.RequireAuthorization()` on the whole group (`Program.cs:105-111`).

- `GET /api/categorization/queue` (`Categorization/CategorizationEndpoints.cs:27-47`) filters `t.UserId == userId && t.CategoryId == null && !t.IsInternalTransfer` (line 42) — the "still needs categorizing" set, i.e. the opposite of what a chart needs.
- `GET /api/categorization/handled` (`Categorization/CategorizationEndpoints.cs:49-67`) filters `t.UserId == userId && (t.CategoryId != null || t.IsInternalTransfer)` (line 62) — an OR, not the AND (`CategoryId != null && !IsInternalTransfer`) a donut chart needs, and has no month bound.
- `GET /api/transactions/` (`Transactions/TransactionEndpoints.cs:24-59`) only takes `skip`/`take` params (lines 24-26) and filters solely by `UserId` (line 36) — no category filter, no date/month filter, no internal-transfer exclusion.
- **No dashboard/aggregation endpoint exists.** A repo-wide grep for `month|dashboard|DateTime.UtcNow|StartOfMonth` across backend `.cs` files (excluding bin/obj/Migrations) returned zero matches — there is no current-month date-boundary logic anywhere yet to reuse. S-04's plan will need to design this from scratch.
- Auth/user-scoping pattern to follow: `ClaimsPrincipalExtensions.GetUserId(this ClaimsPrincipal, UserManager<AppUser>)` (`Transactions/ClaimsPrincipalExtensions.cs:9-21`), used as `var userId = principal.GetUserId(userManager);` then `.Where(t => t.UserId == userId)` in every endpoint (e.g. `TransactionEndpoints.cs:31,36`; `CategorizationEndpoints.cs:33,55,76`).

### Backend: test conventions

- `MyFinances/backend/Tests/TransactionEndpointsTests.cs` (628 lines) — naming convention `MethodUnderTest_Scenario_ExpectedResult` (stated at lines 17-18), e.g. `List_OnlyReturnsCurrentUsersTransactions` (line 182).
- Uses `AuthApiFactory` + `TestClientHelpers.CreateAuthenticatedClientAsync(factory)` (line 171) for an authenticated `HttpClient`; direct DB seeding via `factory.Services.CreateScope()` → `AppDbContext` for setup not worth doing through the API (e.g. `SeedTransactionsAsync`, line 44-63).
- Per-user data isolation is always explicitly tested — a convention worth replicating for a new chart-aggregation endpoint.
- Mutating requests need the antiforgery flow (`GET /api/auth/antiforgery-token` → `X-XSRF-TOKEN` header); **not needed** for a GET-only chart endpoint.

### Frontend: dashboard route and data-fetch pattern

- Dashboard route: `MyFinances/frontend/app/routes/home.tsx`, registered via `index("routes/home.tsx")` in `app/routes.ts:4`.
- Fetch pattern is a hybrid, **not** React Query/SWR (none installed): a `clientLoader` (`home.tsx:115-137`) does the initial auth-check + first transactions page fetch for first paint; component then reads that via `useLoaderData` and re-fetches imperatively through plain `useState` + `apiFetch` calls for subsequent actions (`refreshTransactions`, `handleLoadMore`, lines 252-277). A separate `useEffect` (lines 162-179) fetches dropdown data (accounts, categories) once `user` is present.
- `apiFetch<T>(path, init?)` (`app/lib/api.ts:34-50`): base path defaults to `/api`; every call sets `credentials: "include"` (cookie auth, no bearer tokens); mutating methods (POST/PUT/PATCH/DELETE) auto-fetch a fresh antiforgery token first (lines 25-39) — **a GET-only chart-data call needs none of that**, just `apiFetch<CategorySpendDto[]>('/dashboard/category-spend?...')`. Non-2xx responses throw `ApiError` carrying the raw `Response` (lines 8-16, 43-45); callers parse `.json()` for a `.title` field (ASP.NET Core ProblemDetails convention) inside try/catch for a generic fallback message.
- **No category-filter or month-filter UI exists anywhere** — only category-*assignment* `<select>`s bound to local `useState` (`home.tsx:465-479`, `categorize.tsx:178-191,245-259`). A repo-wide grep found zero uses of `useSearchParams`/`URLSearchParams` in `app/` — there is no existing convention for URL-param-driven filters; the closest precedent is plain component `useState` for similar toggles (`categorize.tsx:52-53`).
- Category source for a filter dropdown: `GET /categorization/categories` → `CategoryDto[] { id, name }`, already fetched via `apiFetch` in both `home.tsx` and `categorize.tsx`.

### Frontend: dependencies and charting

- `MyFinances/frontend/package.json`: React `^19.2.8`, React Router `^8.4.0` (full framework mode — `@react-router/dev`, `@react-router/node`, `@react-router/serve`), Tailwind `^4.2.2`, TypeScript `^5.9.3`, Vite `^8.0.3`.
- **No chart library is installed** — grep across the repo for recharts/chart.js/visx/d3/nivo found no actual package references (only base64 integrity-hash substrings in `package-lock.json`). Recharts is the already-decided pick per `context/foundation/shape-notes.md:139` ("Forward: tech-stack" table, `| Charts | Recharts |`) but has not been added yet — S-04 will be the first PR to add a chart dependency and build the first chart UI in this app.

### Frontend: component/folder conventions

- `app/routes/*.tsx` — one file per route, default-exports the page component; registered in `app/routes.ts`. Route files define local DTO interfaces mirroring backend contracts with a comment noting the source file, e.g. `// Mirrors the backend's TransactionContracts.cs TransactionListItemDto.` (`home.tsx:33`).
- `app/components/*.tsx` — currently only `AppHeader.tsx` (named export, PascalCase file matching export name) — a new chart component (e.g. `CategorySpendDonut.tsx`) fits here.
- `app/lib/*.ts` — shared utilities; currently only `api.ts`.
- Styling: Tailwind v4 utility classes inline, dark theme (`bg-gray-950`, `text-gray-200`, `border-gray-800`, `brand-400..900` scale), no CSS modules.
- Loading/empty/error conventions to mirror: `text-sm text-gray-400` for "Loading…" and empty-state copy (`categorize.tsx:150,216,224`), `text-sm text-red-600` for fetch errors (`home.tsx:481,610`; `categorize.tsx:152`).

## Code References

- `MyFinances/backend/Transactions/Transaction.cs:8-43` — Transaction entity, all relevant fields
- `MyFinances/backend/Categorization/Category.cs:6-13` — Category entity
- `MyFinances/backend/AppDbContext.cs:27-72` — FK config + seeded category data
- `MyFinances/backend/Categorization/CategorizationEndpoints.cs:27-67` — queue/handled filter patterns to adapt
- `MyFinances/backend/Transactions/TransactionEndpoints.cs:24-59` — existing `/transactions` list endpoint (no month/category filter)
- `MyFinances/backend/Transactions/ClaimsPrincipalExtensions.cs:9-21` — user-scoping helper
- `MyFinances/backend/Tests/MBankCsvParserTests.cs:38,51,113-114,129-130` — confirms negative-amount-for-spend convention
- `MyFinances/backend/Tests/TransactionEndpointsTests.cs:17-18,44-63,171,182` — test naming/seeding conventions
- `MyFinances/frontend/app/routes/home.tsx:115-179,252-277,465-479,546-644` — dashboard loader/fetch/empty-state patterns
- `MyFinances/frontend/app/lib/api.ts:1-50` — `apiFetch` client
- `MyFinances/frontend/app/routes/categorize.tsx:49-53,150-224` — loading/error/empty-state convention, category-select pattern
- `MyFinances/frontend/package.json` — confirmed dependency set, no chart library present
- `context/foundation/shape-notes.md:139` — Recharts locked in as the chart library

## Architecture Insights

- Backend consistently uses minimal-API endpoint files grouped by feature folder (`Categorization/`, `Transactions/`, `Import/`), each with a `*Contracts.cs` (records) + `*Endpoints.cs` (route handlers) pair, and DI-injected `AppDbContext`/`UserManager<AppUser>`/`ClaimsPrincipal` per handler — a new `Dashboard/` (or similar) feature folder following this same two-file pattern is the path of least resistance for a category-spend aggregation endpoint.
- Frontend has no client-side cache/query library — every fetch is a plain `apiFetch` call wired to `useState`; a new chart component should follow this same imperative-fetch style rather than introducing a new data-fetching pattern.
- No URL-param-based filter state exists anywhere yet; introducing one for month/category filtering on the dashboard would be a new pattern for this codebase (worth deciding deliberately in the plan rather than defaulting to it).

## Historical Context (from prior changes)

- `context/archive/2026-09-25-categorization-queue/plan.md:101-102` — explicit hand-off: *"Wiring the categorization queue into the donut chart or spend totals — that's S-04, which consumes `CategoryId`/`IsInternalTransfer` but is out of scope here."*
- `context/archive/2026-09-25-transaction-history-view/plan.md:32` — explicit hand-off: *"No date-range filtering (that's the parked FR-016) or current-month filtering (that's S-04, which depends on categorization)."* Confirms current-month filtering was deliberately deferred to this change, not an oversight.
- `context/archive/2026-09-25-transaction-history-view/plan.md:10,22,56-57` — established `TransactionListItemDto(Guid Id, DateOnly Date, string Description, decimal Amount, Guid? CategoryId)` for `GET /api/transactions` — this DTO **omits** `IsInternalTransfer` and category name, so it cannot be reused as-is for chart aggregation; a new DTO/endpoint is needed regardless of whether aggregation happens client- or server-side.
- `context/changes/homepage-redesign/plan.md:34-35` — explicit non-goal: *"Not building placeholder dashboard widgets, charts, or sample data."* Confirms no chart-card visual pattern exists yet to copy; S-04 sets the precedent.
- `context/changes/homepage-visual-refresh/change.md` — added branding (logo, wordmark) but no chart-relevant styling decisions captured at the change.md level.
- `context/foundation/shape-notes.md:97-105` (US-01 acceptance criteria, mirrored in `prd.md`) — most detailed existing spec of expected chart behavior: current-month only, excludes uncategorized until categorized, excludes internal transfers, category filter narrows both chart and list, budget-vs-actual comparison is explicitly S-05 scope (not S-04).

## Related Research

None — this is the first research document for this change.

## Open Questions

- **Backend vs. frontend aggregation**: no prior decision found either way. A new backend endpoint (e.g. `GET /api/dashboard/category-spend?month=YYYY-MM`) that does the `GroupBy(CategoryId)` + `Sum(Amount)` server-side, filtered to `CategoryId != null && !IsInternalTransfer && Amount < 0` and the given month, would avoid re-fetching/paginating through all transactions client-side and matches the existing per-feature-endpoint backend architecture — but this is a plan-time decision, not settled by research.
- **Filter-state mechanism** (URL search params vs. local `useState`): no existing convention either way in this frontend; needs a deliberate choice in the plan since it's a new pattern for the codebase either way.
- **Manually-entered (S-02) transaction sign convention**: confirmed for mBank-imported transactions (negative = spend) but not independently re-verified for manually-entered transactions in this pass — worth a quick check during planning/implementation if manual entries commonly use positive amounts for expenses in the UI.
