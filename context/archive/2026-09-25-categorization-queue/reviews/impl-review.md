<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Categorization Queue (S-03)

- **Plan**: context/archive/2026-09-25-categorization-queue/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 4 observations

Method: code and git history only (commits 5c6e9a3/e3e3447 p1, 8a41d84/66a0e54 p2, 2aab1be p3; the plan's recorded SHAs and the branch SHAs differ because of a later merge/rebase). No build, test or app run. This is a retrospective review: automated checks were not re-run; the plan's Progress records them as passed (1.1-1.3, 2.1-2.4 with 57/57 tests, 3.1-3.2).

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Drift summary (plan vs. landed commits)

- Phase 1: MATCH. Category entity (Categorization/Category.cs), Transaction.Category + IsInternalTransfer + TransferFlagManuallySet (Transaction.cs), DbSet + Restrict FK + 12 seeded categories with deterministic Guids and SortOrder 1-12 (AppDbContext.cs), migration 20260925125557_AddCategorization.
- Phase 2: MATCH. Contracts, TransferDetectionService (predicate: different AccountId, Amount == -Amount, DayNumber diff <= 2, first-match-wins, skips TransferFlagManuallySet rows at TransferDetectionService.cs:22), four endpoints with own private antiforgery filter, registration in Program.cs (+ AddScoped for the service). Detection runs before the query in both list handlers (CategorizationEndpoints.cs:37, :57). PUT sets TransferFlagManuallySet = true only when IsInternalTransfer is provided (:103). Both user-scoped (UserId filter) and cross-user isolation tested. Unit tests cover every plan bullet (7 tests); integration tests cover queue exclusion, PUT persistence, survive-detection, auto-flag, cross-user.
- Phase 3: MATCH. routes.ts +1 line, AppHeader.tsx +1 Link between Import and Settings, categorize.tsx follows settings.tsx shape (clientLoader guard, DTO comments, apiFetch, extractErrorMessage).
- "Not doing" list respected in the landing commits: no category CRUD, no bulk, no optimal matching, no edits to TransactionContracts/TransactionEndpoints/home.tsx.

## Success Criteria (retrospective)

- Automated: not re-run (retrospective). Test files present in the diff match the plan's list.
- Manual 1.4: marked done at landing.
- Manual 2.5, 2.6, 3.3, 3.4, 3.5: at landing the annotations said "not verified live" (port collision with the S-09 preview; 3.3-3.5 were unchecked in commit 2aab1be and only checked later in the archived plan). The user has since confirmed manual validation of these items; recorded here as user-confirmed manual validation, not rubber-stamping. Supporting evidence in the diff: integration test Queue_And_Handled_RunDetectionAndAutoFlagMatchingPair (auto-flag, 2.5), Put_SetsInternalTransfer_PersistsAndSurvivesLaterDetectionPass (override persists, 2.6), queue query has no forcing logic and the form submit is optional (3.5). 3.3 and 3.4 rely on frontend behaviour with no frontend test in the diff, so the user's manual confirmation is the only evidence.

## Findings

### F1 — Transfer detection mutates data inside GET handlers

- **Severity**: 👁 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Categorization/CategorizationEndpoints.cs:37, :57 (calls) and TransferDetectionService.cs:22-60
- **Detail**: GET /queue and GET /handled call SaveChangesAsync through DetectAsync, so a read endpoint writes and is not protected by the antiforgery filter. This is the design chosen in the plan (state sequencing, Performance Considerations) and is idempotent and user-scoped, so it is not drift. Cost is O(n^2) over the user's non-manual transactions per request, executed twice per page load (loadAll calls both endpoints in parallel, categorize.tsx loadAll), so two concurrent detection passes can race on the same rows (benign outcome, same result, but double work).
- **Fix**: If volume grows, trigger detection after import commit or from a single endpoint instead of both GETs.
  - Strength: Removes writes from reads and the double pass.
  - Tradeoff: Touches the import module the plan deliberately avoided.
  - Confidence: MED — depends on real per-user volume.
  - Blind spot: Import-commit hook was not examined.
- **Decision**: PENDING

### F2 — Unplanned test-host change in AuthEndpointsTests

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/backend/Tests/AuthEndpointsTests.cs (commit 66a0e54, +15 lines, CreateHost override with EnsureCreated)
- **Detail**: Not listed in the plan. Needed so the InMemory provider applies the HasData seed that the new tests depend on; explained in the code and benign, but the plan's file list does not mention it.
- **Fix**: None needed; optionally note in the plan as an addendum.
- **Decision**: PENDING

### F3 — Transfer override is per-leg and category cannot be cleared via the API

- **Severity**: 👁 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: CategorizationEndpoints.cs:87-104; categorize.tsx:106, :129
- **Detail**: (a) Un-flagging one auto-detected leg sets TransferFlagManuallySet only on that row; its partner stays flagged as a transfer. The plan specifies per-transaction override, so this matches, but a user may expect both legs to change. (b) A null CategoryId in the PUT body is ignored (per plan: "omitted/null leaves unchanged"), so a categorized item can be re-categorized (FR-015) but never returned to uncategorized. Not required by the plan or FR-015.
- **Fix**: Only if wanted, add an explicit "clear category" contract and/or pair-aware override as a separate change.
- **Decision**: PENDING

### F4 — Later evolution (observation only, not judged)

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: git history after the landing: PR #18 (657bd9e), PR #49 (5949e5b)
- **Detail**: PR #18 (post-merge UI fixes) edited TransactionContracts.cs, TransactionEndpoints.cs and home.tsx, which this plan listed as out of scope while S-09 was unmerged; it is a separate PR after both slices existed, so no violation. PR #49 later replaced the fixed global category list with per-user categories, superseding the seed design reviewed here.
- **Fix**: None.
- **Decision**: PENDING
