# Mutation Testing with Stryker.NET — Plan Brief

> Full plan: `context/changes/mutation-testing-stryker/plan.md`

## What & Why

Add Stryker.NET mutation testing for the backend and run it incrementally on pull requests, so weak tests (mutants that survive) are caught without re-mutating the whole codebase each time. Until now the only mutation checks were manual spot-checks recorded in the parser-correctness plan.

## Starting Point

577 xUnit tests (~43 s, EF InMemory) and no CI at all: `.github/workflows/` is empty. The only git remote is called `Master`, so `origin/main` exists only inside GitHub Actions.

## Desired End State

`dotnet stryker --since:main` locally and the same command in a PR workflow mutate only changed code, produce an HTML/JSON report, and never fail the build on score. A first `ci.yml` also builds and tests the backend on every PR.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Trigger | GitHub Actions on `pull_request` | Mutation runs are too slow for hooks | Plan |
| Incremental mode | `--since:<base>`, no baseline | Baseline needs report storage, overkill for one developer | Plan |
| Test speed | `coverage-analysis: perTest` | Each mutant runs only the tests covering it | Plan |
| Scope | Whole `MyFinances.Api` project | Incremental mode keeps runs small, one config for future slices | Plan |
| Gate | Report only (`break: 0`) | Real threshold is unknown until scores are measured | Plan |
| Run location | From `Tests/` in project mode | Avoids relying on `.slnx` support in Stryker | Plan |
| Tool install | Repo-local `dotnet-tools.json` | Same version locally and in CI | Plan |

## Scope

**In scope:** tool manifest, `stryker-config.json`, `.gitignore` entry, `CLAUDE.md` command, first `ci.yml` (build/test + mutation jobs), a test-plan note.

**Out of scope:** hooks of any kind, baseline/Dashboard, enforced score threshold, frontend CI, branch-protection settings.

## Architecture / Approach

Prove the command locally, wrap the identical command in a PR workflow with `fetch-depth: 0`, verify on a throwaway one-file change. Mutation job runs independently of the test job and uploads `StrykerOutput` as an artifact plus a score line in the job summary.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Local tool and configuration | Pinned tool, config, docs | Stryker project-mode quirks with the test project layout |
| 2. GitHub Actions workflow | `ci.yml` with test + mutation jobs | `--since` needs full history and the right base ref |
| 3. Verify and record | Proof of incremental behaviour, test-plan note | None significant |

**Prerequisites:** none beyond the repo; PR check becomes required only if enabled in GitHub settings.
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- First CI run cannot be verified until the branch is pushed and a PR is opened.
- Whole-project scope means a full manual run (to learn the score) may take long.
- Stryker `.slnx` support is unverified; plan avoids it.

## Success Criteria (Summary)

- One-file change on a branch mutates only that file locally.
- A PR shows green `test` and a finished `mutation` job with a report artifact.
