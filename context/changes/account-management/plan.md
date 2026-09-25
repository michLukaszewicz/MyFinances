# Account Management Implementation Plan

## Overview

Implement S-10: the user can add, edit, and remove their own bank accounts (bank name + account number) via a new "Settings" page. This is the "user's own known accounts" concept FR-009 needs for internal-transfer detection (S-03) and the richer account-picker FR-010's manual transaction entry (S-02) deferred during its own planning. This plan only delivers the account CRUD + settings UI; it does not modify `Transaction`/`ImportBatch` or S-02's plan — those integrations are future work for the slices that consume this data.

## Current State Analysis

No account concept exists anywhere in the codebase. `Transaction.Bank` and `ImportBatch.Bank` (`MyFinances/backend/Transactions/Transaction.cs:12`, `ImportBatch.cs:10`) are free-text strings with no link to a stored account. EF Core Migrations are already wired up (`MyFinances/backend/Migrations/`, two migrations present), so adding `Account` is a normal additive migration — no existing table changes. User-scoping everywhere is a raw `UserId` Guid column plus a manual `.Where()` (`ClaimsPrincipalExtensions.cs`), not EF navigation collections — `AppUser` has no navigation properties today and this plan won't add one either, for consistency.

The endpoint convention (`MapXEndpoints(this IEndpointRouteBuilder api)`, wired into the shared `api` group in `Program.cs:102-105`) and the antiforgery pattern for JSON-body mutating endpoints (`ImportEndpoints.cs:159-172`) are directly reusable. `IBankStatementParser` (`MyFinances/backend/Import/IBankStatementParser.cs`) exposes a `BankName` string per registered parser (currently only `MBankCsvParser` → `"mBank"`), resolved via `IEnumerable<IBankStatementParser>` DI — this plan reuses that same registration to build a bank-name option list, but no endpoint currently exposes that list to the frontend; this plan adds one.

On the frontend, no "Settings" route exists (`routes.ts:1-8` has no reference), and no reusable list/form/dialog components exist anywhere in `app/components/` — every page (e.g. `import.tsx`) builds its own local `useState`-driven form and list inline. `AppHeader.tsx`'s nav links follow one repeated Tailwind className pattern this plan's new "Settings" link will match.

## Desired End State

A logged-in user opens a new "Settings" page (linked from the header nav) and sees a list of their own bank accounts (bank name + account number). They can add a new account by picking a bank from a dropdown (sourced from registered import parsers plus "Other") and typing an account number; edit an existing account's bank/number; and delete an account. The system rejects adding the same bank + account number combination twice, per user, with a clear inline error instead of a silent duplicate row.

### Key Discoveries:

- `MyFinances/backend/Transactions/Transaction.cs:12`, `ImportBatch.cs:10` — `Bank` is free text everywhere; no account entity or account-number field exists in the schema today.
- `MyFinances/backend/AppDbContext.cs:12-14,22-23` — two `DbSet`s registered as context properties; one Fluent API index (`HasIndex(t => new { t.UserId, t.Hash })`, non-unique). A new `DbSet<Account>` plus a *unique* index follows the same pattern.
- `MyFinances/backend/Import/ImportEndpoints.cs:159-172`, `AuthEndpoints.cs:59-72` — the manual antiforgery `AddEndpointFilter` pattern JSON-body mutating endpoints must reuse (JSON bodies skip the automatic form-binding CSRF protection).
- `MyFinances/backend/Transactions/ClaimsPrincipalExtensions.cs` — reuse `principal.GetUserId(userManager)` for scoping every query.
- `MyFinances/backend/Import/IBankStatementParser.cs`, `DI/ImportServiceCollectionExtensions.cs:12` — `IEnumerable<IBankStatementParser>.Select(p => p.BankName)` is the source of truth for known bank names; grows automatically as S-07/S-08 register Revolut/Erste parsers.
- `MyFinances/frontend/app/routes.ts:1-8` — flat `route(path, file)` array; no "settings" entry exists yet.
- `MyFinances/frontend/app/components/AppHeader.tsx:27-40` — nav-link pattern (`<Link>` + repeated className string) inside an `authenticated && (...)` block.
- `MyFinances/frontend/app/lib/api.ts:1,34-49` — `apiFetch<T>(path, init?)` prefixes `VITE_API_BASE_URL ?? "/api"`, auto-attaches CSRF token for mutating verbs, throws `ApiError` carrying the raw `Response` on non-OK.
- No reusable dialog/list/form component and no account-number validation pattern exist anywhere in `app/` — this plan follows `import.tsx`'s inline `useState` + Tailwind pattern rather than introducing a new shared component.

## What We're NOT Doing

- Linking `Transaction`/`ImportBatch` to an `Account` (no `AccountId` FK, no migration touching those tables) — that integration belongs to whichever future work revisits S-02's bank dropdown or S-03's transfer-detection heuristic, not this slice.
- Implementing S-03's internal-transfer detection heuristic itself — this slice only provides the account data it will consume.
- Revisiting S-02's manual-transaction-entry plan to swap its bank dropdown for an account picker — S-02's plan explicitly deferred this and ships independently with its own bank-name dropdown.
- An account nickname/display name field — the user chose bank name + account number only; a same-bank duplicate is prevented by the uniqueness constraint (below), and disambiguating multiple accounts at the same bank in a dropdown is a future concern if it arises.
- Currency, account type, or balance tracking — out of scope; PRD is PLN-only and this feature only needs enough identity to match transactions to "the user's own known accounts," not full account management.
- Any shared/reusable dialog, list, or form component extracted for future pages — following the existing precedent of page-local inline state (`import.tsx`), not introducing new frontend architecture.

## Implementation Approach

Add a new `Account` entity (`Id`, `UserId`, `BankName`, `AccountNumber`) via an additive EF Core migration, with a unique index on `(UserId, BankName, AccountNumber)` so a duplicate add/edit fails fast with a clear error instead of creating a second identical row. Add `AccountEndpoints.cs` following the exact `MapXEndpoints` + antiforgery-filter convention `ImportEndpoints.cs`/`AuthEndpoints.cs` already establish: `GET /accounts` (list, user-scoped), `GET /accounts/banks` (known bank names from `IEnumerable<IBankStatementParser>` plus a fixed `"Other"` sentinel — the dropdown source), `POST /accounts` (create), `PUT /accounts/{id}` (update), `DELETE /accounts/{id}` (delete). A unique-index violation on create/update is caught and translated into a 409 Conflict with a friendly message rather than letting a raw `DbUpdateException` surface.

On the frontend, add a new `routes/settings.tsx` page registered in `routes.ts`, linked from `AppHeader.tsx`'s nav alongside the existing "Add transactions" link. The page follows `import.tsx`'s established shape exactly: local `useState` for the account list/form/errors, `apiFetch` for all calls, a bank-name `<select>` populated from `GET /accounts/banks`, an account-number text input, and an inline list of the user's accounts each with edit (opens the same form pre-filled) and delete (simple confirm-before-delete, no modal component) actions.

## Phase 1: Backend Account Entity + Endpoints

### Overview

Add the `Account` entity, its migration, and the CRUD + bank-options endpoint module.

### Changes Required:

#### 1. Entity

**File**: `MyFinances/backend/Transactions/Account.cs` (new)

**Intent**: One row per bank account the user has told the system about; used later as the "known accounts" input to S-03's transfer-detection heuristic and as an account picker for manual entry (S-02 follow-up), but this slice only stores and CRUDs it.

**Contract**: `Account { Guid Id; Guid UserId; required string BankName; required string AccountNumber; }` — same shape/conventions as `Transaction`/`ImportBatch` (raw `UserId` column, no navigation property, `required` modifier for non-nullable strings).

#### 2. DbContext + migration

**File**: `MyFinances/backend/AppDbContext.cs`, new migration under `MyFinances/backend/Migrations/`

**Intent**: Register the new entity and its uniqueness rule; purely additive, no changes to existing tables.

**Contract**: Add `public DbSet<Account> Accounts => Set<Account>();` alongside the existing two `DbSet`s; in `OnModelCreating`, add `modelBuilder.Entity<Account>().HasIndex(a => new { a.UserId, a.BankName, a.AccountNumber }).IsUnique();`. Generate the migration via `dotnet ef migrations add AddAccounts`.

#### 3. Contracts

**File**: `MyFinances/backend/Transactions/AccountContracts.cs` (new)

**Intent**: DTOs for list/create/update responses and the bank-options list, mirroring `Import/ImportContracts.cs`'s shape.

**Contract**: `AccountDto { Id, BankName, AccountNumber }`; `AccountWriteRequest { BankName, AccountNumber }` (create/update body); `BankOptionsResponse { string[] BankNames }` (or a bare `string[]` — implementer's choice, matching whichever existing DTO convention reads more consistently against `ImportContracts.cs`).

#### 4. Endpoint module

**File**: `MyFinances/backend/Transactions/AccountEndpoints.cs` (new)

**Intent**: `MapAccountEndpoints(this IEndpointRouteBuilder api)`, registered in `Program.cs` alongside `MapImportEndpoints()`, nested under `/api` so it inherits `RequireAuthorization()`.

**Contract**:
- `GET /accounts/banks` — returns known bank names (`IEnumerable<IBankStatementParser>.Select(p => p.BankName)`, deduplicated) plus `"Other"`, in that order. No auth-scoping needed beyond the group-level `RequireAuthorization()` (not user-specific data).
- `GET /accounts` — returns all of the current user's accounts as `AccountDto[]`, ordered by `BankName` then `AccountNumber`.
- `POST /accounts` — body `AccountWriteRequest`; inserts a new `Account`; on a unique-index violation (catch `DbUpdateException`, or pre-check via a `.Where(...).AnyAsync()` before insert — implementer's choice), returns 409 Conflict with a `{ Message: "An account with this bank and account number already exists." }`-shaped problem response; otherwise 201 with the new `AccountDto`.
- `PUT /accounts/{id}` — same body and duplicate-check as create, excluding the account being edited from the uniqueness check by id; 404 if `id` doesn't belong to the current user; 409 on collision with a *different* existing account; otherwise 200 with the updated `AccountDto`.
- `DELETE /accounts/{id}` — 404 if `id` doesn't belong to the current user; otherwise deletes and returns 204.
- `POST`/`PUT`/`DELETE` all carry the same manual antiforgery `AddEndpointFilter` used by `ImportEndpoints.cs`'s commit endpoint. `GET /accounts/banks` and `GET /accounts` need no antiforgery filter (non-mutating).

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds
- `dotnet ef migrations` for `AddAccounts` applies cleanly against a fresh database
- `GET /accounts/banks` returns the registered parser bank names plus `"Other"`
- `GET /accounts` returns only the current user's accounts, not another user's
- `POST /accounts` with a new bank+number inserts and returns 201; with a duplicate bank+number for the same user returns 409 without inserting
- `PUT /accounts/{id}` updates fields; excludes the account's own row from its own duplicate check; returns 409 when colliding with a *different* existing account; returns 404 for another user's account id
- `DELETE /accounts/{id}` removes the row; returns 404 for another user's account id

#### Manual Verification:

- N/A (fully covered by automated endpoint tests; UI covered in Phase 2)

---

## Phase 2: Frontend Settings Page

### Overview

Add the "Settings" page: nav link, account list, add/edit form, delete.

### Changes Required:

#### 1. Route registration

**File**: `MyFinances/frontend/app/routes.ts`

**Intent**: Register the new page under `/settings`.

**Contract**: Add `route("settings", "routes/settings.tsx")` alongside the existing `route("import", "routes/import.tsx")` entry.

#### 2. Nav link

**File**: `MyFinances/frontend/app/components/AppHeader.tsx`

**Intent**: Add a "Settings" link to the authenticated nav block, matching the existing "Add transactions"/"Import transactions" link's pattern exactly.

**Contract**: New `<Link to="/settings" className="...">Settings</Link>` inside the same `authenticated && (...)` block, reusing the identical className string already repeated per link.

#### 3. Settings page

**File**: `MyFinances/frontend/app/routes/settings.tsx` (new)

**Intent**: On load, fetch `GET /accounts/banks` (for the dropdown) and `GET /accounts` (for the list). Render a form (bank `<select>`, account-number text input) that `POST`s to `/accounts` via `apiFetch`; on success, clear the form and re-fetch the list. On a 409 response, show the inline error message from the response body (reusing the `extractErrorMessage`-style helper `import.tsx` already has). Render the account list below the form; each row has "Edit" (pre-fills the form, submits to `PUT /accounts/{id}` instead of `POST /accounts`, same 409 handling) and "Delete" (a simple confirm-before-delete, e.g. a native `window.confirm` or a two-step "Delete? / Confirm" inline toggle — no dialog component exists in this codebase, so don't introduce one for this). If the user has no accounts yet, show an empty-state line matching `home.tsx`'s existing "you haven't imported any transactions yet" phrasing convention (e.g. "You haven't added any accounts yet").

**Contract**: Page follows the exact structural pattern of `import.tsx` (`<AppHeader authenticated />` + `<main>`, local `useState` for form/list/error/loading, `apiFetch` for every call, list re-fetched after every successful create/update/delete).

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes
- `npm run build` succeeds

#### Manual Verification:

- Adding an account with a new bank + account number succeeds and appears in the list immediately
- Adding an account with a bank + account number that already exists shows an inline error and does not add a second row
- Editing an existing account's number updates it in the list; editing it to collide with a different existing account shows the same inline duplicate error
- Deleting an account removes it from the list (after confirming)
- Nav link "Settings" is visible and navigates to the new page; empty state shows correctly for a user with no accounts yet

---

## Phase 3: Integration Tests

### Overview

Cover the new endpoints end-to-end with the existing `WebApplicationFactory` harness.

### Changes Required:

#### 1. Account endpoint tests

**File**: `MyFinances/backend/Tests/AccountEndpointsTests.cs` (new)

**Intent**: Reuse the `WebApplicationFactory` + EF Core InMemory pattern from `AuthEndpointsTests.cs`/`ImportEndpointsTests.cs`, registering a user via `/api/auth/register` for auth.

**Contract**: Test cases cover: `GET /accounts/banks` returns the registered parser names plus `"Other"`; create with no duplicate (inserts, 201); create with a duplicate bank+number for the same user (409, no insert); create with the same bank+number for a *different* user (succeeds — uniqueness is per-user); update recomputing fields and excluding self from the duplicate check; update triggering a 409 against a different account; update/delete returning 404 for another user's account id; delete removing a row; list scoped to the current user only.

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes, including the new `AccountEndpointsTests`

#### Manual Verification:

- N/A (fully covered by automated integration tests)

---

## Testing Strategy

### Unit Tests:

- Covered by the integration tests in Phase 3 (endpoint-level, per-scenario); no new pure-unit logic is introduced beyond a DB-level uniqueness constraint.

### Integration Tests:

- Full create/update/delete/list flow against `WebApplicationFactory`, including the duplicate-conflict path for both create and update, the per-user uniqueness scoping (same bank+number allowed across different users), and the cross-user 404 scoping check.

### Manual Testing Steps:

1. Log in, navigate to "Settings" (new nav link).
2. Confirm the empty state shows when no accounts exist yet.
3. Add an account (pick "mBank" or "Other", type an account number); confirm it appears in the list.
4. Add another account with the identical bank + account number; confirm an inline error appears and no second row is added.
5. Edit the first account's number to something new; confirm the list updates.
6. Edit it again to collide with the second account's bank + number; confirm the inline duplicate error appears.
7. Delete one of the two accounts; confirm it disappears from the list after confirming.

## Migration Notes

One additive migration (`AddAccounts`) creating the new `Account` table with a unique index on `(UserId, BankName, AccountNumber)`. No changes to `Transaction`, `ImportBatch`, or any other existing table.

## References

- Related plan (deferred this feature): `context/changes/manual-transaction-entry/plan.md`
- Related plan (S-01 conventions this reuses): `context/changes/mbank-import-with-dedup/plan.md`
- Endpoint module convention: `MyFinances/backend/Import/ImportEndpoints.cs`, `MyFinances/backend/Auth/AuthEndpoints.cs`
- User-scoping convention: `MyFinances/backend/Transactions/ClaimsPrincipalExtensions.cs`
- Bank-name source: `MyFinances/backend/Import/IBankStatementParser.cs`, `MyFinances/backend/DI/ImportServiceCollectionExtensions.cs`
- Roadmap entry: `context/foundation/roadmap.md` — S-10 (account-management), prerequisite for S-03's internal-transfer detection

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Backend Account Entity + Endpoints

#### Automated

- [x] 1.1 `dotnet build` succeeds
- [x] 1.2 `AddAccounts` migration applies cleanly against a fresh database
- [x] 1.3 `GET /accounts/banks` returns registered parser bank names plus "Other"
- [x] 1.4 `GET /accounts` returns only the current user's accounts
- [x] 1.5 `POST /accounts` inserts on new bank+number (201); returns 409 without inserting on a duplicate for the same user
- [x] 1.6 `PUT /accounts/{id}` updates fields, excludes self from duplicate check, returns 409 on collision with a different account, 404 for another user's id
- [x] 1.7 `DELETE /accounts/{id}` removes the row; 404 for another user's account id

### Phase 2: Frontend Settings Page

#### Automated

- [ ] 2.1 `npm run typecheck` passes
- [ ] 2.2 `npm run build` succeeds

#### Manual

- [ ] 2.3 Adding an account with a new bank + number succeeds and appears in the list
- [ ] 2.4 Adding a duplicate bank + number shows an inline error and does not add a second row
- [ ] 2.5 Editing an account updates it; editing into a collision with a different account shows the duplicate error
- [ ] 2.6 Deleting an account removes it from the list (after confirming)
- [ ] 2.7 Nav link "Settings" works; empty state shows correctly with zero accounts

### Phase 3: Integration Tests

#### Automated

- [ ] 3.1 `dotnet test` passes, including the new `AccountEndpointsTests`
