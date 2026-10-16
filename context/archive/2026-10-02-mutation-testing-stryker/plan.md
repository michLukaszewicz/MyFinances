# Mutation Testing with Stryker.NET Implementation Plan

## Overview

Add Stryker.NET as a local dotnet tool for the backend test suite and run it incrementally (only code changed relative to `main`) in the first GitHub Actions workflow of the repo, on pull requests. The gate is report-only for now (`thresholds.break = 0`).

## Current State Analysis

- No mutation tooling exists. The only mutation checks so far are the manual "mutation spot-checks" recorded in the archived `testing-parser-correctness` plan.
- Backend tests: xUnit, 577 tests, ~43 s end to end (`dotnet test` from `MyFinances/backend`), EF Core InMemory so no database is needed in CI.
- `.github/workflows/` exists but is empty; there is no CI of any kind.
- The only git remote is named `Master` (not `origin`), so `origin/main` exists in CI (`actions/checkout`) but locally the diff target is the local branch `main`.
- Solution file is `MyFinances/backend/MyFinances.Api.slnx`; test project is `MyFinances/backend/Tests/MyFinances.Api.Tests.csproj`, referencing `../MyFinances.Api.csproj`.
- .NET SDK is 10.0.301; no `.config/dotnet-tools.json` yet.

### Key Discoveries:

- Stryker `--since:<committish>` mutates only code changed per `git diff`; a change in a test file re-tests all mutants covered by it. `--with-baseline` needs report storage and is mutually exclusive with `--since`, so it is out of scope (Stryker.NET configuration docs, via Context7).
- `thresholds.break` defaults to 0 and only fails the run when the score drops below it.
- `.slnx` solution-mode support in Stryker is unverified; running Stryker from the test project directory (project mode) avoids depending on it.

## Desired End State

- `dotnet tool restore` then `dotnet stryker --since:main` (from `MyFinances/backend/Tests`) runs a mutation pass limited to changed files and writes an HTML + JSON report under `StrykerOutput/` (git-ignored).
- A `ci.yml` workflow on every pull request to `main` runs build + tests, and a separate mutation job that runs the same incremental command against `origin/main`, uploads the report as an artifact and shows the score in the job summary. The mutation job never fails the build on score (`break: 0`).
- `CLAUDE.md` documents the local command.

## What We're NOT Doing

- No git hooks and no Claude Code hooks (mutation runs are too slow for the edit loop).
- No `--with-baseline` / Stryker Dashboard / report storage.
- No enforced mutation-score gate (`break` stays 0); picking a real threshold is a follow-up once scores are known.
- No frontend CI steps (no frontend test framework exists).
- No branch-protection configuration (repo settings, done by the user in GitHub).
- No scoping of `mutate` to `Import/**`: the whole `MyFinances.Api` project is mutated, with incremental mode keeping runs small.

## Implementation Approach

Prove the tool locally first (phase 1), then wrap the identical command in CI (phase 2), then verify both on a throwaway change (phase 3). Config lives next to the test project so Stryker runs in project mode, which avoids `.slnx` uncertainty.

## Phase 1: Local tool and configuration

### Overview

Install `dotnet-stryker` as a repo-local tool and configure it for incremental runs.

### Changes Required:

#### 1. Tool manifest

**File**: `MyFinances/backend/.config/dotnet-tools.json`

**Intent**: Pin `dotnet-stryker` so local and CI use the same version.

**Contract**: Created via `dotnet new tool-manifest` + `dotnet tool install dotnet-stryker` from `MyFinances/backend`; manifest is committed.

#### 2. Stryker config

**File**: `MyFinances/backend/Tests/stryker-config.json`

**Intent**: Mutate the API project, test it with the sibling test project, keep runs fast, produce reports, never fail on score yet.

**Contract**: `stryker-config` with `project: MyFinances.Api.csproj`, `test-projects: ["MyFinances.Api.Tests.csproj"]`, `coverage-analysis: perTest`, `reporters: ["html", "progress", "json"]`, `thresholds: { high: 80, low: 60, break: 0 }`. `--since` is passed on the command line (target differs between local and CI), not stored in the file.

#### 3. Ignore output and document

**Files**: `.gitignore`, `CLAUDE.md`

**Intent**: Keep `StrykerOutput/` out of git; add the local command to the Commands section of `CLAUDE.md`.

**Contract**: `.gitignore` gains `StrykerOutput/`. `CLAUDE.md` Commands lists `dotnet tool restore` and `dotnet stryker --since:main` (run from `MyFinances/backend/Tests`), noting the local branch is `main` because the remote is called `Master`.

### Success Criteria:

#### Automated Verification:

- Tool restores: `dotnet tool restore` (from `MyFinances/backend`)
- Stryker version prints: `dotnet stryker --version` (from `MyFinances/backend/Tests`)
- Backend tests still pass: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- A mutation run with `--since:main` on a branch with one edited parser file mutates only that file and writes `StrykerOutput/**/reports/mutation-report.html`
- `git status` shows no `StrykerOutput/` files

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding to the next phase.

---

## Phase 2: GitHub Actions workflow

### Overview

Create the repo's first CI workflow: build/test plus an incremental mutation job.

### Changes Required:

#### 1. CI workflow

**File**: `.github/workflows/ci.yml`

**Intent**: On pull requests to `main`, run the backend build and tests, and run Stryker incrementally against the PR's base.

**Contract**: Trigger `pull_request` with `branches: [main]`. Two independent jobs on `ubuntu-latest`, both using `actions/setup-dotnet` with the SDK from `global.json`/`10.0.x`:
- `test`: `dotnet build` and `dotnet test` from `MyFinances/backend`.
- `mutation`: `actions/checkout` with `fetch-depth: 0`; `dotnet tool restore`; `dotnet stryker --since:origin/${{ github.base_ref }}` from `MyFinances/backend/Tests`; upload `StrykerOutput/**` as an artifact (`if: always()`); write the mutation score into `$GITHUB_STEP_SUMMARY`.
Mutation job does not depend on `test` passing being required to start, but its failure must not block when the score is below any threshold (`break: 0`).

### Success Criteria:

#### Automated Verification:

- Workflow YAML parses: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` (from repo root)
- Backend tests still pass locally: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Opening a PR runs both jobs; `test` is green
- `mutation` job finishes, uploads the report artifact and shows a score in the job summary
- The mutation job only reports on files changed in the PR

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before proceeding to the next phase.

---

## Phase 3: Verify and record

### Overview

Prove incremental mode works end to end and record how to use it.

### Changes Required:

#### 1. Test plan note

**File**: `context/foundation/test-plan.md`

**Intent**: Record that mutation testing now exists, how to run it, and that the gate is report-only, so the manual "mutation spot-check" convention has an automated counterpart.

**Contract**: A short entry in the cookbook (§6) or the section that lists test tooling; no change to the risk map.

### Success Criteria:

#### Automated Verification:

- Incremental run on an unchanged tree reports no mutants: `dotnet stryker --since:main` (from `MyFinances/backend/Tests`, on a branch equal to `main`)

#### Manual Verification:

- Editing one line in a parser on a branch makes the run mutate only that file
- Run duration for a one-file change is noticeably shorter than a full run

---

## Phase 4: Strengthen existing tests

### Overview

Use the mutation reports to fix weak existing tests: kill the mutants that survive in changed code. Starting point: all 4 mutants in `Import/DedupHash.cs` survived on the first run, so `Tests/Import/DedupHashTests.cs` asserts too little.

### Changes Required:

#### 1. Tests for surviving mutants

**Files**: `MyFinances/backend/Tests/Import/DedupHashTests.cs` (and others the reports point to)

**Intent**: For each surviving mutant in the report, add or strengthen an assertion that fails when the mutation is applied, following the Arrange/Act/Assert convention from `context/foundation/lessons.md`.

**Contract**: No production code changes. Scope is bounded by the reports produced in phases 1-3; further survivors become follow-up changes.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- Mutants in `Import/DedupHash.cs` are killed: `dotnet stryker -m "Import/DedupHash.cs"` (from `MyFinances/backend/Tests`) reports 0 survivors

#### Manual Verification:

- Skim the new assertions: each fails for a reason a reader can name

---

## Testing Strategy

### Unit Tests:

- None added; the change is tooling. The existing suite is what Stryker exercises.

### Integration Tests:

- None.

### Manual Testing Steps:

1. Branch from `main`, edit a parser file, run `dotnet stryker --since:main` and check the report lists only that file.
2. Open a PR and watch both CI jobs.

## Performance Considerations

Full mutation of the whole API project would be slow; `--since` plus `coverage-analysis: perTest` limit each run to changed code and the tests that cover it. If the whole-project first run is needed to learn the score, run it once manually.

## Migration Notes

None. First CI in the repo: a PR check, not a required status check until the user enables it in repo settings.

## References

- Archived manual spot-checks: `context/archive/2026-10-02-testing-parser-correctness/plan.md`
- Test strategy: `context/foundation/test-plan.md`
- Stryker.NET docs (Context7 `/stryker-mutator/stryker-net`): configuration, `since`, `thresholds`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Local tool and configuration

#### Automated

- [x] 1.1 Tool restores: `dotnet tool restore` (from `MyFinances/backend`) — 8ee975c
- [x] 1.2 Stryker version prints: `dotnet stryker --version` (from `MyFinances/backend/Tests`) — 8ee975c
- [x] 1.3 Backend tests still pass: `dotnet test` (from `MyFinances/backend`) — 8ee975c

#### Manual

- [x] 1.4 A mutation run with `--since:main` on a branch with one edited parser file mutates only that file and writes the HTML report — 8ee975c
- [x] 1.5 `git status` shows no `StrykerOutput/` files — 8ee975c

### Phase 2: GitHub Actions workflow

#### Automated

- [x] 2.1 Workflow YAML parses: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` (from repo root)
- [x] 2.2 Backend tests still pass locally: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 2.3 Opening a PR runs both jobs; `test` is green
- [ ] 2.4 `mutation` job finishes, uploads the report artifact and shows a score in the job summary
- [ ] 2.5 The mutation job only reports on files changed in the PR

### Phase 3: Verify and record

#### Automated

- [ ] 3.1 Incremental run on an unchanged tree reports no mutants: `dotnet stryker --since:main` (from `MyFinances/backend/Tests`, on a branch equal to `main`)

#### Manual

- [ ] 3.2 Editing one line in a parser on a branch makes the run mutate only that file
- [ ] 3.3 Run duration for a one-file change is noticeably shorter than a full run

### Phase 4: Strengthen existing tests

#### Automated

- [ ] 4.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [ ] 4.2 Mutants in `Import/DedupHash.cs` are killed: `dotnet stryker -m "Import/DedupHash.cs"` (from `MyFinances/backend/Tests`) reports 0 survivors

#### Manual

- [ ] 4.3 Skim the new assertions: each fails for a reason a reader can name
