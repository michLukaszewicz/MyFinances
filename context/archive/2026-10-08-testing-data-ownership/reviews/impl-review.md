<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Data Ownership Tests (Risk #3)

- **Plan**: context/archive/2026-10-08-testing-data-ownership/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warnings, 3 observations

Method: read-only review of code and git history. Landing commits: fb4a3ad (phase 1), 379fc16 (phase 2), 4d7bb46 (phase 3), plus c9da821 and 166a046 (SHAs, archive). Build, tests and mutation checks were not re-run. The code at HEAD has evolved since (commit 5949e5b, per-user categories), so findings are judged against the landed versions of the files.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Inventory lists POST /api/accounts/ as ownership-tested without a cross-user test

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Tests/Ownership/EndpointOwnershipInventoryTests.cs:34 (as landed in 4d7bb46)
- **Detail**: `POST /api/accounts/` sits in the `OwnershipTested` set, and the guard message promises "add a cross-user test, then list them here". In the landed Tests/Ownership files no test calls this endpoint as B against A's data. It is only used as a setup helper (Tests/Support/ImportTestHelpers.cs:35). The classification therefore claims coverage that does not exist, and the guard cannot detect that. The test-plan limitation text ("inventory guard proves classification, not test sufficiency") acknowledges the general gap but not this case. The practical risk is low, because creation is bound to the caller's id, but the plan's own intent is that "ownership-tested" is truthful.
- **Fix**: Add a small test, for example B creates an account with A's account number and A's account list and snapshot stay unchanged, or move the endpoint to a documented "creates only under the caller" classification.
- **Decision**: SKIPPED (2026-10-16, user)

### F2 — Plan mutation spot-checks (2.2, 3.2) have no evidence in the diff

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress 2.2, 3.2
- **Detail**: These steps are marked `[x]` with SHAs b7af5ae and b1b81a2, but they are temporary mutations that were reverted, so by design the diff holds no trace of them. Nothing in the repo records that the mutations were run or which tests failed. Evidence that the tests can fail does exist in the diff: `OwnerCanStillChangeOwnData_ControlForTheDenialTests` (DataOwnershipWriteTests.cs:155-165) proves the snapshot comparison can detect a change, and the inventory guard asserts both unclassified and stale endpoints. The SHAs also do not match any commit visible in `git log` for this change (landing commits are fb4a3ad, 379fc16, 4d7bb46). They were probably rewritten when c9da821 recorded them or were lost in the squash or merge. Not a defect, only weak traceability.
- **Fix**: Optionally note the mutation results in the plan or PR description next time.
- **Decision**: PENDING

### F3 — Several tests carry only Act/Assert sections

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Tests/Ownership/DataOwnershipWriteTests.cs (e.g. AccountsPut, TransactionsDelete) and DataOwnershipReadTests.cs (AccountsList, dashboards)
- **Detail**: The lessons rule asks for `// Arrange`, `// Act` and `// Assert` in every test with shared setup moved to `IAsyncLifetime`. The shared setup is correctly in `InitializeAsync` (both classes), and tests that have nothing to arrange omit the Arrange section. This is consistent with the lesson's intent ("Arrange holds only what differs") and is not a violation in substance.
- **Fix**: None required.
- **Decision**: PENDING

### F4 — Later evolution: categories became per-user after this change

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: commit 5949e5b (touches Tests/Ownership)
- **Detail**: The plan's "What We're NOT Doing" (no ownership test for categories, global by design) and the landed inventory (`GET /api/categorization/categories` in `GlobalNoUserData`) were correct when merged. At HEAD, per-user categories moved those endpoints into `OwnershipTested` and added category tests (for example CategoriesDelete and CategoriesList custom-category isolation). The inventory guard did its job here by forcing the reclassification. This is not a defect of this change. It is noted as evidence that the guard works.
- **Fix**: None.
- **Decision**: PENDING

## Plan vs. diff summary

- Phase 1: `Tests/Support/TwoUserHarness.cs` added as planned. A registers via the existing flow, B via `UserManager<AppUser>` and logs in on a separate client. Seed helpers and `SnapshotAsync` over accounts, transactions and import batches are present. The smoke test `Me_ReturnsDifferentEmailsForTheTwoSessions` was added. `git diff --name-only b11a5c5..166a046` shows no non-test production files, so "no product code changes" holds (MATCH).
- Phase 2: WriteTests cover PUT/DELETE account, PUT/DELETE transaction, categorize with category and transfer flag, POST and PUT transaction with A's `accountId`, import parse and commit. Each denial asserts the exact status (404 or 400) and `AssertNobodysDataChangedAsync` compares both users' DB snapshots. The owner control test proves the comparison is able to fail. ReadTests cover lists, a filtered list, queue, handled, the three dashboards, authored sums (30.00, 100.00, 3000.00), cross-user dedup (not flagged duplicate) and a cross-user opposite-amount pair that is not an internal transfer. Expected values are authored literals, not read back from the code under test. No trivially-passing tests were found. The denial tests assert state, not only status (MATCH).
- Phase 3: EndpointOwnershipInventoryTests reads `EndpointDataSource`, requires authorization except for the anonymous allow-list, and checks classification both ways (unclassified and stale). It also checks that no endpoint is in two sets. The `test-plan.md` §3 row, §6.3 and §6.4 were updated. Minor deviation: the plan said "anonymous request to each endpoint group returns 401". It is implemented as an authorization-metadata check plus an anonymous 401 test over the read endpoints only (ReadTests `EveryReadEndpoint_WithoutASession_IsUnauthorized`). The metadata check covers the intent (MATCH with a benign variation).
- Scope: no EXTRA product or test-plan changes beyond the planned files.

## Success Criteria (retrospective)

Automated commands were not re-run in this retrospective review. Progress checkboxes 1.1-3.4 are all `[x]` with commit SHAs. The test and cookbook artifacts they refer to are observable in the diff, except the reverted mutation checks (see F2).
