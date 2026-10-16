<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Account Management Implementation Plan

- **Plan**: context/archive/2026-09-25-account-management/plan.md
- **Scope**: Full plan (Phases 1-3 of 3)
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 3 observations

Method: read-only review of the landing commits d1a4a17 (phase 1), ef8606f (phase 2) and d26e964 (phase 3), all dated 2026-09-25, plus `context/foundation/lessons.md`. Code at HEAD was not used as the baseline. No build or test was run.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Plan vs. diff: every planned file appears in the landing commits (Account.cs, AccountContracts.cs, AccountEndpoints.cs, AppDbContext.cs, AddAccounts migration, routes.ts, AppHeader.tsx, settings.tsx, AccountEndpointsTests.cs). The only unplanned files are `Program.cs` (the planned `MapAccountEndpoints` registration), the migration Designer and snapshot (generated), and docs updates (`plan.md`, `change.md`, `roadmap.md`). The "What We're NOT Doing" list is respected: Transaction and ImportBatch are untouched and there is no nickname, currency or shared component.

Success Criteria: this is a retrospective review, so no automated checks were re-run. Progress items 1.1-1.7, 2.1-2.2 and 3.1 are marked [x] with commit SHAs. The diffs contain matching evidence: the AddAccounts migration, 10 endpoint tests covering the planned scenarios (banks, create, duplicate 409, cross-user create, list scoping, update self-exclusion, update 409, update 404, delete, delete 404), and the frontend build files. Manual items 2.3-2.7 cannot be proven from a diff. The UI logic for each (409 inline error `settings.tsx:165`, edit prefill `:75-80`, two-step delete `:211-236`, empty state `:192`, nav link) is present, so there is no sign of rubber-stamping.

## Findings

### F1 — Unique-index violation is not translated on concurrent duplicates

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Transactions/AccountEndpoints.cs:52-68 and :89-98 (as landed in d1a4a17)
- **Detail**: Duplicates are only caught by an `AnyAsync` pre-check, and `SaveChangesAsync` is not wrapped. Two concurrent identical POST/PUT requests (for example a double-click on submit) can both pass the check, and the second then hits the unique index and surfaces a raw `DbUpdateException` as a 500. The plan allowed a pre-check, but the comment in AppDbContext.cs says "endpoints translate a violation into a friendly 409", which is not true for the race. The EF InMemory provider used in the tests does not enforce the index, so the tests cannot cover this.
- **Fix**: Wrap `SaveChangesAsync` in create/update with a `catch (DbUpdateException)` that returns the same 409 problem response.
- **Decision**: SKIPPED (2026-10-16, user: risk accepted, concurrent duplicate is unlikely)

### F2 — Settings page has no error handling on initial load or delete

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/routes/settings.tsx:55-66 and :111-115 (as landed in ef8606f)
- **Detail**: `loadInitial` and `handleDelete` have no try/catch. If `/accounts/banks` or `/accounts/` fails, `setLoading(false)` is never reached, so the page shows "Loading…" forever, with an unhandled rejection. If DELETE fails, the user sees nothing and the confirm state stays open. Only `handleSubmit` (:104-108) surfaces errors. The plan specified error handling for 409 only, so this is a gap in the plan as well.
- **Fix**: Add try/catch with `finally` for `setLoading(false)` in `loadInitial`, and use `setFormError(await extractErrorMessage(err))` in `handleDelete`.
- **Decision**: SKIPPED (2026-10-16, user)

### F3 — No server-side validation of request fields

- **Severity**: 👁 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Transactions/AccountEndpoints.cs:44-72, :74-101
- **Detail**: `AccountWriteRequest` is persisted without checks for null, empty or whitespace values, a maximum length, trimming, or membership of `BankName` in the `/accounts/banks` list. Only the frontend `required` attributes (`settings.tsx:135,158`) guard this. Because of the lack of trimming, "111" and "111 " count as distinct accounts. This matters for S-03 transfer matching, which will compare account numbers. The plan did not specify validation.
- **Fix**: Trim the values and return 400 for blank input, with a length cap. Consider normalising account numbers (spaces) before the uniqueness check.
- **Decision**: PENDING

### F4 — Tests do not use Arrange/Act/Assert or shared setup

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Tests/AccountEndpointsTests.cs:81-221
- **Detail**: Each test creates its own factory and client, and has no `// Arrange/Act/Assert` markers. The lesson requiring them was added on 2026-10-02 (commit 574bec8), after this change landed, so it is not a violation. The tests match the sibling AuthEndpointsTests/ImportEndpointsTests style at the time. Noted only in case the file is touched again.
- **Fix**: Apply the lesson when the file is next edited.
- **Decision**: PENDING

### F5 — Later evolution: accounts now linked to transactions

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: N/A (history: f143331 "confirm and cascade-delete an account's transactions", 2026-10)
- **Detail**: The plan's DELETE was a plain row removal and explicitly did not link Account to Transaction. A later commit added cascade deletion of an account's transactions, so the plan's non-goal was later superseded by a separate change. This is not a defect of this change. Informational only, since the review is judged against the landing commits.
- **Fix**: None needed.
- **Decision**: PENDING
