<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Transaction History View Implementation Plan

- **Plan**: context/archive/2026-09-25-transaction-history-view/plan.md
- **Scope**: Full plan (Phases 1 and 2 of 2)
- **Reviewed phases**: 1, 2
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warnings, 2 observations

Method: read-only review of the landing commits `8855671` (phase 1, backend) and `60e7e58` (phase 2, frontend), judged against the plan. No build, test or other command was run. Later evolution of the code at HEAD (for example the per-user categories added in `5949e5b`) is not judged here.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Failed initial transaction fetch shows the "no transactions yet" message instead of an inline error

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/routes/home.tsx:68 (loader catch), :79 and :153 (component), :237 (empty-state text), as of commit 60e7e58
- **Detail**: The plan (Phase 2, item 1) says a failed transaction fetch returns `transactions: null` and "the component below turns `null` into an inline error message rather than crashing". The loader does return `null` (line 68). The component never distinguishes it from an empty list: `items` falls back to `[]` (line 79), `hasTransactions` is false (line 153), and the user sees "You haven't imported any transactions yet" (line 237). The only error message implemented is `loadMoreError`, for Load more. A backend failure on the first page therefore tells a user with existing data that they have none. Login state is preserved, as planned.
- **Fix**: Render an inline `text-sm text-red-600` error when `initialTransactions === null` and the user is authenticated, instead of the empty-state block.
- **Decision**: FIXED (2026-10-16, home.tsx shows an inline error when the initial fetch fails; typecheck passed)

### F2 — Non-null `categoryId` is rendered as a raw GUID

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/routes/home.tsx:203 (commit 60e7e58)
- **Detail**: The plan only specifies the "Uncategorized" label for `CategoryId == null` and says no category names exist until S-03. At landing time every transaction was uncategorized, so this branch was unreachable and harmless. If a row ever had a non-null `categoryId`, the UI would print the GUID. The plan did not specify this branch. Later category work is out of scope for this review.
- **Fix**: Show a neutral placeholder for non-null ids until category names are available.
- **Decision**: PENDING

### F3 — New test file has no Arrange/Act/Assert sections

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Tests/TransactionEndpointsTests.cs (6 `[Fact]` methods, for example lines 71-189 in commit 8855671)
- **Detail**: `context/foundation/lessons.md` now holds a rule requiring `// Arrange`, `// Act`, `// Assert` markers and shared per-class setup. The lessons file's own history shows the earliest entries dated 2026-09-24/25, so this rule may not have existed when the change landed (not verified against the exact commit). This is an observation, not a violation. The tests themselves cover every case the plan lists: 401, user scoping, ordering, skip/take with `HasMore` true then false, empty result, last page. Setup is repeated per method (`new AuthApiFactory()`, client, account), which is the same repetition the lesson describes.
- **Fix**: Add the section markers if the file is touched again; no action needed for the archived change.
- **Decision**: PENDING

## Plan adherence (drift analysis)

Phase 1:
- `TransactionContracts.cs`: MATCH. The two records are identical to the plan's contract.
- `TransactionEndpoints.cs`: MATCH. It uses `GetUserId` scoping, default 20, clamp 1..100 and a negative-skip guard, `OrderByDescending(Date).ThenByDescending(Id)`, a separate `CountAsync`, and `HasMore = skip + count < total`. It sits under the `/api` group with no antiforgery filter.
- `Program.cs`: MATCH. `api.MapTransactionEndpoints();` is added after the import endpoints (and after the account endpoints that landed in between).
- `TestClientHelpers.cs` and the `ImportEndpointsTests.cs` call-site updates: MATCH.
- `TransactionEndpointsTests.cs`: MATCH. Account seeding satisfies the required `AccountId` FK, as the plan's update note anticipated.

Phase 2:
- `home.tsx` interface, `clientLoader` and return shape, `useState` set (`items`, `hasMore`, `loadingMore`, `loadMoreError`), `skip` derived from `items.length`, the "Load more" and "Loading…" label with `disabled`, the red inline load error, the container widening to `max-w-2xl`, sign coloring and the "Uncategorized" label: MATCH, except the initial-failure message (F1).
- EXTRA: `formatAmount` (2-decimal locale formatting) is not described in the plan. It is benign and tiny.

Scope Discipline: PASS. None of the "What We're NOT Doing" items were done: no Category table, no bank or account column, no new route, no filters, no infinite scroll. The commits also touched `change.md`, `plan.md` and `roadmap.md` status lines, which is normal bookkeeping.

## Safety and pattern scan

- Auth and scoping: the endpoint is under the `RequireAuthorization()` group and filters by `UserId`. There is no IDOR surface because there is no id parameter.
- Unbounded query: `take` is clamped to 100 and `skip` is floored at 0. A very large `skip` is harmless.
- N+1: none; the query is a single projection plus one count. The count runs as a separate query, so `HasMore` can be marginally inconsistent under concurrent inserts. The plan already documents the offset-pagination drift as accepted.
- Frontend: all values are rendered through React text nodes, so no XSS. The `apiFetch` convention is followed.
- Pattern consistency with `ImportEndpoints.cs` and `import.tsx`: matches (extension-method mapping, record DTOs, row layout, red error text).

## Success Criteria

This is a retrospective review, so the automated checks (`dotnet build`, `dotnet test`, `npm run typecheck`, `npm run build`) were not re-run. They are marked `[x]` with commit shas in Progress, and the commit messages report them as run.

Manual items:
- 1.3 and 1.4: ticked with sha `8855671`. The diff has supporting evidence in the integration tests for user scoping and ordering (`List_OnlyReturnsCurrentUsersTransactions`, `List_ReturnsTransactionsNewestFirst`).
- 2.4, 2.5, 2.7: ticked with sha `60e7e58`. The commit message reports a live dev-server smoke check (ordering, sign colors, Load more, mobile viewport). The diff supports 2.4 and 2.5 directly in code.
- 2.3 (zero-transaction empty state) and 2.6 (refresh after import): ticked later by the user after manual validation, with no sha. The commit message of `60e7e58` says both were not verified live at the time. The diff supports 2.3 in code (the empty-state block is kept unchanged in the `else` branch, around line 232 onward), and 2.6 by design (the data comes from `clientLoader`, which re-runs on navigation). There is no automated test and no recorded live evidence for either. They are treated as user-confirmed.
- One side note on 2.6: `items` and `hasMore` are seeded by `useState` from loader data. This works when navigating from `/import` back to `/`, because `Home` remounts. It would show stale rows if the loader were revalidated while `Home` stayed mounted. This is not a defect for the planned flow.
