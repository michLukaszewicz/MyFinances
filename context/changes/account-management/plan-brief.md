# Account Management — Plan Brief

> Full plan: `context/changes/account-management/plan.md`

## What & Why

FR-009's internal-transfer detection heuristic needs to identify "the user's own known accounts" across banks, and FR-010's manual entry deferred a real account picker during S-02's planning in favor of a placeholder bank-name dropdown. This change gives the user a "Settings" page to add, edit, and remove their own bank accounts (bank name + account number) — the data source both of those future slices will consume. This is S-10 on the roadmap, a prerequisite for S-03 (categorization-queue), needing only F-01 (auth).

## Starting Point

No account concept exists anywhere in the codebase — `Transaction.Bank`/`ImportBatch.Bank` are plain free-text strings. No "Settings" route exists on the frontend. EF Core Migrations, the `MapXEndpoints` endpoint convention, and the `UserId`-column user-scoping pattern (established in S-01) are all directly reusable for this new entity.

## Desired End State

A logged-in user opens "Settings" and sees a list of their own bank accounts. They can add a new one (bank picked from a dropdown of known banks + "Other", plus an account number), edit an existing one, or delete one. Adding the same bank + account number twice is rejected with a clear inline error instead of silently creating a duplicate row.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Bank name field | Constrained dropdown (known `IBankStatementParser` names + "Other") | Keeps account bank names consistent with import/parser names for S-03's later cross-bank matching | Plan (interview) |
| Account fields | Bank name + account number only, no nickname | User's explicit choice — minimal schema matching the original ask | Plan (interview) |
| Duplicate accounts | Enforce uniqueness per user via a DB unique index on (UserId, BankName, AccountNumber) | Prevents confusing duplicate rows that would pollute dropdowns and S-03's matching | Plan (interview) |
| Link to Transaction/ImportBatch | None — no FK added in this slice | This slice only needs to exist as a data source; consuming it is future work for S-02's follow-up and S-03 | Plan |
| Frontend architecture | Page-local inline state, no shared dialog/list/form components | Matches the existing precedent (`import.tsx`) — no such shared components exist in this codebase yet | Plan (research) |

## Scope

**In scope:**
- `Account` entity + additive migration, unique per (UserId, BankName, AccountNumber)
- `GET/POST/PUT/DELETE /api/accounts` + `GET /api/accounts/banks`, user-scoped, following the existing endpoint/antiforgery conventions
- New "Settings" page: account list, add/edit form, delete, nav link
- Integration tests covering create/duplicate/update/delete/list and cross-user scoping

**Out of scope:**
- Linking `Transaction`/`ImportBatch` to `Account` (no schema change to those tables)
- S-03's internal-transfer detection logic itself
- Revisiting S-02's manual-entry bank dropdown to consume this data
- Account nickname, currency, account type, or balance tracking

## Architecture / Approach

One new endpoint module (`AccountEndpoints.cs`, same `MapXEndpoints` convention as `ImportEndpoints`/`AuthEndpoints`) adds CRUD + a bank-options lookup over a new `Account` entity. A DB-level unique index enforces no duplicate bank+number per user; a violation on create/update returns 409 instead of a raw exception. The frontend adds one new page following `import.tsx`'s exact inline-state pattern — no new shared components, no schema change to any existing table.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend Account Entity + Endpoints | Migration, CRUD + bank-options endpoints, uniqueness constraint | Translating the unique-index violation into a clean 409 rather than a raw 500 |
| 2. Frontend Settings Page | New route/page, nav link, list + add/edit form, delete | Wiring the duplicate-conflict response into a clear inline error, no dialog component to reuse |
| 3. Integration Tests | Full endpoint coverage via `WebApplicationFactory` | None significant — reuses S-01's proven test harness |

**Prerequisites:** F-01 (minimal-auth-scaffold) merged — auth/session and user-scoping conventions this plan reuses. Does not depend on S-01/S-02.
**Estimated effort:** ~1 session across 3 phases; one small additive migration, one new entity, no dependent schema changes.

## Open Risks & Assumptions

- Bank-name list is sourced live from registered `IBankStatementParser`s, so it's mBank-only until S-07/S-08 register Revolut/Erste — "Other" covers everything else in the meantime.
- No account nickname means two accounts at the same bank are distinguished only by account number in any future dropdown (e.g. S-02's eventual account picker) — acceptable per the user's explicit choice, but worth remembering if that UX turns out to need a friendlier label later.
- This plan intentionally does not wire S-02 or S-03 to consume this data — those remain separate future changes that will need their own planning once this lands.

## Success Criteria (Summary)

- User can add, edit, and delete their own bank accounts from a new Settings page.
- The same bank + account number can never exist twice for one user — attempting it shows a clear error instead of a silent duplicate.
- The account list is ready to be consumed by S-02's follow-up (account picker) and S-03 (transfer detection) without further schema changes.
