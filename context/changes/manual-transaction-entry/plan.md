# Manual Transaction Entry Implementation Plan

## Overview

Implement S-02 (FR-010): the user can manually add, edit, and delete transactions. A manually-entered
transaction is recognized as the same one if it later appears in an imported statement (and vice
versa), by reusing S-01's existing dedup hash unchanged. This plan adds `POST`/`PUT`/`DELETE
/transactions` endpoints and a form on the home page's transaction list — no new entity, no
migration, no new frontend page.

## Current State Analysis

`Transaction` (`MyFinances/backend/Transactions/Transaction.cs:8-43`) already has everything this
slice needs: `ImportBatchId` is nullable specifically "for a later slice [that] will support manual
entries not tied to any import" (line 26), and `Hash` is deliberately non-unique so a kept duplicate
is a legitimate stored row (lines 6-7). `TransactionEndpoints.cs` currently only exposes `GET /`
(`TransactionEndpoints.cs:20-54`, paginated, user-scoped); no write endpoint exists on this module.

`DedupHash.ComputeHash(userId, date, amount, description, accountId)`
(`MyFinances/backend/Import/DedupHash.cs:11-24`) is a pure static method with no dependency on the
import pipeline — every input it needs (date, amount, description, the chosen account's id) is
already present on a manually-entered transaction, so it is directly reusable with no adaptation.
The one behavioral precedent to preserve: a hash collision is never auto-resolved by adding more
fields to the hash — it's always routed through a human decision (`context/changes/mbank-import-with-dedup/research.md:159-167`, rejected adding a running-balance disambiguator specifically
because manual entries have no balance field).

`AccountEndpoints.cs` (`MyFinances/backend/Transactions/AccountEndpoints.cs`) is the concrete
POST/PUT/DELETE precedent to follow: `MapXEndpoints` extension method, `RequireValidAntiforgery` as
a private per-module filter (not shared), 409-on-conflict via `Results.Problem`, self-owned FK-safety
check before delete. `CategorizationEndpoints.cs:69-113`'s `PUT /categorization/transactions/{id}`
shows the "validate referenced `CategoryId` exists, 400 if not" pattern this plan reuses verbatim.

On the frontend, `settings.tsx` is the complete add/edit/delete form precedent (toggle-open form,
`startAdd`/`startEdit`/`closeForm`, `extractErrorMessage` reading `ProblemDetails.title`, two-step
inline delete confirm — no modal component exists anywhere in the app). `home.tsx:78-253` already
renders the transaction list this plan adds actions to; `categorize.tsx:174-192` shows the existing
category-`<select>` pattern fed by `GET /categorization/categories`. `GET /accounts` (`AccountEndpoints.cs:28-42`) already returns `AccountDto{Id,BankName,AccountNumber}` — `Account.cs:4-5`'s
own comment names this exact use as "an account picker for manual entry (S-02 follow-up)".

### Key Discoveries:

- `Transaction.cs:26` — `ImportBatchId` is nullable specifically for this slice; a manually-created row just sets it to `null`.
- `DedupHash.cs:11-24` — hash inputs (`userId, date, amount, description, accountId`) are all present on a manual entry; no new hash logic needed.
- `ImportEndpoints.cs:56-87` — the existing side-by-side duplicate-review pattern (`ExistingTransactionDto`, `Import/ImportContracts.cs:8`) is the shape to reuse for the manual-entry duplicate warning, not a new contract.
- `AccountEndpoints.cs:44-160` — full CRUD + antiforgery-filter + 409-conflict precedent to mirror.
- `CategorizationEndpoints.cs:87-93` — `CategoryId` existence validation (400 "Unknown category.") to mirror for the new write endpoints.
- `settings.tsx` (whole file) — the frontend add/edit/delete form precedent to mirror exactly.
- `home.tsx:33-46,78-253` — the `Transaction`/`TransactionListResponseDto` interfaces and the list rendering this plan extends with per-row actions.

## Desired End State

A logged-in user with at least one bank account (S-10) sees an "Add transaction" control above
their transaction list on the home page. They can add a transaction (date, description, signed
amount, account, category — all required); edit or delete any transaction in the list, whether it
was imported or entered manually. If the entered data collides (by hash) with an existing
transaction, the form shows the existing transaction's date/description/amount and an "Add anyway"
/ "Save anyway" confirmation instead of silently inserting or silently blocking. A manually-entered
transaction is later recognized as a duplicate by `/import/parse` exactly the same way an imported
one is, because it carries the same hash.

Verification: log in, add a transaction, see it appear in the home list; edit it and see the change
persist; delete it and see it disappear; re-add the same date/description/amount/account and see
the duplicate warning; import a statement containing a transaction matching one entered manually
and see it flagged as a duplicate in the import review screen.

## What We're NOT Doing

- No new `Transaction` fields, no EF Core migration — every field this slice needs already exists.
- No `IsInternalTransfer` control in the manual-entry form — it defaults to `false` /
  `TransferFlagManuallySet = false` on create, same as an imported row, and is set later via the
  existing categorization queue (`CategorizationEndpoints.cs:69-113`), which already has its own
  UI for this.
- No change to `GET /transactions`'s pagination/ordering contract beyond adding `AccountId` to the
  response item — existing consumers keep working unchanged.
- No change to `ImportEndpoints.cs`, `DedupHash.cs`, or the import review/commit flow — this plan
  is pure consumption of the existing hash, not a modification of it.
- No soft-delete, undo, or audit trail for deleted transactions — `DELETE` is a hard delete, matching
  `AccountEndpoints.cs`'s convention; this also means deleting a transaction that was part of a past
  import does not adjust that `ImportBatch`'s stored summary counts (`ImportedCount`/
  `SkippedDuplicateCount`), which is an accepted, unchanged historical record.
- No new frontend route/page — the form lives inline on `home.tsx`, not a separate page.
- No shared/reusable dialog or form component extraction — following the existing precedent of
  page-local inline `useState` (`settings.tsx`, `import.tsx`), not introducing new frontend
  architecture.

## Implementation Approach

Add three endpoints to the existing `TransactionEndpoints.cs` module — `POST /transactions`,
`PUT /transactions/{id}`, `DELETE /transactions/{id}` — following `AccountEndpoints.cs`'s CRUD
shape exactly (per-module `RequireValidAntiforgery` filter, `Results.Problem` for conflicts,
user-scoped `FirstOrDefaultAsync` for edit/delete lookups). Both `POST` and `PUT` share one request
contract, `TransactionWriteRequest`, validate the referenced `AccountId`/`CategoryId` belong to the
user / exist (mirroring `CategorizationEndpoints.cs`'s category check), compute the hash via the
unmodified `DedupHash.ComputeHash`, and — unless the request carries `Force: true` — check for an
existing same-hash row (excluding the row being edited, for `PUT`) and return a `409` carrying the
existing transaction's snapshot instead of inserting/updating. `PUT` always recomputes and
overwrites `Hash` from the new field values but leaves `ImportBatchId` untouched, so an edited
imported row still recognizes future duplicates by its *current* data, not its original import
snapshot. `DELETE` is unconditional for any transaction the user owns, imported or manual alike —
no FK-safety check is needed first, since nothing else in the schema references `Transaction.Id`.

On the frontend, `home.tsx` gains an "Add transaction" button that opens an inline form (mirroring
`settings.tsx`'s `isFormOpen`/`editingId` toggle), sourcing its account `<select>` from `GET
/accounts` and its category `<select>` from `GET /categorization/categories` (both fetched once on
mount, same as `categorize.tsx`'s `loadAll`). Each transaction row gets "Edit" and "Delete" (Delete
via `settings.tsx`'s two-step inline confirm, no modal). A `409` response is parsed into a warning
banner showing the existing transaction's date/description/amount with an "anyway" button that
resubmits the same form values with `Force: true`.

## Phase 1: Backend write endpoints

### Overview

Add `POST`/`PUT`/`DELETE /transactions` to the existing `TransactionEndpoints.cs` module, reusing
`DedupHash` unmodified, with a `409`-plus-snapshot duplicate response.

### Changes Required:

#### 1. Request/response contracts

**File**: `MyFinances/backend/Transactions/TransactionContracts.cs`

**Intent**: Add the write-side contracts for create/edit, and extend the existing list item so the
frontend can prefill an edit form (which account a transaction belongs to) without a second
round-trip.

**Contract**:
- `TransactionWriteRequest(DateOnly Date, string Description, decimal Amount, Guid AccountId, Guid CategoryId, bool Force)` — one shape for both `POST` and `PUT`; `Force` defaults to `false` via the frontend always sending it explicitly (no server-side default needed on a record positional parameter that's always supplied).
- `TransactionDetailDto(Guid Id, DateOnly Date, string Description, decimal Amount, Guid AccountId, Guid CategoryId, string CategoryName)` — returned by `POST`/`PUT`.
- `DuplicateTransactionResponse(string Title, ExistingTransactionDto ExistingTransaction)` — the `409` body; reuses `MyFinances.Api.Import.ExistingTransactionDto` (`ImportContracts.cs:8`) via a `using MyFinances.Api.Import;` directive, rather than duplicating that shape.
- Extend `TransactionListItemDto` with one added field: `Guid AccountId` (last positional parameter, to avoid disturbing existing positional callers — check `TransactionEndpoints.cs:41-47`'s single construction site when adding it).

#### 2. Write endpoints

**File**: `MyFinances/backend/Transactions/TransactionEndpoints.cs`

**Intent**: `POST /` creates a transaction with `ImportBatchId = null`; `PUT /{id:guid}` edits any
transaction the user owns (imported or manual) and recomputes its hash; `DELETE /{id:guid}` removes
any transaction the user owns unconditionally. All three validate `AccountId` (must belong to the
caller) and `CategoryId` (must exist) the same way `CategorizationEndpoints.cs:87-93` validates
category, returning `400` with title `"Unknown account."` / `"Unknown category."` respectively.

**Contract**: Both `POST` and `PUT` compute `DedupHash.ComputeHash(userId, request.Date,
request.Amount, request.Description, request.AccountId)` and, when `request.Force` is `false`,
query `db.Transactions.Where(t => t.UserId == userId && t.Hash == hash)` (excluding `t.Id == id` on
`PUT`) — a match returns `Results.Json(new DuplicateTransactionResponse(...), statusCode: 409)`
instead of writing. `GET /` also gains `AccountId` in its existing `.Select(...)` projection
(`TransactionEndpoints.cs:41-47`). All three mutating routes share one `RequireValidAntiforgery`
filter, defined as a private method on this class (per-module copy, per
`context/changes/categorization-queue/plan.md:32-34`'s established convention — do not extract a
shared helper).

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- New tests in `Tests/TransactionEndpointsTests.cs` cover: `POST` creates a transaction with
  `ImportBatchId == null` and a hash matching `DedupHash.ComputeHash` on the same inputs; `POST`
  without `Force` against an existing hash returns `409` with the existing transaction's snapshot;
  `POST` with `Force: true` against an existing hash inserts anyway; `PUT` recomputes `Hash` from
  new field values while leaving `ImportBatchId` unchanged on a row seeded with one; `PUT` without
  `Force` against another row's hash returns `409`; `DELETE` removes a transaction regardless of
  whether `ImportBatchId` is set; all three endpoints 404/reject when the target/`AccountId` isn't
  scoped to the caller, and 400 when `CategoryId` doesn't exist; all three 401 without an auth
  cookie and 400 without a valid antiforgery token (mirroring `AccountEndpointsTests.cs`'s existing
  coverage shape for the equivalent cases).

#### Manual Verification:

- Via Swagger UI (`/swagger`) or a REST client: `POST /api/transactions` with a fresh transaction succeeds; repeating the exact same body returns `409` with the existing row's date/description/amount; repeating with `Force: true` inserts a second row with the same hash.
- `PUT /api/transactions/{id}` against a transaction seeded via a real mBank import (has `ImportBatchId` set) succeeds and the row's `ImportBatchId` is unchanged afterward (inspect via `GET /api/transactions`).
- `DELETE /api/transactions/{id}` against both a manually-created and an imported transaction succeeds in both cases.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 2: Frontend add/edit/delete UI

### Overview

Add an inline add/edit form and per-row edit/delete actions to `home.tsx`'s transaction list,
sourcing account and category options from their existing endpoints, with a duplicate-warning
confirmation step.

### Changes Required:

#### 1. Transaction list + form

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Add an "Add transaction" entry point above the list (mirroring `settings.tsx`'s
add-button-that-becomes-a-form pattern) and "Edit"/"Delete" actions on each row. The form collects
date, description, signed amount, an account `<select>` (from `GET /accounts`), and a category
`<select>` (from `GET /categorization/categories`, mirroring `categorize.tsx:178-191`'s markup) —
all four required, no `IsInternalTransfer` control. If the account list is empty when the user opens
"Add transaction", render a message directing them to Settings (`<Link to="/settings">`) instead of
the form fields.

**Contract**: Extend the local `Transaction` interface (`home.tsx:34-41`) with `accountId: string`
to match the backend's added `TransactionListItemDto.AccountId`. Local component state follows
`settings.tsx`'s shape: `isFormOpen`, `editingId`, one field per form input, `formError`,
`submitting`, plus a duplicate-specific `pendingDuplicate: { date, description, amount } | null` set
from a caught `409`'s `DuplicateTransactionResponse` body (via `extractErrorMessage`'s existing
`ApiError`-body-parsing pattern, extended to also surface `existingTransaction` when present).
`handleSubmit` posts/puts `TransactionWriteRequest` with `Force: false` first; on `409`, shows the
warning banner with an "anyway" button that resubmits the same values with `Force: true`. Delete
uses the identical `confirmingDeleteId` two-step inline confirm as `settings.tsx:225-251`, calling
`DELETE /transactions/{id}` then re-running the existing loader-equivalent fetch (`apiFetch<TransactionListResponseDto>` at the current page size) to refresh the list.

### Success Criteria:

#### Automated Verification:

- Frontend typechecks: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- With no accounts configured, opening "Add transaction" shows a message linking to Settings instead of a form.
- After adding an account in Settings, adding a transaction (date, description, signed amount, account, category) succeeds and the new row appears in the home list immediately, without a page reload.
- Editing an existing row (both a manually-added one and one from a prior import) pre-fills the form and saves changes.
- Deleting a row (both kinds) removes it from the list after the two-step confirm.
- Re-entering the same date/description/amount/account as an existing transaction shows the duplicate warning with the existing transaction's details; clicking "anyway" inserts/saves it regardless.
- Manually adding a transaction, then importing an mBank statement containing that same transaction, shows it flagged as a duplicate on the import review screen (cross-check against Phase 1's backend behavior, no separate import-side code change).

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `TransactionEndpointsTests.cs`: hash computation on create matches `DedupHash.ComputeHash` on
  identical inputs; `Force` bypasses the duplicate check on both create and edit; edit recomputes
  `Hash` and preserves `ImportBatchId`; delete works regardless of `ImportBatchId`; ownership/
  validation checks (account/category/transaction not found or not owned) return the correct status
  codes for all three endpoints.

### Integration Tests:

- End-to-end via `AuthApiFactory` (existing test infra): register → create account → create
  transaction → attempt duplicate without `Force` (expect 409) → retry with `Force` (expect insert)
  → edit it → delete it — one full-lifecycle test per `AccountEndpointsTests.cs`'s existing
  full-CRUD-lifecycle style.

### Manual Testing Steps:

1. Add a transaction with no existing accounts — verify the Settings-link message.
2. Add an account, then add a transaction — verify it appears in the home list.
3. Edit that transaction's amount — verify the new amount displays.
4. Delete it — verify it disappears.
5. Add a transaction, then add the identical one again — verify the duplicate warning shows the
   first transaction's snapshot, and "anyway" creates a second row.
6. Manually add a transaction, then import an mBank CSV containing the same transaction — verify
   the import review screen flags it as a duplicate against the manually-entered row.

## References

- Dedup mechanism: `MyFinances/backend/Import/DedupHash.cs:11-24`, `context/changes/mbank-import-with-dedup/plan.md`, `context/changes/mbank-import-with-dedup/research.md:159-167`
- Account CRUD precedent: `MyFinances/backend/Transactions/AccountEndpoints.cs`, `context/changes/account-management/plan.md`
- Categorization validation/UI precedent: `MyFinances/backend/Categorization/CategorizationEndpoints.cs:69-113`, `MyFinances/frontend/app/routes/categorize.tsx`
- Frontend form precedent: `MyFinances/frontend/app/routes/settings.tsx`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend write endpoints

#### Automated

- [x] 1.1 Backend builds: `dotnet build`
- [x] 1.2 Backend tests pass: `dotnet test`

#### Manual

- [ ] 1.3 POST creates a transaction; repeating the same body returns 409 with existing snapshot; Force: true inserts anyway
- [ ] 1.4 PUT against an imported transaction succeeds and leaves ImportBatchId unchanged
- [ ] 1.5 DELETE succeeds against both a manually-created and an imported transaction

### Phase 2: Frontend add/edit/delete UI

#### Automated

- [x] 2.1 Frontend typechecks: `npm run typecheck`
- [x] 2.2 Frontend builds: `npm run build`

#### Manual

- [ ] 2.3 No accounts configured shows Settings-link message instead of the form
- [ ] 2.4 Adding a transaction succeeds and appears in the home list without a page reload
- [ ] 2.5 Editing a manual and an imported row pre-fills and saves correctly
- [ ] 2.6 Deleting a manual and an imported row removes it after the two-step confirm
- [ ] 2.7 Duplicate warning shows existing transaction details; "anyway" inserts/saves regardless
- [ ] 2.8 A manually-entered transaction is flagged as a duplicate when later imported via mBank CSV
