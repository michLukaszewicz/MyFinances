<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Import Integrity and Dedup Test Rollout (Phase 1)

- **Plan**: context/archive/2026-10-02-testing-import-integrity-dedup/plan.md
- **Scope**: Full plan (Phases 1, 2, 3 of 3)
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 4 observations

Method: read-only review of the plan and the landing commits on main (d67a2e9 p1, 42ef628 p2, 8a32391 test reorganisation, 574bec8 p3, 208108d epilogue). No build or tests were run. Code at HEAD was only consulted for later evolution.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success Criteria note: retrospective review, automated checks were not re-run. All 9 Progress items are `[x]` with a commit SHA. The automated items are backed by the committed test files (ImportDedupIntegrityTests: 6 tests, ImportCrossFormatOverlapTests: 7 tests). The two mutation checks (2.3, 3.3) are transient by nature and leave no evidence in the diff. Item 3.4 is verifiable and holds: test-plan.md section 6.2 is filled and names both new test files and the helper (574bec8).

Test quality note: tests assert the behaviour listed in the plan, and expected values come from authored rows (`RepeatedRow`, `SecondRow`, `ThirdRow`, `MBankPairedStatement.Transactions`), not from a first parse. Every planned scenario is present: full re-import, partial overlap, skip on a non-duplicate, keep on a flagged duplicate, manual-entry match, the repeated-rows limitation, fixture sanity, both overlap orders, same-format controls (CSV and PDF), the warn-only commit and the partial period. The optional "other account" scenario was skipped, which the plan allows. Assertions check the persisted state (`GetStoredAsync`, amount sums) and not only HTTP responses. I found no trivially-passing tests. `ImportCrossFormatOverlapTests.cs` asserts both `ExpectedOverlap(...)` and a literal count, and adds `Assert.NotEqual(All.Count, ...)` in the partial-period test, so the expected value is not circular.

## Findings

### F1 — Existing test files moved and rewritten despite "no changes to ImportEndpointsTests.cs"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: Tests/Import/ImportEndpointsTests.cs (commit 8a32391; plan "What We're NOT Doing", last bullet)
- **Detail**: The plan says "No changes to `Tests/Import/ImportEndpointsTests.cs`". Commit 8a32391 (a plain `test:` commit, not tagged with the change) moved 23 test files into folders and added 1128 lines of Arrange/Act/Assert markers, including +202 lines in `ImportEndpointsTests.cs`. The plan's Amendments section records the folder move and says existing tests "were not otherwise rewritten". It does not mention that A/A/A markers were added to every existing test class. The change is benign and behaviour-neutral, but it widens the diff well beyond the two new test classes, and the plan text does not describe it accurately.
- **Fix**: Amend the plan (archived copy stays as is) or note in the change record that 8a32391 also added A/A/A markers to all existing test classes.
- **Decision**: FIXED (2026-10-16, plan Amendments updated)

### F2 — Manual mutation checks have no durable evidence

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress 2.3 and 3.3
- **Detail**: Both checks (break `DedupHash.ComputeHash`, make the overlap query ignore `SourceFormat`) are marked `[x]` with only a commit SHA. Because the mutation is reverted, nothing in the diff shows it was done. The tests are plausible detectors: the `accountId` and description inputs of the hash are exercised by the re-import tests, and the cross-format tests assert `MixedFormatOverlapCount > 0` and `IsDuplicate == false`. The result itself is unverified.
- **Fix**: Next time, record the observed failing test names in the Progress line. The same pattern is covered by the Stryker setup that landed later (b2101a8).
- **Decision**: PENDING

### F3 — Progress SHAs do not match the commits on main

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: plan.md Progress (74e3966, b625cbf, ca21cf2)
- **Detail**: The SHAs in Progress are the pre-rebase commits. The same phases are on main as d67a2e9, 42ef628 and 574bec8. Traceability from the plan to the history needs a message search.
- **Fix**: None needed. When SHAs are written back, re-check them after a rebase.
- **Decision**: PENDING

### F4 — lessons.md entry added in p3 (not in plan)

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: context/foundation/lessons.md (574bec8, +7 lines)
- **Detail**: The Arrange/Act/Assert plus shared-setup rule was recorded as a project lesson. The plan's Amendments mention the convention but not the lesson file. It is benign and consistent with the Amendments. The tests follow the rule (IAsyncLifetime, A/A/A in every test).
- **Fix**: None.
- **Decision**: PENDING

### F5 — Later evolution: single-page mBank PDF now parses

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Tests/Support/MBankPairedStatement.cs (touched later by ddf2d04)
- **Detail**: At landing time, `MBankPairedStatement` forced a two-page PDF layout (`FirstPageRows = 3`) because the parser read no rows from a single-page statement. This was documented in the code and in test-plan.md section 6.4. The later change `mbank-single-page-pdf` (ddf2d04) touched this file again. The workaround note should be checked for staleness. No impact on the landed change.
- **Fix**: Check whether the "two pages on purpose" comment and the 6.4 note are still accurate.
- **Decision**: PENDING
