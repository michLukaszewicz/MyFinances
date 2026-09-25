# Transaction History View Implementation Plan

## Overview

Add a read-only, paginated transaction history list to the authenticated dashboard (`home.tsx`), backed by a new `GET /api/transactions` endpoint. This replaces the always-shown "you haven't imported any transactions yet" placeholder with the user's real transaction data (date, description, amount, category) — the gap flagged during S-01 manual testing (roadmap S-09, FR-011 partial).

## Current State Analysis

- [`home.tsx:31-39`](../../../MyFinances/frontend/app/routes/home.tsx) `clientLoader` only fetches `/api/auth/me`; the authenticated branch ([`home.tsx:113-144`](../../../MyFinances/frontend/app/routes/home.tsx)) unconditionally renders a static empty-state message ([`home.tsx:128-133`](../../../MyFinances/frontend/app/routes/home.tsx)) regardless of whether the user has any transactions.
- [`Transaction.cs:6-29`](../../../MyFinances/backend/Transactions/Transaction.cs) already has everything needed to list history: `UserId`, `Date` (`DateOnly`), `Description`, `Amount` (signed — negative for expenses, positive for income, confirmed via `MBankCsvParser.cs:165`'s `AllowLeadingSign` parse), `CategoryId` (nullable `Guid?`, no `Category` table yet — "uncategorized" is `CategoryId == null`).
- No listing endpoint exists yet — only `/api/import/parse` and `/api/import/commit` ([`ImportEndpoints.cs`](../../../MyFinances/backend/Import/ImportEndpoints.cs)).
- No pagination/sorting precedent exists anywhere in the codebase — this is the first feature to need it.
- No `Transaction` type exists on the frontend; the closest precedent is the import-flow's row-specific DTOs in [`import.tsx:14-41`](../../../MyFinances/frontend/app/routes/import.tsx).

## Desired End State

A logged-in user visiting `/` sees their transactions listed newest-first (date, description, amount, category), with amounts colored by sign (expense/income) and uncategorized transactions labeled "Uncategorized". Up to 20 transactions load initially; a "Load more" button appends the next 20 on click, and disappears once all transactions are loaded. A user with zero transactions still sees the existing empty-state message and import CTA, unchanged. Returning to `/` after completing an import (via React Router's natural loader re-run) shows the newly imported transactions without any special cache-busting code.

### Key Discoveries:

- Amount sign is already meaningful and preserved from the source CSV ([`MBankCsvParser.cs:165`](../../../MyFinances/backend/Import/MBankCsvParser.cs)) — no new sign-inference logic needed for color-coding.
- `ClaimsPrincipalExtensions.GetUserId` ([`ClaimsPrincipalExtensions.cs:11-20`](../../../MyFinances/backend/Transactions/ClaimsPrincipalExtensions.cs)) is the established per-user scoping convention, used identically in both import endpoints.
- DTOs are plain C# `record`s grouped in one `*Contracts.cs` file per feature ([`ImportContracts.cs`](../../../MyFinances/backend/Import/ImportContracts.cs)), manually mapped, no AutoMapper.
- `/api` group applies `RequireAuthorization()` at [`Program.cs:102`](../../../MyFinances/backend/Program.cs) — a new GET endpoint inherits auth for free and needs no antiforgery filter (GETs aren't antiforgery-checked; only `/import/commit` and `/auth/logout` add that filter).

## What We're NOT Doing

- No `Category` table, category names, or category filtering — categorization is S-03. Uncategorized transactions just show a static "Uncategorized" label.
- No bank/source column (e.g. "mBank") on each row — out of scope per roadmap outcome's literal 4 fields; revisit when S-07/S-08 add more banks.
- No dedicated `/transactions` route — the list lives inline on the dashboard (`home.tsx`), per the roadmap outcome ("...on the dashboard").
- No date-range filtering (that's the parked FR-016) or current-month filtering (that's S-04, which depends on categorization).
- No manual transaction entry (S-02) — today every transaction has a non-null `ImportBatchId`; the list just renders whatever exists in `Transactions`.
- No infinite scroll or numbered pagination — "Load more" button only.

## Implementation Approach

Backend first (Phase 1): a new `GET /api/transactions` endpoint following the exact structural conventions of `ImportEndpoints.cs` (grouped under `/api`, `GetUserId` scoping, record DTOs), returning a page of transactions ordered newest-first with a deterministic tiebreak, plus a `hasMore` flag computed from a total count. Frontend second (Phase 2): fetch the first page in `home.tsx`'s `clientLoader` (so it's naturally fresh on every navigation to `/`), render the list with sign-colored amounts and "Uncategorized" labels using `import.tsx`'s row styling as the base, and add client-side state for the "Load more" button to fetch and append subsequent pages via `apiFetch`.

## Phase 1: Backend transaction list endpoint

### Overview

Add `GET /api/transactions?skip=&take=`, scoped to the current user, ordered newest-first, paginated, with integration tests.

### Changes Required:

#### 1. Transaction list contracts

**File**: `MyFinances/backend/Transactions/TransactionContracts.cs` (new)

**Intent**: Define the wire contract for a page of transaction history, following the same plain-`record` convention as `ImportContracts.cs`.

**Contract**:
```csharp
public record TransactionListItemDto(Guid Id, DateOnly Date, string Description, decimal Amount, Guid? CategoryId);
public record TransactionListResponseDto(IReadOnlyList<TransactionListItemDto> Items, bool HasMore);
```

#### 2. Transaction list endpoint

**File**: `MyFinances/backend/Transactions/TransactionEndpoints.cs` (new)

**Intent**: `MapTransactionEndpoints(this IEndpointRouteBuilder api)` extension method (mirroring `ImportEndpoints.MapImportEndpoints`), registering `GET /transactions` under a `/transactions` sub-group. Reads `skip` (default 0) and `take` (default 20, clamp to a max of 100) from the query string, scopes to `principal.GetUserId(userManager)`, orders by `Date` descending with `Id` descending as a deterministic tiebreak (no `CreatedAt` field exists, and `Date` alone isn't unique per user), and computes `HasMore` from a separate `CountAsync()` against the same filtered query (`skip + items.Count < totalCount`).

**Contract**: `GET /api/transactions?skip={int}&take={int}` → `200 TransactionListResponseDto`. Inherits `RequireAuthorization()` from the parent `/api` group — no explicit attribute needed, no antiforgery filter (GET).

#### 3. Register the endpoint group

**File**: `MyFinances/backend/Program.cs`

**Intent**: Wire the new endpoint group into the app, next to the existing ones.

**Contract**: Add `api.MapTransactionEndpoints();` after [`Program.cs:105`](../../../MyFinances/backend/Program.cs)'s `api.MapImportEndpoints();`.

#### 4. Shared test client helper

**File**: `MyFinances/backend/Tests/TestClientHelpers.cs` (new)

**Intent**: `CreateAuthenticatedClientAsync` is currently `private static` inside `ImportEndpointsTests.cs:24` — not callable from a new test file. Extract it into a small shared internal static class so both `ImportEndpointsTests` and the new `TransactionEndpointsTests` call the same helper instead of duplicating it. `AuthApiFactory` itself ([`AuthEndpointsTests.cs:22`](../../../MyFinances/backend/Tests/AuthEndpointsTests.cs)) is already `public` and needs no change. `GetAntiforgeryTokenAsync` stays in `ImportEndpointsTests.cs` — the new read-only GET endpoint's tests don't need it.

**Contract**: `TestClientHelpers.CreateAuthenticatedClientAsync(AuthApiFactory factory)` returns an authenticated `HttpClient`, with `ImportEndpointsTests.cs`'s call sites updated to use it.

#### 5. Integration tests

**File**: `MyFinances/backend/Tests/TransactionEndpointsTests.cs` (new)

**Intent**: Follow `ImportEndpointsTests.cs`'s conventions — use `AuthApiFactory` + the shared `TestClientHelpers.CreateAuthenticatedClientAsync` (item 4 above), seed transactions directly via a scoped `AppDbContext`, and use the `MethodUnderTest_Scenario_ExpectedResult` naming convention.

**Contract**: Cover: unauthenticated request returns 401; a user only sees their own transactions (not another user's); results are ordered newest-first; `skip`/`take` paginate correctly; `HasMore` is `true` when more rows exist beyond the current page and `false` on the last page; an empty result set returns `Items: []`, `HasMore: false`.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- All backend tests pass: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- `GET /api/transactions` (with a valid session cookie) via Swagger UI (`/swagger`) returns transactions for the logged-in test account, newest-first.
- Confirm a second user's session never sees the first user's transactions.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend dashboard integration

### Overview

Fetch and render the transaction list on the dashboard, with "Load more" pagination, sign-colored amounts, and an "Uncategorized" label — while preserving the existing empty-state placeholder when the user has zero transactions.

### Changes Required:

#### 1. Transaction type and initial fetch

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Add a `Transaction` interface mirroring `TransactionListItemDto` (with a `// Mirrors the backend's TransactionContracts.cs` comment, matching `import.tsx:12-13`'s convention). Extend `clientLoader` to also fetch the first page (`apiFetch<TransactionListResponseDto>("/transactions?skip=0&take=20")`) when the user is authenticated, and return both `user` and the transaction page from the loader so the page is naturally refetched on every navigation to `/` (satisfying the "refresh after import" decision with zero extra code).

**Contract**: `clientLoader` return shape becomes `{ user: ... | null, transactions: TransactionListResponseDto | null }`. If the transaction fetch fails, catch and return `transactions: null` distinctly from `user` (so a transaction-fetch failure doesn't break login state) — the component below turns `null` into an inline error message rather than crashing.

#### 2. Transaction list rendering, load-more state, and styling

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Replace the static empty-state block ([`home.tsx:128-133`](../../../MyFinances/frontend/app/routes/home.tsx)) with: the existing empty-state message + CTA when there are zero transactions; otherwise a list of rows (date, description, amount, category) below the welcome heading, plus a "Load more" button when `hasMore` is true. Widen the authenticated container beyond the current `max-w-[300px]` ([`home.tsx:121`](../../../MyFinances/frontend/app/routes/home.tsx)) to accommodate the list, following `import.tsx:182`'s pattern of toggling a wider max-width when list content is present.

Row layout follows `import.tsx:305-311`'s `flex items-center justify-between` structure, extended with a category segment and sign-based amount coloring (negative → red, positive → green/emerald, matching the existing dark-theme palette). A `CategoryId == null` row renders a muted "Uncategorized" label instead of a category name (no category names exist until S-03).

"Load more" is local component state (`items`, `hasMore`, `loadingMore`, `loadMoreError`) seeded from the loader's initial page; `skip` for the next request is derived from `items.length`, not tracked separately; clicking it calls `apiFetch<TransactionListResponseDto>(`/transactions?skip=${items.length}&take=20`)`, appends the returned items, updates `hasMore`, and follows `import.tsx`'s convention of a text-swapped button label ("Load more" → "Loading…") plus a `disabled` state while in flight — no spinner component exists to reuse. On failure, show an inline `text-sm text-red-600` message ([`import.tsx`](../../../MyFinances/frontend/app/routes/import.tsx) convention) below the button rather than losing the already-loaded rows.

**Contract**: No new route entry in `routes.ts` — this is all within `home.tsx`'s existing authenticated branch.

### Success Criteria:

#### Automated Verification:

- Frontend type checks pass: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- A user with zero transactions still sees the original "you haven't imported any transactions yet" message and CTA — no regression.
- A user with transactions sees them listed newest-first, amounts colored by sign, uncategorized rows labeled "Uncategorized".
- With more than 20 transactions, "Load more" appends the next page and disappears once all transactions are loaded.
- After importing a new statement on `/import` and navigating back to `/`, the new transactions appear without a manual refresh.
- Dark mode and responsive layout look correct (matches existing `dark:`-class convention throughout the dashboard).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- None planned beyond the integration tests below — this feature has no isolated business logic worth unit-testing separately from the endpoint behavior.

### Integration Tests:

- Backend: see Phase 1's `TransactionEndpointsTests.cs` scope above (auth, user-scoping, ordering, pagination boundaries, empty state).

### Manual Testing Steps:

1. Log in as the shared test account (see `reference_myfinances_test_account.md` memory) with zero transactions — confirm the empty-state placeholder still shows.
2. Import an mBank statement with more than 20 transactions via `/import`.
3. Navigate back to `/` — confirm the list shows the newest 20 transactions, amounts colored by sign, all labeled "Uncategorized".
4. Click "Load more" — confirm the next page appends and the button disappears once exhausted.
5. Log in as a second test account with no transactions imported — confirm it sees no transactions from the first account.

## Performance Considerations

None beyond the pagination itself — page size is capped at 100 server-side regardless of client-requested `take`, preventing an unbounded query.

## Open Risks & Assumptions

- Offset-based pagination (`skip`/`take`) can drift if a transaction with a newer date is inserted (e.g. imported in another browser tab) between two "Load more" clicks — the shift can cause an already-seen row to reappear on the next page. Accepted as a known limitation for this solo, single-user MVP (no multi-tenant/sharing per PRD); revisit with keyset/cursor pagination if this ever becomes multi-session or multi-device concurrent usage.

## Migration Notes

None — no schema changes; this phase is purely additive (new endpoint, new frontend rendering) over the existing `Transactions` table from S-01.

## References

- Similar implementation: [`ImportEndpoints.cs`](../../../MyFinances/backend/Import/ImportEndpoints.cs), [`ImportContracts.cs`](../../../MyFinances/backend/Import/ImportContracts.cs), [`import.tsx`](../../../MyFinances/frontend/app/routes/import.tsx)
- Roadmap: `context/foundation/roadmap.md` (S-09)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend transaction list endpoint

#### Automated

- [ ] 1.1 Backend builds: `dotnet build`
- [ ] 1.2 All backend tests pass: `dotnet test`

#### Manual

- [ ] 1.3 `GET /api/transactions` via Swagger returns the logged-in user's transactions, newest-first
- [ ] 1.4 A second user's session never sees the first user's transactions

### Phase 2: Frontend dashboard integration

#### Automated

- [ ] 2.1 Frontend type checks pass: `npm run typecheck`
- [ ] 2.2 Frontend builds: `npm run build`

#### Manual

- [ ] 2.3 Zero-transaction user still sees the original empty-state message and CTA
- [ ] 2.4 Transactions render newest-first, amounts colored by sign, uncategorized rows labeled "Uncategorized"
- [ ] 2.5 "Load more" appends the next page and disappears once all transactions are loaded
- [ ] 2.6 New transactions appear after import + navigation back to `/`, with no manual refresh
- [ ] 2.7 Dark mode and responsive layout look correct
