<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Link Imported Transactions to an Account

- **Plan**: context/archive/2026-10-02-import-account-linking/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 4 observations

Reviewed by reading git history only (commits a1d687c, 8dbd9b6, 2c3712c, 5fe7f6b, all 2026-09-25). No build, test or npm command was run; the Success Criteria dimension is judged from Progress checkboxes and the diffs, not re-run.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Drift summary (plan vs landed commits)

- Phase 1: MATCH. DedupHash takes `Guid accountId` formatted with "D" (DedupHash.cs); `Bank` removed and `AccountId` + `Account` navigation added on Transaction and ImportBatch; both relationships `DeleteBehavior.Restrict` (AppDbContext.cs); migration `20260925120715_LinkTransactionsToAccounts` deletes Transactions then ImportBatches, drops `Bank`, adds non-null `AccountId` with FK Restrict and indexes. Table names are the quoted PascalCase names of the real schema, not the lowercase names in the plan prose.
- Phase 2: MATCH. `/parse` and `/commit` resolve the account with `a.Id == accountId && a.UserId == userId` and return 404 otherwise (ownership check present on both). `/commit` parser lookup removed. `BankMismatch` uses OrdinalIgnoreCase, as planned.
- Phase 3: MATCH. Required account select, blocking empty-state with a Settings link, `accountId` in the parse FormData and commit body, mismatch banner that does not disable Continue.
- Phase 4: MATCH. DedupHash, parse ownership, mismatch true/false, cross-account isolation and commit ownership tests were added. The planned AccountEndpoints delete case was added, together with the friendly 409 in AccountEndpoints.cs.

## Findings

### F1 — Accounts fetch on import page has no error handling

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/routes/import.tsx (loadAccounts in useEffect, added in 2c3712c)
- **Detail**: If `GET /accounts/` fails, `setAccountsLoading(false)` is never reached and the page shows "Loading…" indefinitely. This mirrors settings.tsx `loadAccounts`, so it follows the existing pattern.
- **Fix**: Wrap in try/catch/finally and surface the error through the existing `error` state.
- **Decision**: PENDING

### F2 — Account-delete behaviour from this change was later reversed

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Transactions/AccountEndpoints.cs (5fe7f6b)
- **Detail**: 5fe7f6b added a proactive linked-history check plus a `DbUpdateException` catch returning 409, as the plan allowed. Commit f143331 (fix/account-delete-with-transactions) later changed delete to confirm and cascade-delete the account's transactions, which departs from the plan's Restrict-and-fail intent. This is later evolution, not a defect in the reviewed commits. The `Restrict` FK in AppDbContext.cs remains.
- **Fix**: None required; keep in mind that the plan's "Restrict" rationale no longer describes current delete behaviour.
- **Decision**: PENDING

### F3 — New tests lack Arrange/Act/Assert markers and a shared setup

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Tests/ImportEndpointsTests.cs (e.g. Parse_SameTransactionDataUnderDifferentAccount_IsNotFlaggedAsDuplicate), AccountEndpointsTests.cs (Delete_ForAccountWithLinkedTransactions_ReturnsConflictWithoutThrowing)
- **Detail**: The tests added in 5fe7f6b have no `// Arrange/Act/Assert` comments and repeat factory, client and account creation. The lessons.md rule on this was added in 574bec8 on 2026-10-02, after these commits, so this is not a violation at the time of the change.
- **Fix**: Apply the lesson if these tests are touched again.
- **Decision**: PENDING

### F4 — Manual checks 3.3-3.6 share one commit SHA; 3.6 is not backed by that commit

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress, items 3.3-3.6 (all tagged 2c3712c)
- **Detail**: The 2c3712c diff shows code supporting 3.3 (empty state), 3.4 (required select, accountId in FormData) and 3.5 (banner, Continue unchanged). Item 3.6 (same file against two accounts is not a duplicate) is backend behaviour and is evidenced by test Parse_SameTransactionDataUnderDifferentAccount_IsNotFlaggedAsDuplicate in 5fe7f6b, not by the frontend commit. No evidence of a real in-browser run is recorded, but the code and tests support each claim, so this is not rubber-stamping.
- **Fix**: None required.
- **Decision**: PENDING
