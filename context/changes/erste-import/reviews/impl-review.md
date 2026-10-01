<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Erste Bank Polska CSV Import

- **Plan**: context/changes/erste-import/plan.md
- **Scope**: Full plan (Phases 1–2 of 2)
- **Reviewed phases**: 1, 2
- **Date**: 2026-10-01
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success criteria re-run during review: `dotnet build` 0 errors; `dotnet test --filter ErsteCsvParserTests` 14/14; `dotnet test` 142/142; `npm run typecheck` exit 0; `npm run build` exit 0. All manual Progress rows are checked (1.4, 2.4–2.7, confirmed by the user).

## Findings

### F1 — CanParse and Parse disagree on a whitespace-only first line

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/ErsteCsvParser.cs:50-52 vs :113
- **Detail**: `DetectDelimiter` skips whitespace-only leading lines, but `Parse` relies on CsvHelper skipping only truly empty lines. A file starting with a whitespace-only line (e.g. spaces, then the summary) passes `CanParse`; `Parse` then treats the whitespace record as the summary, `isPln` is false and every real row is counted as skipped (reproduced: 0 transactions, 1 skipped). A `"\r\n"`-only prefix is fine. Unlikely for real Erste exports.
- **Fix**: In `Parse`, advance past leading blank records (reuse `IsBlankRecord`) before reading the summary.
- **Decision**: FIXED (Fix now: Parse skips leading blank records; regression test Parse_SkipsWhitespaceOnlyLeadingLine_BeforeTheSummary)

### F2 — Unbalanced quote swallows the rest of the file into one record

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/ErsteCsvParser.cs:117-140
- **Detail**: A stray `"` inside a description makes CsvHelper read the remaining lines as one field: one skipped row, all later rows lost, with a skip count of 1. `MBankCsvParser` behaves the same way, so this is consistent with the existing parser.
- **Fix**: None required; accept as consistent with mBank.
- **Decision**: SKIPPED (consistent with MBankCsvParser)

### F3 — Manual-bank fallback on non-Erste content shows an empty preview without explanation

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/ErsteCsvParser.cs:101-106; Import/ImportEndpoints.cs:43-48
- **Detail**: Choosing "Erste" manually for a file that is not an Erste export returns 200 with 0 rows and 0 skipped, so the UI shows an empty review without saying why. Deliberate and tested, mirrors mBank.
- **Fix**: None required; optional UX copy later.
- **Decision**: SKIPPED (deliberate, tested, mirrors mBank)
