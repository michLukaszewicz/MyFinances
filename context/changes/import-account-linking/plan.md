# Link Imported Transactions to an Account — Implementation Plan

## Overview

Today the import flow (S-01, `mbank-import-with-dedup`) stores each `Transaction` with a free-text `Bank` string set by whichever `IBankStatementParser` recognized the CSV. Separately, `account-management` added user-owned `Account` rows (bank name + account number) used today only by the Settings page. This change closes the gap: the import page requires the user to pick which of their accounts a statement belongs to, and `Transaction`/`ImportBatch` reference that `Account` by `AccountId` instead of a bank-name string.

## Current State Analysis

- [MyFinances/backend/Transactions/Transaction.cs](MyFinances/backend/Transactions/Transaction.cs) — `Bank` is a plain `required string`, no relationship to `Account`.
- [MyFinances/backend/Transactions/ImportBatch.cs](MyFinances/backend/Transactions/ImportBatch.cs) — same: a free-text `Bank` string, set from `parser.BankName` at commit time.
- [MyFinances/backend/Transactions/Account.cs](MyFinances/backend/Transactions/Account.cs) — already exists (`Id`, `UserId`, `BankName`, `AccountNumber`), unique per `(UserId, BankName, AccountNumber)` ([AppDbContext.cs:29-31](MyFinances/backend/AppDbContext.cs)). CRUD lives in [AccountEndpoints.cs](MyFinances/backend/Transactions/AccountEndpoints.cs) under `/api/accounts`.
- [MyFinances/backend/Import/DedupHash.cs](MyFinances/backend/Import/DedupHash.cs) — pure function `ComputeHash(userId, date, amount, description, bank)`, called identically from both `/import/parse` and `/import/commit` ([ImportEndpoints.cs:52,67,101,106](MyFinances/backend/Import/ImportEndpoints.cs)).
- [MyFinances/backend/Import/ImportEndpoints.cs](MyFinances/backend/Import/ImportEndpoints.cs) — `/parse` auto-detects the bank format via `IBankStatementParser.CanParse`, with a manual `bank` form-field fallback if detection fails. `/commit` re-looks-up the parser by `request.Bank` solely to feed `DedupHash.ComputeHash` and to stamp `ImportBatch.Bank`.
- [MyFinances/backend/Import/ImportContracts.cs](MyFinances/backend/Import/ImportContracts.cs) — `ImportParseResponse`/`ImportCommitRequest` carry `Bank` (string), no account concept.
- [MyFinances/frontend/app/routes/import.tsx](MyFinances/frontend/app/routes/import.tsx) — upload form has no account selector; posts `file` (+ optional `bank` fallback) to `/import/parse`, then `Bank`/`Rows` to `/import/commit`.
- [MyFinances/frontend/app/routes/settings.tsx](MyFinances/frontend/app/routes/settings.tsx) — already fetches `GET /accounts/` and renders `AccountDto[]`; the import page will reuse this same endpoint and DTO shape.
- Tests: [ImportEndpointsTests.cs](MyFinances/backend/Tests/ImportEndpointsTests.cs), [DedupHashTests.cs](MyFinances/backend/Tests/DedupHashTests.cs), [AccountEndpointsTests.cs](MyFinances/backend/Tests/AccountEndpointsTests.cs) all use the `AuthApiFactory` + EF Core **InMemory** provider swap — no real Postgres migration ever runs in the test suite, so the migration's data-wipe step only matters against a real dev/prod database.

### Key Discoveries:

- [MyFinances/backend/Transactions/ClaimsPrincipalExtensions.cs:11](MyFinances/backend/Transactions/ClaimsPrincipalExtensions.cs) — `ClaimsPrincipal.GetUserId(UserManager<AppUser>)` is the established convention for resolving the current user; both `AccountEndpoints` and `ImportEndpoints` already use it and this plan follows the same pattern for the new `AccountId` ownership check.
- `/commit`'s parser lookup ([ImportEndpoints.cs:91-95](MyFinances/backend/Import/ImportEndpoints.cs)) exists *only* to recompute the dedup hash and stamp `ImportBatch.Bank`. Once the hash takes `accountId` instead of a bank string and `ImportBatch.Bank` is dropped, that lookup (and the `request.Bank` field itself) becomes dead code — Phase 2 removes it rather than keeping it around unused.
- The manual bank-picker fallback in `/parse` (`[FromForm] string? bank`, `SUPPORTED_BANKS` in `import.tsx`) is a *format* hint (which `IBankStatementParser` to use), independent of *which account* the statement belongs to — both survive this change and are wired together only for the new mismatch warning.

## Desired End State

A logged-in user with at least one account opens Import, must pick one of their accounts from a required dropdown before uploading, and the resulting `Transaction`/`ImportBatch` rows are linked to that `Account` via `AccountId` (no more `Bank` string column on either). If the account's `BankName` doesn't match the CSV's auto-detected bank format, the review screen shows a non-blocking warning but still lets the import proceed. A user with zero accounts sees the upload form replaced by a message + link to Settings instead of a dropdown they can't use.

### Key Discoveries:

(see above — folded in since this is a small, single-slice change)

## What We're NOT Doing

- Backfilling existing `Transaction`/`ImportBatch` rows to a matching `Account` — this is pre-launch dev data; the migration wipes it (user decision).
- Blocking the parse/commit request when the selected account's bank doesn't match the detected CSV format — warn only (user decision).
- An import-history page, Revolut/Erste parsers, categorization, or internal-transfer detection — unchanged from `mbank-import-with-dedup`'s own scope exclusions.
- Changing `Account`'s own schema, CRUD endpoints, or the Settings page beyond what the import page reuses read-only.

## Implementation Approach

`Account` becomes the single source of truth for "which bank" a transaction belongs to. `Transaction.Bank` and `ImportBatch.Bank` (both `string`) are replaced by `AccountId` (`Guid`, FK to `Account`) plus a `HasOne`/navigation relationship. `DedupHash.ComputeHash` swaps its `string bank` parameter for `Guid accountId`, so the dedup pool is scoped per-account rather than per-bank-name (two accounts at the same bank no longer collide). The `/import/parse` and `/import/commit` contracts both gain a required `AccountId`, validated to belong to the current user the same way `AccountEndpoints` already validates ownership. `/parse` keeps its existing CSV-format auto-detection (`IBankStatementParser`) for parsing purposes only, and additionally compares the detected format's `BankName` against the selected account's `BankName` to produce a non-blocking `BankMismatch` flag in the response. `/commit` no longer needs to look up a parser at all — it takes `AccountId` directly and recomputes hashes from it. The frontend adds a required account `<select>` (populated from the existing `GET /accounts/` endpoint) above the file input, blocks the whole form with a Settings link when the account list is empty, and threads `accountId` through both the parse and commit requests.

## Phase 1: Backend Data Model

### Overview

Replace the `Bank` string on `Transaction`/`ImportBatch` with an `AccountId` FK, and repoint `DedupHash` at `AccountId`.

### Changes Required:

#### 1. `DedupHash` formula

**File**: [MyFinances/backend/Import/DedupHash.cs](MyFinances/backend/Import/DedupHash.cs)

**Intent**: Scope the dedup pool per-account instead of per-bank-name string, so two accounts at the same bank don't false-positive collide.

**Contract**: `ComputeHash(Guid userId, DateOnly date, decimal amount, string description, Guid accountId)` — replace the trailing `string bank` parameter with `Guid accountId` (formatted the same way as `userId`, via `ToString("D")`) in the joined stable string.

#### 2. `Transaction` entity

**File**: [MyFinances/backend/Transactions/Transaction.cs](MyFinances/backend/Transactions/Transaction.cs)

**Intent**: Replace the free-text bank string with a real relationship to the user's account.

**Contract**: Remove `required string Bank`. Add `Guid AccountId` and an `Account Account` navigation property (non-nullable — every transaction, imported or future manually-entered, belongs to exactly one account).

#### 3. `ImportBatch` entity

**File**: [MyFinances/backend/Transactions/ImportBatch.cs](MyFinances/backend/Transactions/ImportBatch.cs)

**Intent**: Same replacement as `Transaction`, for consistency and so the batch's bank is always read live from the linked account.

**Contract**: Remove `required string Bank`. Add `Guid AccountId` and an `Account Account` navigation property.

#### 4. `AppDbContext` model configuration

**File**: [MyFinances/backend/AppDbContext.cs](MyFinances/backend/AppDbContext.cs)

**Intent**: Wire up the new FK relationships and keep the existing dedup-lookup index useful.

**Contract**: Configure `Transaction.Account` and `ImportBatch.Account` as required one-to-many relationships (`Account` → many `Transaction`/`ImportBatch`, `DeleteBehavior.Restrict` — deleting an account with existing transactions should fail loudly, not cascade-delete financial history). Keep the existing non-unique `HasIndex(t => new { t.UserId, t.Hash })`.

#### 5. EF Core migration

**File**: `MyFinances/backend/Migrations/<timestamp>_LinkTransactionsToAccounts.cs` (new, via `dotnet ef migrations add`)

**Intent**: Apply the schema change against the real (Postgres) dev database. Per user decision, existing `Transaction`/`ImportBatch` rows are pre-launch dev data and are wiped rather than backfilled.

**Contract**: The migration must, in order: (a) delete all existing rows from `transactions` then `import_batches` (raw SQL `DELETE FROM` in the `Up` method, before the schema edit, since `import_batches` is only referenced by `transactions` via FK) — needed because the new `account_id` columns are non-nullable and no valid value exists for old rows; (b) drop the `bank` column from both tables; (c) add a non-nullable `account_id uuid` column to both tables with a FK to `accounts(id)` and `ON DELETE RESTRICT`. Regenerate `AppDbContextModelSnapshot.cs` as part of `dotnet ef migrations add`.

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds in `MyFinances/backend`
- `dotnet ef migrations add LinkTransactionsToAccounts` (or equivalent) generates without errors and `dotnet ef migrations script` runs clean

#### Manual Verification:

- N/A — pure schema/data-model phase, exercised through Phase 2/3's endpoint and UI checks

---

## Phase 2: Backend Import Endpoints

### Overview

Make `/import/parse` and `/import/commit` account-aware: require and validate `AccountId`, recompute dedup hashes from it, and surface a non-blocking bank-mismatch signal.

### Changes Required:

#### 1. Import contracts

**File**: [MyFinances/backend/Import/ImportContracts.cs](MyFinances/backend/Import/ImportContracts.cs)

**Intent**: Add the account concept to both request/response shapes; drop the now-redundant `Bank` field from the commit request and batch summary (Phase 1 dropped it from the entity).

**Contract**: `ImportParseResponse` gains `bool BankMismatch` (true when the detected parser's `BankName` differs from the selected account's `BankName`) alongside its existing `Bank` field (kept — it's the *detected format* name, still useful to show "recognized as mBank" regardless of the account chosen). `ImportCommitRequest` drops `string Bank` and gains `Guid AccountId`. `ImportSummaryDto` drops `string Bank` and gains `Guid AccountId` (or an `AccountDto`-shaped summary — reuse `Transactions.AccountDto` for consistency with `AccountEndpoints`).

#### 2. `/import/parse` endpoint

**File**: [MyFinances/backend/Import/ImportEndpoints.cs](MyFinances/backend/Import/ImportEndpoints.cs)

**Intent**: Require the target account, validate it belongs to the caller, hash against it instead of the bank-name string, and compute the mismatch flag.

**Contract**: Add a required `[FromForm] Guid accountId` parameter. Look up the `Account` scoped to `(Id == accountId, UserId == currentUserId)`; return `Results.NotFound()` (or a `400 Problem`) if it doesn't resolve — same ownership-check shape as `AccountEndpoints`'s existing lookups. Replace every `DedupHash.ComputeHash(userId, ..., parser.BankName)` call with `DedupHash.ComputeHash(userId, ..., account.Id)`. Set `BankMismatch = !string.Equals(parser.BankName, account.BankName, StringComparison.OrdinalIgnoreCase)` on the response.

#### 3. `/import/commit` endpoint

**File**: [MyFinances/backend/Import/ImportEndpoints.cs](MyFinances/backend/Import/ImportEndpoints.cs)

**Intent**: Same account requirement and ownership check as `/parse`; drop the now-unnecessary parser lookup since hashing and batch-stamping no longer need a bank-name string.

**Contract**: Replace `ImportCommitRequest.Bank`-driven parser lookup with an `AccountId`-driven `Account` ownership lookup (404/400 if not owned by caller). Remove the `parsers.FirstOrDefault(...)` call entirely. Compute `keepHashes`/`skipHashes` via `DedupHash.ComputeHash(userId, ..., account.Id)`. Set `ImportBatch.AccountId = account.Id` and `Transaction.AccountId = account.Id` (both replacing their old `Bank = parser.BankName` assignments).

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds
- `dotnet test` passes for the updated `ImportEndpointsTests` and `DedupHashTests` (Phase 4 updates these)

#### Manual Verification:

- N/A — exercised through Phase 3's UI checks

---

## Phase 3: Frontend Import Page

### Overview

Add a required account picker to the import page, block the form when the user has no accounts, and thread `accountId` through both requests plus the new mismatch warning.

### Changes Required:

#### 1. Account fetch + picker

**File**: [MyFinances/frontend/app/routes/import.tsx](MyFinances/frontend/app/routes/import.tsx)

**Intent**: Let the user pick which of their accounts this import belongs to, reusing the same `GET /accounts/` call and `AccountDto` shape already used by `settings.tsx`.

**Contract**: On mount, fetch `GET /accounts/` into local state (mirroring `settings.tsx`'s `loadAccounts` pattern). Add an `accountId` field to component state, a required `<select>` populated from the fetched accounts (label: `${bankName} — ${accountNumber}`), rendered above the file input. If the fetched account list is empty, render a blocking empty-state (message + a link to `/settings`) instead of the upload form — the same shape `settings.tsx` already uses for its own empty account list ("You haven't added any accounts yet.").

#### 2. Wire `accountId` into parse + commit

**File**: [MyFinances/frontend/app/routes/import.tsx](MyFinances/frontend/app/routes/import.tsx)

**Intent**: Send the selected account through both requests, matching the backend's new required field.

**Contract**: `handleSubmit`'s `FormData` gains `formData.append("accountId", accountId)`. `commitImport`'s JSON body drops `Bank: result.bank` and gains `AccountId: accountId`. The `ImportParseResponse`/`ImportSummaryDto` TypeScript interfaces are updated to match Phase 2's contract changes (`bankMismatch: boolean` added; `ImportSummaryDto`'s `bank` replaced per Phase 2's chosen shape).

#### 3. Bank-mismatch warning

**File**: [MyFinances/frontend/app/routes/import.tsx](MyFinances/frontend/app/routes/import.tsx)

**Intent**: Surface the non-blocking warning from Phase 2 without preventing the user from continuing.

**Contract**: When `result.bankMismatch` is true, render a dismissible-looking (but not actually dismissible — just visible) warning banner above the parsed-rows list, in the same visual style as the existing `allRowsAreDuplicates` amber banner, e.g. "This file looks like a {result.bank} export, but the selected account is {accountBankName}. You can still continue if this is correct." Does not disable the Continue button.

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes in `MyFinances/frontend`
- `npm run build` succeeds

#### Manual Verification:

- With zero accounts: Import page shows the blocking empty-state and a working link to Settings; no upload form is visible
- With one or more accounts: the account dropdown is required (form won't submit without a selection), upload + parse succeeds, and the resulting rows import against the chosen account
- Uploading an mBank-shaped file while a non-"mBank"-named account is selected shows the mismatch warning but still allows Continue
- Uploading the same file twice against the same account still triggers the existing duplicate-review flow; uploading the same file against two *different* accounts does not flag duplicates against each other (per the AccountId-scoped hash)

---

## Phase 4: Tests

### Overview

Update existing test suites for the new entity/contract shapes and add coverage for the new validation and warning paths.

### Changes Required:

#### 1. `DedupHashTests`

**File**: [MyFinances/backend/Tests/DedupHashTests.cs](MyFinances/backend/Tests/DedupHashTests.cs)

**Intent**: Match the new `ComputeHash` signature.

**Contract**: Replace the `Bank` string constant/parameter with an `AccountId` `Guid` constant; rename `ComputeHash_Differs_WhenBankDiffers` to `ComputeHash_Differs_WhenAccountIdDiffers` (or equivalent), asserting two different `Guid` account ids produce different hashes.

#### 2. `ImportEndpointsTests`

**File**: [MyFinances/backend/Tests/ImportEndpointsTests.cs](MyFinances/backend/Tests/ImportEndpointsTests.cs)

**Intent**: Cover the new required-`accountId` behavior end to end.

**Contract**: Existing parse/commit test flows create an `Account` first (via the DB context directly or `POST /accounts/`) and pass its id through `/import/parse` and `/import/commit`. Add cases for: `/import/parse` rejects (404/400) an `accountId` that doesn't belong to the authenticated user; `/import/parse` sets `BankMismatch = true` when the account's `BankName` isn't "mBank" but the uploaded file is mBank-shaped, and `false` when it matches; the same transaction hash imported under two different accounts for the same user does **not** get flagged as a duplicate of each other.

#### 3. `AccountEndpointsTests`

**File**: [MyFinances/backend/Tests/AccountEndpointsTests.cs](MyFinances/backend/Tests/AccountEndpointsTests.cs)

**Intent**: Confirm no regression — this suite doesn't touch import, but deleting an account that has linked transactions should now fail per Phase 1's `DeleteBehavior.Restrict`.

**Contract**: Add one case: creating a transaction linked to an account, then attempting `DELETE /accounts/{id}` for that account, fails (the restrict FK either surfaces as a 500/Problem from a `DbUpdateException`, or — preferred — `AccountEndpoints`'s delete handler catches it and returns a friendly `409 Problem`, matching the existing duplicate-account 409 pattern). Pick whichever the implementer finds `AccountEndpoints`'s delete handler already positioned to do cleanly; if it takes a code change to add the friendly 409, make it — an unhandled 500 on this path is a regression.

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes for the full backend suite (`DedupHashTests`, `ImportEndpointsTests`, `AccountEndpointsTests`, plus unaffected suites `AuthEndpointsTests`, `MBankCsvParserTests`)

#### Manual Verification:

- N/A — fully covered by automated tests

---

## Testing Strategy

### Unit Tests:

- `DedupHash.ComputeHash` produces different hashes for different `accountId` values, identical hashes for identical inputs (existing coverage, updated signature).

### Integration Tests:

- Full parse → review → commit flow against a specific account, including the ownership check (foreign account id rejected) and the mismatch-flag computation.
- Cross-account dedup isolation: same transaction data imported under two different accounts for the same user is not treated as a duplicate of itself.
- Account deletion is blocked (not a silent cascade or unhandled 500) once a transaction references it.

### Manual Testing Steps:

1. As a user with zero accounts, open Import — confirm the blocking empty-state and the Settings link work.
2. Add an account in Settings, return to Import — confirm the account now appears in the dropdown and is selectable.
3. Upload a real mBank sample CSV against that account — confirm parse, review, and commit all work and the imported transactions show up correctly linked to the account (spot-check via a DB query or a future account-scoped view, since there is currently no transaction-listing UI).
4. Repeat the same upload against a second, differently-named account — confirm it is *not* treated as a duplicate of the first import.
5. Create an account named something other than "mBank" and upload the mBank sample against it — confirm the mismatch warning appears and Continue still works.

## Performance Considerations

None beyond existing scope — the per-user hash index (`UserId, Hash`) is unchanged, and the new `AccountId` FK columns get their own index implicitly via the FK relationship.

## Migration Notes

The Phase 1 migration wipes all existing `transactions` and `import_batches` rows (user decision — this is pre-launch dev data, not worth a backfill). Run it against the dev database with this understood; there is no rollback path that recovers the wiped rows short of a database backup.

## References

- Prior plan this extends: [context/changes/mbank-import-with-dedup/plan.md](context/changes/mbank-import-with-dedup/plan.md)
- Account entity/endpoints this reuses: [context/changes/account-management/plan.md](context/changes/account-management/plan.md)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Backend Data Model

#### Automated

- [x] 1.1 `dotnet build` succeeds in `MyFinances/backend` — a1d687c
- [x] 1.2 `dotnet ef migrations add LinkTransactionsToAccounts` generates without errors and `dotnet ef migrations script` runs clean — a1d687c

### Phase 2: Backend Import Endpoints

#### Automated

- [x] 2.1 `dotnet build` succeeds
- [x] 2.2 `dotnet test` passes for the updated `ImportEndpointsTests` and `DedupHashTests`

### Phase 3: Frontend Import Page

#### Automated

- [ ] 3.1 `npm run typecheck` passes in `MyFinances/frontend`
- [ ] 3.2 `npm run build` succeeds

#### Manual

- [ ] 3.3 Zero-accounts empty-state blocks the form and links to Settings
- [ ] 3.4 Account dropdown is required; upload + parse succeeds against a chosen account
- [ ] 3.5 Bank-mismatch warning shows but doesn't block Continue
- [ ] 3.6 Same file imported against two different accounts is not flagged as a cross-account duplicate

### Phase 4: Tests

#### Automated

- [ ] 4.1 `dotnet test` passes for the full backend suite
