<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Erste Bank Polska CSV Import

- **Plan**: context/changes/erste-import/plan.md
- **Mode**: Deep (direct verification)
- **Date**: 2026-10-01
- **Verdict**: REVISE → SOUND after fixes
- **Findings**: 0 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding
7/7 paths ✓, 5/5 symbols ✓, brief↔plan ✓, Progress↔phases ✓

## Findings

### F1 — Phase 2 breaks an existing test the plan doesn't mention

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Plan Completeness
- **Location**: Phase 2 — DI registration / criterion 2.1
- **Detail**: `AccountEndpointsTests.cs:92` asserts `/accounts/banks` == `["mBank", "Other"]`; registering the Erste parser changes the list.
- **Fix**: Add the test update to Phase 2.
- **Decision**: FIXED (new Phase 2 change #4)

### F2 — Behaviour of Parse on an unrecognised file is undefined

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Blind Spots
- **Location**: Phase 1 parser contract; Phase 2 manual-fallback test
- **Detail**: `/import/parse` calls `Parse` on a manually chosen bank even when `CanParse` rejected the file.
- **Fix**: Parse returns an empty result (0 rows, 0 skipped) when no delimiter matches; fallback test asserts 200 with 0 rows.
- **Decision**: FIXED

### F3 — Roadmap update misses two stale places

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Plan Completeness
- **Location**: Phase 2 — Roadmap sync
- **Detail**: `roadmap.md:63` (stream C gating) and `:232` ("Depends on S-07") also need updating.
- **Fix**: List lines 51, 63, 188, 232.
- **Decision**: FIXED

### F4 — Decoding strictness of CanParse is unspecified

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Blind Spots
- **Location**: Phase 1 — CanParse
- **Detail**: All parsers' `CanParse` run on every upload, including cp1250 mBank files; a strict UTF-8 decode would throw.
- **Fix**: Lenient decode, never throw; test against the mBank fixtures.
- **Decision**: FIXED

### F5 — Roadmap status changed in a commit without GitHub issue sync

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Blind Spots
- **Location**: Commit c8d96c5; lessons.md
- **Detail**: Lesson requires issue sync after roadmap status changes.
- **Fix**: Sync issue #9 now.
- **Decision**: FIXED (issue #9: label status: proposed → status: planning, comment added)
