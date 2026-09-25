# Link Imported Transactions to an Account — Plan Brief

> Full plan: `context/changes/import-account-linking/plan.md`

## What & Why

On the import page, the user currently uploads a CSV with no way to say which of their accounts it belongs to, and every imported `Transaction` just stores whichever bank name the CSV parser detected as a free-text string. This change adds a required account picker to the import flow and replaces that free-text `Bank` field with a real `AccountId` foreign key on both `Transaction` and `ImportBatch`, so transactions are tied to an actual account record instead of a string.

## Starting Point

`account-management` already added user-owned `Account` rows (bank name + account number) and a Settings CRUD page, but nothing consumes them yet. `mbank-import-with-dedup` (currently `implementing`) built the parse → review → commit import flow with a `Bank` string set from the auto-detected CSV parser, dedup-hashed per `(user, bank name)`.

## Desired End State

A user with accounts picks one from a required dropdown before uploading; the import commits transactions linked to that account's `Id`. A user with no accounts sees the form blocked with a link to Settings. If the uploaded file's detected bank format doesn't match the selected account's bank name, a non-blocking warning shows but import still proceeds.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Dedup hash formula | Hash on `AccountId` instead of bank-name string | Two accounts at the same bank no longer false-positive collide on duplicates | Plan |
| Existing data migration | Wipe existing `Transaction`/`ImportBatch` rows | Pre-launch dev data; not worth a backfill-matching migration | Plan |
| Bank/account mismatch | Warn, don't block | Keeps the existing manual-bank-picker fallback usable; avoids new blocking validation | Plan |
| Zero-accounts state | Block form, link to Settings | Matches existing empty-state guidance pattern already used in Settings | Plan |
| `Bank` string field | Dropped from both `Transaction` and `ImportBatch` | Single source of truth via `Account.BankName`, avoids drift after account rename | Plan |

## Scope

**In scope:**
- `Transaction`/`ImportBatch` schema change: `Bank` (string) → `AccountId` (FK)
- `DedupHash` formula change to hash on `AccountId`
- `/import/parse` and `/import/commit` require + validate `AccountId`, add a `BankMismatch` flag
- Import page: required account picker, empty-state block, mismatch warning
- EF Core migration (wipes existing transaction/import-batch data)
- Updated/added backend tests

**Out of scope:**
- Backfilling old transactions to a matching account
- Blocking on bank/account mismatch
- Revolut/Erste parsers, categorization, transfer detection, import history UI
- Any change to `Account`'s own CRUD or the Settings page

## Architecture / Approach

`Account` becomes the single source of truth for "which bank." `Transaction`/`ImportBatch` get a required `AccountId` FK (`DeleteBehavior.Restrict`). `/import/parse` still auto-detects CSV format via `IBankStatementParser` (for parsing only) and now additionally requires an `accountId`, validated to belong to the caller; it compares the detected format's bank name against the account's bank name to set `BankMismatch`. `/import/commit` drops its parser lookup entirely (no longer needed once hashing depends on `AccountId`, not a bank string) and instead validates `accountId` ownership directly.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend Data Model | `AccountId` FK on `Transaction`/`ImportBatch`, `DedupHash` repointed, migration | Migration wipes existing dev data — irreversible without a DB backup |
| 2. Backend Import Endpoints | `/parse` + `/commit` require & validate `accountId`, mismatch flag | Commit endpoint's parser lookup removal must not break existing format-detection error messages from `/parse` |
| 3. Frontend Import Page | Required account picker, empty-state, mismatch warning, wiring | No existing transaction-listing UI to visually confirm the link post-import — manual verification relies on a DB spot-check |
| 4. Tests | Updated/new coverage across `DedupHash`, `Import`, `Account` suites | Account-deletion-with-linked-transactions behavior needs a friendly 409, not an unhandled 500 |

**Prerequisites:** `account-management` and `mbank-import-with-dedup` phases already implemented (both are, per current `change.md` status).
**Estimated effort:** ~1 session across 4 phases — small, single-slice schema + two-endpoint + one-page change.

## Open Risks & Assumptions

- The migration's data wipe is only safe because no real (non-dev) data exists yet — this assumption should be reconfirmed before running the migration against any shared/staging database.
- `ImportSummaryDto`'s exact post-`Bank`-removal shape (raw `AccountId` vs. a nested `AccountDto`) is left as an implementer call in Phase 2 — either satisfies the contract, pick whichever reads more naturally next to `AccountEndpoints`' own `AccountDto`.

## Success Criteria (Summary)

- A user can only complete an import after selecting one of their own accounts, and the resulting transactions are queryable by that account's id.
- Two accounts at the same bank do not spuriously duplicate-block each other's imports.
- A user with no accounts is guided to Settings instead of hitting a broken/unusable import form.
