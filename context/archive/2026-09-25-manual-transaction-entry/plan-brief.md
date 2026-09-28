# Manual Transaction Entry — Plan Brief

> Full plan: `context/changes/manual-transaction-entry/plan.md`

## What & Why

Deliver S-02 / FR-010: the user can manually add, edit, and delete transactions, without those
transactions colliding with a later import of the same real-world transaction. This closes the
"how do I record cash spend or a transaction my bank statement doesn't cover yet" gap in the core
spend-insight loop — everything downstream (categorization, charts) currently only sees imported
data.

## Starting Point

`Transaction` already has every field this slice needs — `ImportBatchId` is nullable specifically
"for a later slice [that] will support manual entries not tied to any import"
(`Transaction.cs:26`), and the S-01 dedup hash (`DedupHash.ComputeHash`) takes only inputs a manual
entry already has (date, amount, description, account). `TransactionEndpoints.cs` currently only
has `GET /`; there is no create/edit/delete path yet. S-10 (account-management) already shipped
`GET /accounts`, which `Account.cs`'s own code comment names as the intended picker for this slice.

## Desired End State

On the home page, a user with at least one bank account sees an "Add transaction" control above
their transaction list. They can add, edit, or delete any transaction — imported or manual alike.
Adding/editing a transaction that collides (by hash) with an existing one shows a warning with the
existing transaction's details and an "anyway" override, instead of silently duplicating or
silently blocking. A manually-entered transaction is later recognized as a duplicate during import,
exactly like two imported transactions would be.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Edit/delete scope | All transactions, imported or manual | User chose flexibility over import-record immutability | Plan |
| Duplicate on create/edit | Warn (409 + existing snapshot), allow via explicit override | Matches the import flow's own "Keep despite duplicate" UX instead of introducing a second model | Plan |
| Category at creation | Required | Manually-added transactions should be immediately visible in category-based charts, unlike an import row which can sit uncategorized in the queue | Plan |
| Account source | Existing `GET /accounts` (S-10), required; empty state links to Settings | `Account.cs` already earmarks this exact endpoint for S-02; avoids reintroducing a free-text bank field | Plan |
| Hash on edit | Recomputed from new field values; `ImportBatchId` left unchanged | Dedup must reflect a transaction's *current* data, not its original import snapshot | Plan |
| Delete semantics | Hard delete, unconditional, same for manual and imported rows | Matches `AccountEndpoints.cs`'s existing delete convention; no soft-delete/undo exists anywhere in the app | Plan |
| Amount input | Single signed field (negative = expense, positive = income) | Matches `Transaction.Amount`'s stored representation exactly — zero conversion logic | Plan |
| `IsInternalTransfer` | Not exposed in the manual form; defaults `false`, same as import | Already has its own dedicated UI in the categorization queue; adding a second entry point would fragment that flow | Plan |

## Scope

**In scope:**
- `POST`/`PUT`/`DELETE /transactions` backend endpoints, reusing `DedupHash` unmodified
- Duplicate-detection response (409 + existing transaction snapshot) with an explicit override
- Inline add/edit/delete UI on the home page's transaction list, account + category pickers
- Backend tests for the three new endpoints

**Out of scope:**
- Any change to `Transaction`'s schema, or a new EF Core migration
- Any change to the import parse/commit flow or `DedupHash` itself
- A manual `IsInternalTransfer` toggle (stays exclusive to the categorization queue)
- Soft-delete, undo, or adjusting a past `ImportBatch`'s stored summary counts after a delete
- A new frontend route/page or a shared/reusable form/dialog component

## Architecture / Approach

Three new endpoints on the existing `TransactionEndpoints.cs` module, following
`AccountEndpoints.cs`'s CRUD + antiforgery-filter + 409-conflict shape exactly. `POST`/`PUT` share
one `TransactionWriteRequest` contract, validate `AccountId`/`CategoryId` ownership/existence,
compute the hash via the unmodified `DedupHash.ComputeHash`, and short-circuit into a `409` (unless
`Force: true`) when a same-hash row already exists. On the frontend, `home.tsx` gains an inline
add/edit form (mirroring `settings.tsx`'s toggle-form pattern) and per-row edit/delete actions,
sourcing pickers from the already-existing `GET /accounts` and `GET /categorization/categories`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend write endpoints | `POST`/`PUT`/`DELETE /transactions`, hash reuse, 409-duplicate response | Getting the hash-recompute-on-edit / `ImportBatchId`-preserved behavior exactly right, since it's the one subtle interaction with S-01's existing data |
| 2. Frontend add/edit/delete UI | Inline form + row actions on `home.tsx`, account/category pickers, duplicate-warning UX | Keeping the duplicate-override flow (409 → "anyway" → resubmit with `Force`) simple enough to fit the existing page-local `useState` pattern without a new component |

**Prerequisites:** S-01 (dedup hash, done) and S-10 (account management, done) — both already
shipped; no upstream work blocks starting this plan.
**Estimated effort:** ~1-2 sessions across 2 phases — no schema change, both phases reuse
established backend and frontend patterns directly.

## Open Risks & Assumptions

- Assumes nothing else in the schema references `Transaction.Id` as a foreign key, so `DELETE`
  needs no FK-safety pre-check (verified against `AppDbContext`/migration snapshot during
  planning; re-check if a later slice adds such a reference before implementing Phase 1).
- Assumes the user always has read access to `GET /accounts` and `GET /categorization/categories`
  by the time they reach the home page (both already gated behind the same auth as everything
  else) — no new auth work needed.

## Success Criteria (Summary)

- User can add, edit, and delete a transaction from the home page, with account and category
  required.
- Adding/editing a transaction that duplicates an existing one (by date+amount+description+account)
  shows a warning instead of silently duplicating or silently failing.
- A manually-entered transaction is flagged as a duplicate if the same transaction is later
  imported from a bank statement.
