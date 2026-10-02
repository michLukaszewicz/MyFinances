# Import Integrity and Dedup Test Rollout (Phase 1) — Plan Brief

> Full plan: `context/changes/testing-import-integrity-dedup/plan.md`
> Research: `context/changes/testing-import-integrity-dedup/research.md`

## What & Why

Add integration tests that prove re-imports are flagged and leave stored data unchanged when skipped (Risk #1), and that a PDF imported after a CSV of the same period raises the overlap warning (Risk #4). These are the two highest-impact double-counting paths in the import pipeline, and today they are covered only for the all-duplicates case and with dummy seeded rows.

## Starting Point

Dedup is advisory: parse flags by hash, commit trusts the client's Keep/Skip. Cross-format identity is not in the hash, so only a date-range warning exists. 69 existing import/hash tests pass; none use a CSV and PDF describing the same transactions, none test partial overlap.

## Desired End State

Two new test classes in `MyFinances/backend/Tests` fail when dedup flagging, skip behavior or the cross-format warning regress. `test-plan.md` §6.2 documents the pattern for the next test author.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| CSV-vs-PDF contract | Pin warn-only | Matches current product and UI text; no product code in this change | Plan |
| Repeated identical rows (more in file than stored) | Characterization test of current behavior | Makes the limitation visible without a failing test or a fix | Plan |
| Edit-then-reimport | Out of scope | User decision; stays recorded in research | Plan |
| Test layer | Integration, EF InMemory | No DB constraint or transaction is involved | Research |
| "Totals unchanged" assertion | Stored rows and per-account amount sum | Charts only count categorized rows | Research |
| Paired fixture | PDF via `MBankPdfBuilder` + test-authored cp1250 CSV from one row list | Avoids fixtures derived from parser output | Research |
| Existing test file | Untouched; new helper class | 1388 lines, private helpers | Plan |

## Scope

**In scope:** shared test helpers; Risk #1 tests; paired CSV/PDF fixture and Risk #4 tests; test-plan §6.2 cookbook.

**Out of scope:** product code changes, edit-then-reimport, Erste/VeloBank cross-format pairs, frontend, concurrency, Postgres-backed tests.

## Architecture / Approach

Drive endpoints as the client does: parse, derive decisions from `IsDuplicate`, commit, assert persisted state. Expected values come from authored row lists, never from a first run of the code under test.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Test support | `ImportTestHelpers` | Helper shapes drift from real request format |
| 2. Risk #1 tests | `ImportDedupIntegrityTests` | Characterization test may be read as endorsement |
| 3. Risk #4 tests + cookbook | Paired fixture, `ImportCrossFormatOverlapTests`, §6.2 | Hand-authored CSV/PDF failing to parse as intended |

**Prerequisites:** none; baseline suite is green.
**Estimated effort:** ~2 sessions across 3 phases.

## Open Risks & Assumptions

- Warn-only means the Risk #4 tests cannot prove "totals do not inflate"; they pin the warning and the stored rows.
- Erste CSV/PDF description relationship and cross-delimiter hash equality remain unverified.
- No CI exists; the tests protect only when run locally.

## Success Criteria (Summary)

- Full `dotnet test` passes with both new classes.
- Mutating the hash or the overlap query makes the new tests fail.
- Test-plan §6.2 has a usable pattern.
