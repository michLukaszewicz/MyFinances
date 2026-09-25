# Categorization Queue (S-03) Implementation Plan

## Overview

Implement the categorization queue: the user works through their imported/manually-entered
transactions one at a time, assigning a category from a fixed list (FR-007, no
auto-suggestion), internal transfers between the user's own known accounts get auto-flagged
by a detection heuristic with manual override (FR-009), and any transaction — categorized or
not — can be re-categorized at any time (FR-015).

## Current State Analysis

- `MyFinances/backend/Transactions/Transaction.cs:1-31` already carries a nullable
  `CategoryId` placeholder (`Transaction.cs:30`, comment: "categorization lands in a later
  slice") but no `Category` navigation property, no FK configuration in `AppDbContext`, and no
  `Category` entity exists anywhere in the backend.
- `MyFinances/backend/Transactions/Account.cs:1-15` is a real per-user account entity
  (`Id`, `UserId`, `BankName`, `AccountNumber`) landed by S-10 (account-management, merged).
  Its header comment (`Account.cs:4-5`) reads: *"Used as the 'known accounts' input to S-03's
  transfer-detection heuristic and as an account picker for manual entry (S-02 follow-up);
  this slice only stores and CRUDs it."* — confirming this is the intended input to the
  transfer heuristic built in this plan.
- `Transaction.AccountId` (`Transaction.cs:12`) is a required FK to `Account`, replacing the
  old free-text `Bank` field; `Transaction.Amount` (`Transaction.cs:20`) is a signed `decimal`
  with no separate direction field.
- No `IsInternalTransfer`-style flag exists on `Transaction` yet — this plan adds it.
- Endpoint modules follow one convention: `public static class XEndpoints` with
  `public static void MapXEndpoints(this IEndpointRouteBuilder api)`, registered in
  `Program.cs:103-107` (`api.MapAuthEndpoints(); api.MapImportEndpoints();
  api.MapAccountEndpoints();`). Every user-scoped query resolves the user id via
  `ClaimsPrincipalExtensions.GetUserId` (`Transactions/ClaimsPrincipalExtensions.cs:9-21`) and
  filters with `.Where(x => x.UserId == userId)`. Mutating routes add
  `.AddEndpointFilter(RequireValidAntiforgery)`, where each module keeps its own private copy
  of that filter method (see `AccountEndpoints.cs:147-160`) rather than sharing one.
- `AppDbContext.cs:10-46` has `DbSet<Transaction>`, `DbSet<ImportBatch>`,
  `DbSet<Account>` — no `Categories` DbSet yet. Latest migration is
  `20260925120715_LinkTransactionsToAccounts`.
- S-01's dedup flow (`Import/DedupHash.cs`, `ImportEndpoints.cs`) establishes a precedent for
  "detect a candidate match deterministically, but only commit it after either an automatic
  rule or an explicit user decision" — useful as a pattern analogy, but this plan does not
  touch any import-module file (see Key Discoveries below on sibling-change collision
  avoidance).
- Frontend: `MyFinances/frontend/app/routes.ts:1-9` registers routes as
  `route("<segment>", "routes/<file>.tsx")`, one line per route, no other file touched.
  `MyFinances/frontend/app/routes/settings.tsx` (S-10) is the closest existing pattern for a
  new authenticated CRUD-ish page: `clientLoader` auth guard hitting `/api/auth/me`,
  `apiFetch`/`ApiError` from `MyFinances/frontend/app/lib/api.ts:34-50`, a DTO interface
  commented "Mirrors the backend's `X`Contracts.cs `X`Dto", an `extractErrorMessage` helper
  reading `ProblemDetails.title`, and PascalCase JSON request bodies to match the C# model
  binder. Nav links are centralized in `MyFinances/frontend/app/components/AppHeader.tsx:26-46`
  (`Dashboard` / `Import transactions` / `Settings`, one `<Link>` per entry) — every
  authenticated page renders `<AppHeader authenticated />` and gets the nav for free.
- `MyFinances/backend/Transactions/TransactionContracts.cs` and
  `MyFinances/backend/Transactions/TransactionEndpoints.cs` do **not** exist in this worktree.
  They belong to the in-flight sibling change S-09 (transaction-history-view, branch
  `claude/10x-transaction-history-view-02cb0c`), not yet merged. S-02
  (manual-transaction-entry) already collided with S-09 by recreating those same two files on
  its own branch. `MyFinances/frontend/app/routes/home.tsx` (146 lines, minimal "Welcome back"
  placeholder) is also S-09's likely edit target.

### Key Discoveries:

- `Account.cs:4-5`'s comment is written as a direct hand-off note to this slice — the
  transfer-detection heuristic's match predicate below uses `Account.Id` (via
  `Transaction.AccountId`) as the "different known account" signal, not a free-text bank-name
  comparison.
- `Transaction.CategoryId` already exists as a schema placeholder; this plan only needs to add
  the `Category` entity, its FK/nav wiring, and a migration — not a new column on `Transaction`
  for categorization itself.
- No category-management FR exists in the PRD (no "user creates/edits categories" story) —
  FR-007 says "select ... from the full category list", implying a fixed, pre-existing list.
  This plan seeds a fixed global category list rather than building category CRUD.

## Desired End State

A logged-in user visiting `/categorize` sees an "up next" card for the first
not-yet-handled transaction (uncategorized, not flagged as an internal transfer), can assign
it a category or mark/unmark it as an internal transfer, and advances automatically to the
next one. A second section lists every already-handled transaction (categorized or
transfer-flagged) with the same controls, so any of them can be changed at any time (FR-015).
Internal transfers between the user's own accounts are pre-flagged automatically the first
time they're detected, without blocking manual override.

**Verification**: `dotnet test` passes in `MyFinances/backend/Tests`; manually, import two
mBank statements for two different accounts containing a matching transfer pair, confirm both
legs show up pre-flagged as internal transfer in the queue's handled section, categorize a
few ordinary transactions through the "up next" card, and re-categorize one from the handled
list to confirm FR-015.

## What We're NOT Doing

- Category management (create/rename/delete/reorder categories) — the category list is a
  fixed, seeded set for this slice; no FR requires user-managed categories.
- Batch/bulk categorization (e.g. "categorize all Biedronka transactions as Groceries") — PRD
  Socratic note under FR-007 explicitly keeps the one-transaction-at-a-time flow.
- Optimal (e.g. min-cost bipartite) transfer-pair matching when more than one candidate pair
  shares the same date/amount — a greedy first-match is used; manual override always available.
- Any change to `MyFinances/backend/Transactions/TransactionContracts.cs`,
  `TransactionEndpoints.cs`, or `MyFinances/frontend/app/routes/home.tsx` — those belong to
  S-09, not yet merged into this branch.
- Wiring the categorization queue into the donut chart or spend totals — that's S-04, which
  consumes `CategoryId`/`IsInternalTransfer` but is out of scope here.
- Re-running transfer detection retroactively as a standalone admin/maintenance action — it
  runs inline on every queue/handled-list fetch instead (see Phase 2).

## Implementation Approach

Add a `Category` entity and the `IsInternalTransfer`/`TransferFlagManuallySet` fields to
`Transaction` in one migration (Phase 1). Build a new, self-contained
`MyFinances/backend/Categorization/` module — contracts, endpoints, and a transfer-detection
service — registered the same way `AccountEndpoints`/`ImportEndpoints` are, so no existing
backend file outside `Transaction.cs`/`AppDbContext.cs` is touched (Phase 2). Build a new
`categorize.tsx` frontend route plus one nav-link edit to the shared `AppHeader.tsx` (Phase 3).
Transfer detection runs lazily and idempotently inside the queue/handled-list GET handlers —
it only evaluates transactions the user hasn't manually touched yet
(`TransferFlagManuallySet == false`), so it never needs to hook into the import-commit path
owned by another module.

## Critical Implementation Details

**State sequencing**: Transfer detection must run *before* the queue/handled-list query
executes within the same request (not as a separate background pass), and must only mutate
rows where `TransferFlagManuallySet == false`. Once a user PUTs an explicit
`IsInternalTransfer` value for a transaction, `TransferFlagManuallySet` flips to `true`
permanently for that row, and all future detection passes skip it — otherwise a user's manual
"this is not a transfer" correction would be silently re-flagged on the next queue load.

## Phase 1: Category entity + Transaction fields + migration

### Overview

Adds the `Category` entity (seeded, fixed list), wires `Transaction.CategoryId` to it, and
adds the two new boolean fields needed for transfer-flag tracking.

### Changes Required:

#### 1. Category entity

**File**: `MyFinances/backend/Categorization/Category.cs` (new)

**Intent**: A fixed, seeded category a transaction can be assigned to. Not user-owned or
user-editable in this slice.

**Contract**: `public class Category { public Guid Id; public required string Name; public
int SortOrder; }`. `SortOrder` drives the order categories are returned in for the picker UI.

#### 2. Transaction entity wiring

**File**: `MyFinances/backend/Transactions/Transaction.cs`

**Intent**: Give the existing `CategoryId` placeholder a real navigation property, and add the
two fields the transfer-detection heuristic needs to track its own state.

**Contract**: Add `public Category? Category { get; set; }` alongside `CategoryId` (line 30
area). Add `public bool IsInternalTransfer { get; set; }` (default `false`) and `public bool
TransferFlagManuallySet { get; set; }` (default `false`).

#### 3. EF configuration + seed data

**File**: `MyFinances/backend/AppDbContext.cs`

**Intent**: Register the new `DbSet<Category>`, configure the `Transaction.Category` FK as
optional with `DeleteBehavior.Restrict` (matching the existing `Account` FK convention at
`AppDbContext.cs:29-39`), and seed the fixed category list.

**Contract**: `DbSet<Category> Categories => Set<Category>();`. In `OnModelCreating`:
`builder.Entity<Transaction>().HasOne(t => t.Category).WithMany().HasForeignKey(t =>
t.CategoryId).OnDelete(DeleteBehavior.Restrict);` and `builder.Entity<Category>().HasData(...)`
with a fixed list of 12 categories (Groceries, Dining & Takeout, Transport, Housing &
Utilities, Health, Shopping, Entertainment, Travel, Subscriptions, Income, Fees & Charges,
Other), each with a hard-coded `Guid` (deterministic, so the seed is stable across
environments) and increasing `SortOrder`.

#### 4. Migration

**File**: `MyFinances/backend/Migrations/<timestamp>_AddCategorization.cs` (generated via
`dotnet ef migrations add AddCategorization`)

**Intent**: Persist the new table, FK, two new columns, and seed rows.

**Contract**: Standard EF migration; no manual SQL needed.

### Success Criteria:

#### Automated Verification:

- Migration applies cleanly: `dotnet ef database update` (or `dotnet build` compiles the
  generated migration without errors)
- Backend builds: `dotnet build` in `MyFinances/backend`
- Existing tests still pass: `dotnet test` in `MyFinances/backend/Tests`

#### Manual Verification:

- Inspect the seeded `Categories` table after migration (via `psql`/Neon console) — 12 rows,
  correct `SortOrder`

---

## Phase 2: Backend categorization module

### Overview

New `Categorization` endpoint module: list categories, list the queue, list handled
transactions, and categorize/flag a transaction — plus the transfer-detection service that
runs inline on the list endpoints.

### Changes Required:

#### 1. Contracts

**File**: `MyFinances/backend/Categorization/CategorizationContracts.cs` (new)

**Intent**: Request/response shapes for the categorization endpoints, following the
`AccountContracts.cs` convention (plain records, one file per module).

**Contract**: `CategoryDto(Guid Id, string Name)`; `TransactionQueueItemDto(Guid Id, DateOnly
Date, string Description, decimal Amount, string BankName, string AccountNumber, Guid?
CategoryId, string? CategoryName, bool IsInternalTransfer)`; `CategorizeRequest(Guid?
CategoryId, bool? IsInternalTransfer)` — either field may be omitted/null to leave that aspect
unchanged.

#### 2. Transfer-detection service

**File**: `MyFinances/backend/Categorization/TransferDetectionService.cs` (new)

**Intent**: Given a user's transactions, auto-flag internal-transfer pairs the user hasn't
already manually decided on, using `Account.Id` (not bank name) as the "different known
account" signal per the `Account.cs:4-5` hand-off comment.

**Contract**: `Task DetectAsync(Guid userId, AppDbContext db)`. Match predicate — two
transactions `t1`/`t2` for the same user are treated as one internal-transfer pair when all of:
`t1.UserId == t2.UserId == userId`, `t1.AccountId != t2.AccountId`, `t1.Amount == -t2.Amount`,
and `Math.Abs(t1.Date.DayNumber - t2.Date.DayNumber) <= 2` (a small tolerance for the two
accounts' banks posting the same transfer on slightly different calendar days — documented as
an assumption in the plan brief, since the PRD doesn't specify a window). Only candidates
where `TransferFlagManuallySet == false` are considered; a transaction already matched in this
pass is excluded from further pairing (first-match-wins, no optimal matching). Matched pairs
get `IsInternalTransfer = true` (leaving `TransferFlagManuallySet` at its existing value —
this is the automatic path, not a manual one).

```csharp
// Illustrative match predicate (exact LINQ shape left to the implementer):
bool IsTransferPair(Transaction a, Transaction b) =>
    a.AccountId != b.AccountId &&
    a.Amount == -b.Amount &&
    Math.Abs(a.Date.DayNumber - b.Date.DayNumber) <= TransferDateToleranceDays;
```

#### 3. Endpoints

**File**: `MyFinances/backend/Categorization/CategorizationEndpoints.cs` (new)

**Intent**: Expose categories, the queue, the handled list, and the write path, following the
`MapXEndpoints(this IEndpointRouteBuilder api)` convention (`AccountEndpoints.cs:13-15`).

**Contract**:
- `GET /api/categorization/categories` → `CategoryDto[]`, ordered by `SortOrder`.
- `GET /api/categorization/queue` → runs `TransferDetectionService.DetectAsync` for the
  current user first, then returns `TransactionQueueItemDto[]` where `CategoryId == null &&
  IsInternalTransfer == false`, ordered by `Date` then `Id`.
- `GET /api/categorization/handled` → same detection pass, then returns
  `TransactionQueueItemDto[]` where `CategoryId != null || IsInternalTransfer == true`,
  ordered by `Date` descending (FR-015's re-categorize-any-time surface).
- `PUT /api/categorization/transactions/{id}` (body: `CategorizeRequest`, filtered by
  `.Where(t => t.UserId == userId)` like `AccountEndpoints.cs:83`) → applies `CategoryId` if
  provided; if `IsInternalTransfer` is provided, sets it and sets
  `TransferFlagManuallySet = true`. Returns the updated `TransactionQueueItemDto`. Uses the
  module's own private `RequireValidAntiforgery` filter copy (matching
  `AccountEndpoints.cs:147-160`'s pattern rather than sharing one).

#### 4. Registration

**File**: `MyFinances/backend/Program.cs`

**Intent**: Wire the new module in.

**Contract**: Add `api.MapCategorizationEndpoints();` after `api.MapAccountEndpoints();`
(`Program.cs:107`).

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds
- New unit tests for `TransferDetectionService` pass: same-user/different-account/opposite-
  amount/within-tolerance pairs get flagged; different-user, same-account, non-opposite-amount,
  and out-of-tolerance-date pairs do not; a manually-set flag (`TransferFlagManuallySet ==
  true`) is never touched by detection
- New integration tests in `MyFinances/backend/Tests/CategorizationEndpointsTests.cs`
  (following `AccountEndpointsTests.cs`'s `WebApplicationFactory` pattern) pass: queue excludes
  categorized/transfer-flagged transactions, `PUT` sets category and persists across a
  subsequent `GET`, `PUT` with `IsInternalTransfer` persists and survives a later detection
  pass, cross-user isolation (a transaction from another user never appears)
- `dotnet test` passes in full

#### Manual Verification:

- Via Swagger UI (`/swagger`), import two statements on two different S-10 accounts with a
  matching transfer pair, call `GET /api/categorization/queue`, confirm the pair is absent
  (auto-flagged) and appears instead in `GET /api/categorization/handled` with
  `isInternalTransfer: true`
- `PUT` a transaction with `IsInternalTransfer: false` to override an auto-flagged transfer,
  re-fetch the queue, confirm it now appears there instead

---

## Phase 3: Frontend categorization page

### Overview

New `/categorize` route: an "up next" single-item card for the queue, and a "handled"
section listing every already-categorized/flagged transaction for FR-015 re-categorization,
plus a nav link.

### Changes Required:

#### 1. Route registration

**File**: `MyFinances/frontend/app/routes.ts`

**Intent**: Register the new page.

**Contract**: Add `route("categorize", "routes/categorize.tsx"),` alongside the existing
entries (`routes.ts:7-8`).

#### 2. Categorization page

**File**: `MyFinances/frontend/app/routes/categorize.tsx` (new)

**Intent**: Let the user work through the queue one transaction at a time (category `<select>`
+ "internal transfer" checkbox + Save, advancing to the next queue item after each save), and
re-categorize/re-flag any already-handled transaction inline below. Follows `settings.tsx`'s
shape: `clientLoader` auth guard hitting `/api/auth/me`, DTOs commented as mirroring
`CategorizationContracts.cs`, `apiFetch`/`extractErrorMessage` for calls and errors.

**Contract**: On mount, load `/categorization/categories`, `/categorization/queue`, and
`/categorization/handled`. "Up next" shows `queue[0]` (or an empty-state message when the
queue is empty); saving `PUT`s `/categorization/transactions/{id}` then reloads the queue so
the next item surfaces. The handled list renders one row per handled transaction with the same
category `<select>`/checkbox controls, each independently PUT-able.

#### 3. Nav link

**File**: `MyFinances/frontend/app/components/AppHeader.tsx`

**Intent**: Make the new page discoverable from every authenticated page.

**Contract**: Add one `<Link to="/categorize">Categorize</Link>` entry between the existing
`Import transactions` and `Settings` links (`AppHeader.tsx:34-45`), same className as its
neighbors.

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes in `MyFinances/frontend`
- `npm run build` succeeds in `MyFinances/frontend`

#### Manual Verification:

- Log in, navigate to `/categorize` via the new nav link, categorize a transaction from "up
  next", confirm it moves to the handled list and the next queue item surfaces automatically
- Toggle the internal-transfer checkbox on a handled transaction and confirm the change
  persists after a page reload
- Confirm an uncategorized transaction stays in the queue indefinitely if left untouched (no
  forced categorization), per US-01's acceptance criteria

---

## Testing Strategy

### Unit Tests:

- `TransferDetectionService` match predicate: same-user opposite-account opposite-amount
  within-tolerance pair matches; cross-user, same-account, non-opposite-amount, and
  out-of-tolerance-date pairs do not; manually-set flags are never overwritten; a transaction
  already matched in one pass isn't reused for a second pair.

### Integration Tests:

- Full queue → categorize → handled-list round trip via `CategorizationEndpointsTests.cs`
  (`WebApplicationFactory`-based, following `AccountEndpointsTests.cs`).
- Cross-user isolation on every endpoint.

### Manual Testing Steps:

1. Import two mBank statements on two different accounts (via S-10 + S-01) containing a real
   matching transfer pair; confirm both legs are auto-flagged and excluded from the queue.
2. Categorize several ordinary transactions one at a time through the "up next" card.
3. Re-categorize an already-categorized transaction from the handled list (FR-015).
4. Override an auto-flagged transfer back to "not a transfer" and confirm it then appears in
   the queue for categorization.

## Performance Considerations

Transfer detection runs an O(n²) in-memory pairing pass per queue/handled-list request over
only the current user's transactions with `TransferFlagManuallySet == false` — acceptable at
MVP's expected per-user transaction volume (hundreds, not tens of thousands); revisit if that
assumption changes.

## Migration Notes

Additive migration only (new table + two new nullable-default-false columns on an existing
table) — no backfill needed since no `Transaction` rows currently have `IsInternalTransfer`
semantics to preserve.

## References

- `MyFinances/backend/Transactions/Account.cs:4-5` — the transfer-detection heuristic
  hand-off comment this plan implements against.
- `MyFinances/backend/Transactions/AccountEndpoints.cs`, `AccountContracts.cs` — endpoint
  module pattern followed by `Categorization/`.
- `MyFinances/frontend/app/routes/settings.tsx` — frontend page pattern followed by
  `categorize.tsx`.
- `MyFinances/backend/Tests/AccountEndpointsTests.cs` — integration test pattern followed by
  `CategorizationEndpointsTests.cs`.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Category entity + Transaction fields + migration

#### Automated

- [x] 1.1 Migration applies cleanly — e3e3447
- [x] 1.2 Backend builds — e3e3447
- [x] 1.3 Existing tests still pass — e3e3447

#### Manual

- [x] 1.4 Seeded Categories table inspected — 12 rows, correct SortOrder — e3e3447

### Phase 2: Backend categorization module

#### Automated

- [x] 2.1 Backend builds
- [x] 2.2 TransferDetectionService unit tests pass
- [x] 2.3 CategorizationEndpointsTests integration tests pass
- [x] 2.4 Full `dotnet test` passes (57/57)

#### Manual

- [ ] 2.5 Matching transfer pair auto-flagged and excluded from queue (verified via Swagger) — skipped: starting a second local backend instance on this shared dev machine collided with the sibling S-09 preview on port 5007 (both worktrees' launchSettings default to 5007); killed the stray process immediately to avoid disrupting that live manual-testing session. Needs a human pass with a dedicated port/profile.
- [ ] 2.6 Manual override of an auto-flagged transfer persists and moves it back to the queue — same reason as 2.5, not verified live.

### Phase 3: Frontend categorization page

#### Automated

- [ ] 3.1 `npm run typecheck` passes
- [ ] 3.2 `npm run build` succeeds

#### Manual

- [ ] 3.3 Categorize a transaction from "up next"; it moves to handled and the queue advances
- [ ] 3.4 Toggle internal-transfer checkbox on a handled transaction; persists after reload
- [ ] 3.5 Uncategorized transaction stays in queue indefinitely if left untouched
