<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Mutation Testing with Stryker.NET

- **Plan**: context/archive/2026-10-02-mutation-testing-stryker/plan.md
- **Scope**: Full plan (Phases 1-4 of 4)
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 3 observations

Method: git history and code reading only (commits b2101a8 p1, 10de77a p2, a5ad3f6 CI restore, a6b8b7c test-plan sync, a642448 archive). No build, test, dotnet or npm commands were run. Success Criteria: automated checks were not re-run (retrospective review). The nine Progress items ticked after the user's manual validation are treated as user-confirmed; supporting evidence in the diff is noted per finding.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Findings

### F1 — Phase 4 has no landed change (DedupHashTests never modified after the Stryker run)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Tests/Import/DedupHashTests.cs
- **Detail**: Phase 4 plans new or strengthened assertions in this file to kill the 4 surviving mutants in Import/DedupHash.cs. The last commit touching the file is cc4b519 (2026-10-02, namespace refactor), which predates p1 (b2101a8, 13:58 on 2026-10-02). No commit in the change references Phase 3 or 4 (only p1 and p2 exist), and the file has 6 tests (identical inputs, plus one per field differing) that are unchanged by this change. Items 4.1-4.3 are ticked without commit SHAs (Phases 2-4 carry none), so the "0 survivors" claim has no supporting evidence in git. DedupHash.cs formats values with "D", "yyyy-MM-dd", "F2" and a '|' separator (lines 12-18), none of which the current tests pin to an exact value.
- **Fix**: Either commit the Phase 4 test changes (e.g. exact-hash and separator/format assertions) with a SHA recorded in Progress, or amend the plan notes to say Phase 4 was deferred or done outside git.
  - Strength: Restores traceability between the ticked 4.1-4.3 and the repository.
  - Tradeoff: If the work exists only in an uncommitted or lost branch, it must be redone.
  - Confidence: MED — only git history was inspected, not the user's local working tree.
  - Blind spot: Work may have been squashed or committed under another change's message.
- **Decision**: ACCEPTED (2026-10-16, user confirms phase 4 was run locally without a commit)

### F2 — Phase 3 documentation note landed outside the change and with a stale path

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/foundation/test-plan.md:88
- **Detail**: Phase 3 plans a short test-plan.md entry. It exists (line 88: Stryker.NET 5.0.0, incremental, report-only) but arrived in a6b8b7c (2026-10-08, "sync stack, gates and Phase 4"), not in a change commit. It still points to `context/changes/mutation-testing-stryker/`, which no longer exists after the archive (a642448). Phase 3 steps 3.1-3.3 are runtime checks with no repository evidence; they rest on the user's manual validation.
- **Fix**: Update the link to context/archive/2026-10-02-mutation-testing-stryker/.
- **Decision**: FIXED (2026-10-16, path in context/foundation/test-plan.md updated to the archive folder)

### F3 — Tool manifest location differs from the plan

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/dotnet-tools.json
- **Detail**: Plan says `MyFinances/backend/.config/dotnet-tools.json`; p1 committed `MyFinances/backend/dotnet-tools.json`. dotnet tool restore accepts both locations and CLAUDE.md and ci.yml (`working-directory: MyFinances/backend`) are consistent with the actual location. Version is pinned (5.0.0, rollForward false) as planned.
- **Fix**: None needed; optionally note it in the plan.
- **Decision**: PENDING

### F4 — Mutation score summary depends on a Stryker log string

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/ci.yml (mutation job, "Summarize mutation score" step)
- **Detail**: The score is taken by `grep "final mutation score"` on the tee'd log and falls back to "no mutants tested", so a change in Stryker's output wording silently produces a misleading summary. The job stays non-blocking as planned (break 0 in Tests/stryker-config.json). The workflow matches the plan (pull_request to main, fetch-depth 0, `--since:origin/${{ github.base_ref }}`, artifact upload with if: always()). Later evolution: a974fcf added typecheck and e2e jobs and the ruleset requires only test, typecheck and e2e.
- **Fix**: Optionally parse the JSON report for the score instead of the log.
- **Decision**: PENDING

### F5 — Config and plan items confirmed as matching (no action)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/backend/Tests/stryker-config.json:1-9
- **Detail**: Config matches the contract exactly (project, test-projects, perTest coverage, html/progress/json reporters, thresholds 80/60/0, no `since` stored). .gitignore gains `StrykerOutput/` and CLAUDE.md gains the local command with the `main` vs `Master` note. No hooks, baseline, frontend steps or Import-only `mutate` were added, so the NOT-doing list is respected. Supporting evidence for ticks 1.1-1.5 and 2.1: the p1 and p2 commits contain the files and config; runtime ticks (1.4, 2.3-2.5, 3.x) cannot be proven from the diff.
- **Fix**: None.
- **Decision**: PENDING
