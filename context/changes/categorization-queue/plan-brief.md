# Categorization Queue (S-03) — Plan Brief

> Full plan: `context/changes/categorization-queue/plan.md`

## What & Why

Build the categorization queue: the user assigns a category to each transaction one at a
time, with no auto-suggestion (FR-007); internal transfers between the user's own accounts
get auto-flagged by a heuristic, overridable by hand (FR-009); any transaction can be
re-categorized at any time (FR-015). This is the product's core wedge — categorization
quality comes from the user's own deliberate decisions, not a black-box suggester — and it's
the direct prerequisite for the donut chart (S-04).

## Starting Point

`Transaction.CategoryId` already exists as an unwired nullable placeholder
(`Transaction.cs:30`). `Account` (S-10, merged) is a real per-user entity whose own code
comment states it exists specifically as "the 'known accounts' input to S-03's
transfer-detection heuristic" — this plan is that heuristic's first consumer. No `Category`
entity, no transfer-flag field, and no categorization UI exist yet.

## Desired End State

A `/categorize` page shows the next uncategorized transaction as a focused "up next" card;
saving a category or transfer-flag choice advances to the next one automatically. A second
"handled" list below lets the user revisit and change any already-decided transaction at any
time. Transfers between the user's own accounts are pre-flagged the first time they're seen,
without blocking a manual correction.

## Key Decisions Made

| Decision                                   | Choice                                                                 | Why                                                                                   | Source |
| ------------------------------------------- | ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------- | ------ |
| Category source                             | Fixed, seeded, global list of 12 categories — no user-managed CRUD      | No FR asks for category management; FR-007 says "the full category list" implying a fixed set | Plan   |
| Transfer-match predicate                    | Same user, **different `AccountId`**, `Amount == -otherAmount`, dates within 2 days | Uses `Account.Id` (not bank name) per `Account.cs:4-5`'s explicit hand-off comment    | Plan   |
| Transfer-detection timing                   | Runs inline on every queue/handled-list `GET`, skipping manually-set flags | Avoids touching the import-commit path (owned by another module); idempotent by construction | Plan   |
| Manual override persistence                 | `TransferFlagManuallySet` bool, set `true` on any explicit `PUT`         | Detection must never re-flag a transaction the user already corrected                 | Plan   |
| Re-categorization surface (FR-15)           | "Handled" list on the same page, not a separate view                     | S-09 (transaction-history-view) isn't merged into this branch — this plan can't depend on it | Plan   |
| Backend module boundary                     | New `MyFinances/backend/Categorization/` module + own route             | Avoids recreating `TransactionContracts.cs`/`TransactionEndpoints.cs`, which collided once already (S-02 vs. S-09) | Plan   |
| Frontend route                              | New `categorize.tsx`, `home.tsx` untouched                              | `home.tsx` is S-09's likely edit target                                                | Plan   |

## Scope

**In scope:**
- `Category` entity + seed migration, `Transaction` FK wiring + two new bool fields
- `Categorization` backend module: categories/queue/handled list + write endpoint, transfer
  detection service
- `/categorize` frontend page + nav link

**Out of scope:**
- Category management UI (create/edit/delete categories)
- Batch/bulk categorization
- Optimal transfer-pair matching (greedy first-match only)
- Any edit to `TransactionContracts.cs`, `TransactionEndpoints.cs`, or `home.tsx` (S-09's
  files, not merged here)
- Wiring categorization into the donut chart/spend totals (S-04)

## Architecture / Approach

New backend module (`Categorization/`) mirrors the existing `AccountEndpoints` pattern:
contracts + endpoints + a small detection service, registered in `Program.cs` alongside the
other modules. `Transaction.CategoryId`'s existing placeholder gets a real FK; two new fields
(`IsInternalTransfer`, `TransferFlagManuallySet`) track the transfer heuristic's state per
transaction. Detection is a lazy, idempotent pass over not-yet-manually-touched transactions,
run inline before every list read — no hook into the import-commit code owned by another
module. Frontend is one new route following the `settings.tsx` CRUD-page pattern, plus a
one-line nav addition.

## Phases at a Glance

| Phase                                  | What it delivers                                              | Key risk                                                        |
| --------------------------------------- | ----------------------------------------------------------------- | -------------------------------------------------------------------- |
| 1. Category entity + Transaction fields | Seeded category table, wired `CategoryId` FK, transfer-flag fields | Migration correctness against the Neon Postgres instance             |
| 2. Backend categorization module        | Queue/handled/categories/write endpoints + transfer detection      | Match predicate accuracy against real multi-bank data (date tolerance is an assumption) |
| 3. Frontend categorization page         | `/categorize` UI + nav link                                       | UX for "handled" list re-categorization staying discoverable without S-09's history view |

**Prerequisites:** S-01 (mbank-import-with-dedup) and S-10 (account-management) merged —
confirmed present on this branch (base commit `4981420`, includes both slices' commits).
**Estimated effort:** ~2-3 sessions across 3 phases.

## Open Risks & Assumptions

- The 2-day transfer-match date tolerance is a plan-level assumption (PRD doesn't specify a
  window) — worth validating against real mBank/Revolut/Erste transfer data once S-07/S-08
  land; manual override covers any misses in the meantime.
- Greedy first-match transfer pairing could mis-pair when a user has multiple same-day,
  same-amount transfers between the same two accounts — acceptable at MVP scale, correctable
  by hand.
- `AppHeader.tsx`'s nav list is a shared component; S-09 might also want to add its own nav
  entry there later — a small, low-probability merge-conflict surface, not a functional
  collision like the `TransactionContracts.cs` case.

## Success Criteria (Summary)

- User can categorize any transaction from a one-at-a-time queue, with no forced order and no
  auto-suggestion (FR-007)
- Transfers between the user's own accounts are pre-flagged automatically, and the flag can be
  corrected by hand at any time (FR-009)
- Any transaction, categorized or not, can be re-categorized at any time (FR-015)
