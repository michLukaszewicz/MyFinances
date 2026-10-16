<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Parser Correctness Tests (Risk #2)

- **Plan**: context/archive/2026-10-02-testing-parser-correctness/plan.md
- **Scope**: Full plan (Phases 1-3 of 3)
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warnings, 4 observations

Basis: code and git history only, judged against the landing commits 89f3928 (phase 1), 057dda1 (phase 2) and 06473a0 (phase 3), all dated 2026-10-02. No build, test or mutation run was performed; later evolution of the files at HEAD is not assessed. Accepted lesson in force (`context/foundation/lessons.md`): Arrange/Act/Assert comments and shared per-class setup.

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

### F1 — NBSP in PDF amount cells is pinned as accepted, plan said rejected

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Tests/Import/Parsers/MBankPdfParserTests.cs, VeloBankPdfParserTests.cs, ErstePdfParserTests.cs (tests `Parse_ReadsANoBreakSpaceThousandsSeparator_AsTheSameValueAsARegularSpace`, added in 057dda1)
- **Detail**: Phase 2 change 1 says a cell with U+2212, NBSP or a dot decimal must make the parser reject the statement with `StatementIntegrityException`. Implemented: U+2212 and dot decimal are rejected (theories `..._AnAmountCellWithALookalikeCharacter_...` and `..._ABalanceCellWithALookalikeCharacter_...` in all three banks), but NBSP is pinned as parsed to the correct value (e.g. 3200.00m with date 2026-09-17). The test comments explain why (PdfPig returns NBSP as an ordinary space, so the number pattern never sees it), and the outcome is never a wrong value, which is the plan's real intent. The deviation is recorded only in test comments; the plan, its Progress item 2.2 and `plan.md` were not amended.
- **Fix**: Add a one-line note to the plan or test-plan.md findings that NBSP is accepted-as-correct (not rejected) in PDFs.
- **Decision**: FIXED (2026-10-16, plan Amendments added)

### F2 — Foreign-currency adjacency tests assert counts only

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Tests/Import/Parsers/VeloBankPdfParserTests.cs (`Parse_Accepts_ADroppedRowNextToAForeignCurrencyRow_KnownLimitation`, 057dda1)
- **Detail**: The plan's contract says expected outcomes are literal. The test asserts `Transactions.Count == 15` and `SkippedErrorCount == 1` only; it does not assert which row is missing or the surviving amounts, so a different row dropping would also pass. The other VeloBank limitation tests (newest, oldest, pending) also assert a literal amount. Also, the plan asked for a drop "adjacent to a pending" row; the implemented pending test drops the pending row itself, not its neighbour.
- **Fix**: Add an assertion that the dropped row's amount is absent and a neighbour's literal amount is present.
- **Decision**: PENDING

### F3 — Culture tests use Contains on a few rows, not the whole result

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: MyFinances/backend/Tests/Import/Parsers/CultureIndependenceTests.cs (ErsteCsv, MBankPdf, VeloBankPdf, ErstePdf tests, 06473a0)
- **Detail**: Only the mBank CSV culture test compares the full row list. The other four assert two or three `Assert.Contains` literals per culture (VeloBank PDF: `t.Amount == -89.00m` with no date). They are not trivially passing (literals are authored, thousands and decimal-comma values are covered, the pl-PL separator guard exists), but a culture-dependent misread of an unasserted row would pass. The plan required the "same authored date/amount literals" for a key fixture, which is met.
- **Fix**: Optionally reuse the full `printed` arrays already written in the per-bank PDF tests for the culture runs.
- **Decision**: PENDING

### F4 — Invisible NBSP literals and double-to-decimal cast in oracles

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MBankCsvParserTests.cs (`Parse_ReadsKwotaWithNbspThousands_AsExactValue`), the three PDF NBSP tests, ErsteCsvParserTests.cs (`Parse_ReadsThousandsSeparatedAmount_AsExactValue`, 89f3928)
- **Detail**: The NBSP is a raw U+00A0 byte pair in the source (verified in the blob), visually identical to a regular space, so the regular-space and NBSP mBank tests look identical in a diff or editor. The Erste theory passes the expected amount as `double` and casts it with `(decimal)expectedAmount`; it is correct for these values but makes the oracle depend on a floating-point conversion.
- **Fix**: Write the NBSP as `" "` and pass the expected amount as `decimal` through a string or `[InlineData]` converted with `decimal.Parse(..., CultureInfo.InvariantCulture)`.
- **Decision**: PENDING

### F5 — Mutation spot-checks cannot be confirmed from the diff

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress items 1.3, 2.2, 3.2
- **Detail**: Retrospective review; automated checks were not re-run. Items 1.1-1.2, 2.1, 3.1 and 3.3 are consistent with the diff (tests exist, no product code changed in any of the three commits, no hunks outside the planned files). Items 1.3, 2.2, 3.2 (temporary mutation then revert) leave no trace by design and are accepted on the checkbox plus commit SHA alone. Manual items are backed by the diff: A/A/A comments and a shared `_parser` field in 89f3928; hand-written literal arrays with comments saying they are not derived from builders in 057dda1; test-plan.md Phase 2 row updated in 06473a0 (only the Phase 2 row and the TBD note changed, as planned).
- **Fix**: None required.
- **Decision**: PENDING

## Review notes (no finding)

- Phase 1: dmy test now asserts the full list by value (day/month swap would fail); thousands-space and NBSP `Kwota` outcomes pinned as correct values with `SkippedErrorCount == 0`; `..._KnownLimitation` mid-file bad-date test asserts one row kept and zero skipped, as planned; Erste delimiter theory and thousands-separated theory cover all four variants.
- Phase 2: literal per-row oracles for all three banks; mBank compensating-error pair (consistently altered last row rejected by the closing-balance anchor, two offsetting errors accepted) matches the plan; VeloBank newest/oldest/pending/foreign-neighbour limitations named `_KnownLimitation` with update conditions in comments; endpoint 422 test asserts `GetStoredAsync` is empty.
- Phase 3: `CultureMatrix.Run` restores both `CurrentCulture` and `CurrentUICulture` in `finally`, covers the five planned cultures, culture list is a shared public member (a `IReadOnlyList`, not a const); results are asserted against literals; `CodePagesEncodingProvider` registered in the constructor.
- Scope: the Erste CSV test file got a class-wide `_parser` refactor beyond the plan, driven by the A/A/A and shared-setup lesson; benign. No production code touched.
