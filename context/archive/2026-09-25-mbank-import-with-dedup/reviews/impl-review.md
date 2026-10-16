<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: mBank Import With Duplicate Resolution

- **Plan**: context/archive/2026-09-25-mbank-import-with-dedup/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5, 6, 7
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 4 observations

Method: code and git history only (landing commits 30d6704, 53a139c, 490f3ac, e099558, 335f5f1, f1bc154, 229323e, 5fe7f6b). No build, test or sub-agents were run. Code was judged as it was at the landing commits; later evolution is noted only as observation.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

Success Criteria: automated checks were not re-run (retrospective). All Progress boxes are `[x]` with a commit SHA. Evidence in the diff exists for most items (parser tests in 53a139c, endpoint tests in 490f3ac, and two manual-test-driven fix commits f1bc154 and 229323e that show the browser flow was really exercised). Two items lack clean evidence, see F1 and F5.

## Findings

### F1 — Phase 7 has no commit of its own; its SHA points to another change and the planned re-import test is only indirectly covered

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md:442-443 (Progress 7.1, 7.2); MyFinances/backend/Tests/ImportEndpointsTests.cs
- **Detail**: Progress 7.1/7.2 cite 5fe7f6b, which belongs to the `import-account-linking` change (its own plan.md is in that commit), not this change. The Phase 7 test file `ImportEndpointsTests.cs` was actually created in 490f3ac (Phase 3), whose header comment says "Phase 7 adds full fixture-driven" coverage, but no later commit of this change adds it. The plan's Phase 7 case "second import of the same file against a database already seeded with one of its rows, flagged exactly" is covered only by the f1bc154 regression test `Parse_WhenTwoStoredTransactionsShareTheSameHash_DoesNotThrow` (seeds two rows with one hash and asserts the matching rows are `IsDuplicate`; it does not assert that the other rows are not flagged). The malformed-row case is covered at parser level (`MBankCsvParserTests.Parse_SkipsMalformedRow_WithoutAbortingRestOfFile`) and in the commit test (SkippedErrorCount echo), not as an endpoint fixture test. 7.1/7.2 were therefore checked off by reference to a different change's commit.
- **Fix**: Record in a follow-up note that Phase 7 coverage landed in 490f3ac and f1bc154, and that the "exactly the expected duplicates" assertion was strengthened later by the testing-import-integrity-dedup change.
- **Decision**: ACCEPTED (2026-10-16, user: indirect coverage is enough)

### F2 — Commit endpoint re-hashes but does not validate the client rows it persists

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/ImportEndpoints.cs (commit handler, `keepRows` loop with `db.Transactions.Add`, at f1bc154)
- **Detail**: The plan (Phase 3, item 3) says the server "re-validates and re-hashes every row". The implementation recomputes the hash and the skipped-duplicate count, but persists every `Keep` row exactly as sent: no check on `Description` (null or very long text, which can fail the `required` column and return 500), no bound on the number of rows, and no check that rows came from a prior parse. `SkippedErrorCount` is echoed unvalidated (the plan accepts this). Impact is limited to the caller's own data: `UserId` comes from the principal, the antiforgery filter is present, and the body is capped at 5 MB, so this is data quality and robustness rather than cross-user exposure.
- **Fix**: Add a minimal server-side guard in the commit handler: reject rows with an empty or over-length `Description`, cap `Rows.Count`, and return 400 for them.
  - Strength: Makes the plan's "server is not a trust boundary" statement true for persisted fields, not only for the hash.
  - Tradeoff: A few lines plus tests; the limits (description length, row cap) need picking.
  - Confidence: MED — the limits are a product decision, not derivable from the plan.
  - Blind spot: Did not check whether a column max length is configured in the migration (30d6704).
- **Decision**: FIXED (2026-10-16, commit handler rejects empty or over-1000-char Description and more than 10000 rows with 400; 3 tests added; Tests.Import 429 passed)

### F3 — `IBankStatementParser.Parse` returns `ParseResult`, not `IReadOnlyList<NormalizedTransaction>`

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Import/IBankStatementParser.cs; MyFinances/backend/Import/MBankCsvParser.cs:55
- **Detail**: The plan's contract lists `IReadOnlyList<NormalizedTransaction> Parse(Stream)`, but the plan also requires skipped malformed rows to be counted and surfaced as `skippedErrorCount`, which a bare list cannot carry. A `ParseResult(transactions, skippedErrorCount)` type was introduced. Justified but not documented in the plan. Also extra but benign: `Import/ImportContracts.cs`, `DI/ImportServiceCollectionExtensions.cs`, and the throw in `ClaimsPrincipalExtensions.GetUserId`.
- **Fix**: None needed; optionally note in a plan addendum.
- **Decision**: PENDING

### F4 — A planned design consequence surfaced as a bug during manual testing

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Import/ImportEndpoints.cs (parse handler, `existingByHash`); commit f1bc154
- **Detail**: The plan makes `Transaction.Hash` deliberately non-unique, but the first parse implementation built a dictionary keyed by hash, which throws when two stored rows share a hash (the real BLIK case kept twice). It was caught in manual testing and fixed in f1bc154 with an in-memory `GroupBy(...).First()` plus a regression test. Final behaviour matches the plan. The second fix commit 229323e ("Skip all" commits immediately) is unplanned UI behaviour, benign.
- **Fix**: None; already fixed and covered by a test.
- **Decision**: PENDING

### F5 — Antiforgery behaviour of the multipart parse endpoint has no automated test

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: MyFinances/backend/Program.cs (`app.UseAntiforgery()` added in 30d6704); plan.md:380 (Progress 1.5)
- **Detail**: Progress 1.5 (multipart POST without token returns 400, not 500) was a manual check with a throwaway endpoint that left no trace in the diff. The commit endpoint has `Commit_WithoutAntiforgeryToken_ReturnsBadRequest`, but no test posts a multipart file to `/api/import/parse` without a token. The real parse endpoint relies solely on the `UseAntiforgery()` middleware (no manual filter), so a regression there would not be caught.
- **Fix**: Add one test: `/api/import/parse` without `X-XSRF-TOKEN` returns 400.
- **Decision**: PENDING

### F6 — Test style differs from the later project lesson

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Tests/ImportEndpointsTests.cs (at 490f3ac and f1bc154)
- **Detail**: Tests landed without Arrange/Act/Assert markers and with factory/client setup repeated per test, which the later lesson in context/foundation/lessons.md asks for. The lesson was written after this change, so this was not a violation at the time; commit 8a32391 later added the markers. Later evolution: tests were extended by import-account-linking (5fe7f6b) and testing-import-integrity-dedup.
- **Fix**: None needed.
- **Decision**: PENDING
