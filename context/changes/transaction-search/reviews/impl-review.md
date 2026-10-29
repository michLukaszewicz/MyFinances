<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Search transactions by description

- **Plan**: `context/changes/transaction-search/plan.md`
- **Scope**: Full plan (CI review on PR #58)
- **Date**: 2026-10-29
- **CI run**: https://github.com/michLukaszewicz/MyFinances/actions/runs/40875929194
- **Verdict**: REJECTED
- **Findings**: [4 critical] [0 warnings] [0 observations]

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Test Coverage | FAIL |
| Success Criteria | PASS |

## Findings

### F1 — `search` is concatenated into raw SQL (SQL injection)

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; the rewrite is small but the escaping decision needs a look
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Transactions/TransactionEndpoints.cs:53
- **Detail**: `FromSqlRaw` builds the query by concatenating the raw `search` value (and `userId`) into the SQL string: `"... AND \"Description\" ILIKE '%" + search + "%'"`. A value containing a single quote breaks out of the literal. The build emits EF1003 ("FromSqlRaw inserts concatenated strings directly into the SQL") on this line, so the warning was present in CI and not acted on. Separately, `%` and `_` typed by the user act as ILIKE wildcards.
- **Fix**: Replace the raw SQL with a LINQ predicate applied on top of the existing user scope: `db.Transactions.Where(t => t.UserId == userId)` followed by `.Where(t => EF.Functions.ILike(t.Description, $"%{search}%"))`.
  - Strength: EF parameterizes the value, which removes the injection class. It also keeps the filter composable with the category, period and kind filters below it.
  - Tradeoff: Wildcard characters still act as wildcards unless escaped. Escape `\`, `%` and `_` and pass an ESCAPE clause if literal matching is required.
  - Confidence: HIGH — the plan requires this shape and the project already composes filters with LINQ in the same handler.
  - Blind spot: Not executed against Postgres in this review; the `EF.Functions.ILike` translation is assumed from Npgsql.
- **Decision**: PENDING

### F2 — Plan forbids raw SQL; filter must be LINQ / EF Core

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — same change as F1; batch with it
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Transactions/TransactionEndpoints.cs:51
- **Detail**: Plan, Changes Required item 1: "Filter with LINQ / EF Core only (parameterized), never by concatenating user input into SQL." Actual: the search branch uses `db.Transactions.FromSqlRaw(...)`, and the other filters in the same handler are LINQ `Where` chains. The raw-SQL branch is also a second query shape inside one handler, which is why pattern consistency is flagged WARNING.
- **Fix**: Apply the LINQ rewrite from F1 so the search filter is a `Where` like the `categoryId`, period and kind filters.
- **Decision**: PENDING

### F3 — Planned tests were not added

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — three tests with a clear spec
- **Dimension**: Test Coverage
- **Location**: MyFinances/backend/Tests/Transactions/TransactionEndpointsTests.cs (expected test additions; file not modified)
- **Detail**: Plan, Changes Required item 2 commits to three tests: case-insensitive match, no leakage of another user's transactions, and a `search` over 100 characters returning 400. The test file is not in the diff and contains no reference to `search`. `dotnet test` passes 628/628, but none of those exercise the new behaviour, so the PR's own automated checks say nothing about it.
- **Fix**: Add the three tests from the plan to `TransactionEndpointsTests.cs` and run `dotnet test` again.
- **Decision**: PENDING

### F4 — 100-character limit on `search` is not enforced

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — one guard clause in the handler
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Transactions/TransactionEndpoints.cs:31
- **Detail**: Plan, Changes Required item 1: "Reject a `search` longer than 100 characters with 400 Bad Request." The handler accepts `string? search` with no length check.
- **Fix**: Return 400 Bad Request when `search.Length > 100`, before the query is built.
- **Decision**: PENDING

## Automated checks

| Command (from plan) | Result |
|---------------------|--------|
| `dotnet build` (MyFinances/backend) | PASS, exit 0; 4 warnings, including EF1003 on TransactionEndpoints.cs:53 (see F1) |
| `dotnet test` (MyFinances/backend) | PASS, 628 passed, 0 failed, 0 skipped |

Manual Verification (`GET /api/transactions?search=...` in Swagger) is unchecked, so it is pending and not graded.

<!-- End of report -->
