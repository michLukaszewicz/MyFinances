<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: PDF Statement Import (mBank and Erste)

- **Plan**: context/archive/2026-10-02-pdf-import/plan.md
- **Scope**: Full plan (Phases 1-7 of 7)
- **Reviewed phases**: 1, 2, 3, 4, 5, 6, 7
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 5 observations

Method: code and git history only (commits 85afcc4, 0d6a918, 333b83c, 37f4e2d, bec4f14, db84e8b, 8112efa). No build, test or npm command was run. Line numbers refer to the files as committed in the phase commit named in each finding; HEAD may differ.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

All six dimensions pass; the observations below are minor notes that do not change a dimension verdict.

## Findings

### F1 — Erste fixtures regenerated in Phase 7, after Phase 6 was marked done

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Tests/Support/ErsteSampleData.cs; Tests/Fixtures/erste-pdf-sample-synthetic.pdf and erste-pdf-sample-synthetic-multipage.pdf (all in commit 8112efa)
- **Detail**: Phase 6 (db84e8b) committed the two Erste fixtures. The Phase 7 commit (8112efa) changed one row of the sample data (a refund description shortened, dropping a masked card number) and regenerated both PDFs (19027 -> 18779 and 14875 -> 14636 bytes). Phase 6 is fully `[x]`, yet its artefacts were still adjusted while the parser was built against the real file. The parser comments at ErstePdfParser.cs:114-116 and :162-163 confirm the parser was tuned to differences between synthetic and real files (synthetic rectangles are also stroked; the real page has unfilled clips, a filled logo and doubly painted header cells). The change is benign and keeps the parser independent of builder artefacts, but the plan did not anticipate fixture changes in Phase 7.
- **Fix**: None needed; for future plans, expect fixture adjustment when the parser meets the real sample.
- **Decision**: PENDING

### F2 — mBank rows anchored by grid bands, not by date lines (plus an unplanned currency check)

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/backend/Import/MBankPdfParser.cs:157-195 and :54, :99 (commit bec4f14)
- **Detail**: The plan (Phase 5, Parse) says rows are anchored by lines whose first column holds an ISO date, with later description lines appended until the next date line. The implementation instead takes the bands between consecutive grid lines of the first column (ReadRows, :160-195) and requires the first cell of each non-header band to be a date (ReadRow, :219-222). The outcome is the same for the documented layout, and it is the stricter reading because any band that is not a date row is an integrity failure, but the mechanism differs from the plan text. The parser also adds a `MissingCurrency` rejection (:54, :99) when no "Waluta" value is found; the plan lists only missing opening balance, closing balance and summary. This follows from the plan's PLN-only rule (the currency must be known), but it is not in the plan's list of integrity failures.
- **Fix**: None needed; optionally note the band-based anchoring if the plan is reused as a pattern.
- **Decision**: PENDING

### F3 — Existing "bank without PDF parser" test rewritten with a stub parser

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/backend/Tests/ImportEndpointsTests.cs (commits bec4f14 and 8112efa; stub `CsvOnlyParser` at :525-534 in 8112efa); Tests/Support/VeloBankPdfBuilder.cs (37f4e2d)
- **Detail**: The Phase 5 commit removed the existing test that expected "mBank import does not support PDF files." and Phase 7 removed its Erste successor, replacing them with `Parse_PdfWithBankThatHasNoPdfParser_ReturnsBadRequestNamingBankAndFormat` against an injected `StubCsvBank`. The plan did not list this edit, but registering the new parsers makes the old assertions false, so the change is forced and the 400 behaviour stays pinned. Phase 4 also made `VeloBankPdfBuilder.PinTrailerId` internal to reuse it, a one-word visibility change not named in the plan.
- **Fix**: None needed.
- **Decision**: PENDING

### F4 — New test files carry no Arrange/Act/Assert comments

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Tests/MBankPdfParserTests.cs, ErstePdfParserTests.cs, MBankPdfFixtureTests.cs, ErstePdfFixtureTests.cs, and the new tests in ImportEndpointsTests.cs
- **Detail**: None of the roughly 130 new tests contain `// Arrange`, `// Act` or `// Assert` comments (count 0 in each file at the phase commits). This was not a violation at the time: the rule in context/foundation/lessons.md ("Write every test with Arrange/Act/Assert sections...") was added in commit 574bec8 at 11:59 on 2026-10-02, after the last implementation commit 8112efa (09:18). The new tests match their siblings (VeloBankPdfParserTests also has none). Noted because later edits to these files will have to follow the rule.
- **Fix**: Add the section comments when these files are next edited, per the lesson.
- **Decision**: PENDING

### F5 — mBank summary check treats a zero amount as neither credit nor debit

- **Severity**: OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/MBankPdfParser.cs:353-358 (commit bec4f14)
- **Detail**: Credits are rows with `Amount > 0`, debits rows with `Amount < 0`, while the total uses all rows. A statement containing a 0,00 operation would have credits + debits one short of the total, so the comparison against the printed summary depends on how mBank counts such a row. Neither sample contains a zero-amount row and the plan does not define the case, so whether this can reject a valid statement is unverified. The effect would be a false rejection (422 with an explanation), never a silent import, so the failure mode is safe.
- **Fix**: Leave as is unless a zero-amount mBank row is ever observed; then decide which side mBank counts it on.
  - Strength: Failure mode is a visible rejection, not corrupted data.
  - Tradeoff: A hypothetical valid statement is refused until the rule is known.
  - Confidence: LOW — no sample with a zero-amount row exists.
  - Blind spot: How mBank counts a 0,00 operation in the printed summary.
- **Decision**: PENDING

## Review notes by dimension

- **Plan Adherence**: Phase 1 matches (PRD FR-018 and the non-goal line updated, roadmap merged into S-12 `pdf-import`, S-13 block and rows removed). Phase 2 extracts `Import/Pdf/PdfStatementReader.cs` with the single catch-all, byte-copy open, position restore, `JoinCell`, `NumberPattern`/`ParseNumber` and `Rejection(bankName, ...)`; VeloBank keeps its message text through `PdfStatementReader.Rejection("VeloBank", ...)` and no VeloBank test file is in the diff stat. Phase 3 matches the contract: `ImportBatch.SourceFormat` (text column, default Csv, VeloBank backfill to Pdf in the migration), `Format` and `MixedFormatOverlapCount` on the parse response, `SourceFormat` echoed on commit with default Csv, inclusive `[min, max]` range, ignoring batch-less rows, other accounts and other users. Phases 4-7 deliver the generators, committed fixtures (csproj entries in 37f4e2d and db84e8b), parsers, DI registrations (one line each), the `home.tsx` copy and the integrity checks named in the plan (mBank: chain, closing balance, summary; Erste: order-independent multiset linkage, unreadable rows, header-less later page, foreign-currency row skipped and disabling the check).
- **Scope Discipline**: every item in "What We're NOT Doing" is respected in the diff: no change to `DedupHash`, no description normalization, the warning is non-blocking, no account-number checks, no Revolut work.
- **Safety & Quality**: rejection messages use fixed problem strings plus page and date (PdfStatementReader.cs:137-144), so no amount, description or name reaches the user; digit runs are bounded in `NumberPattern` (:24); the overlap query is scoped by `UserId` and `AccountId` (ImportEndpoints.cs, 333b83c); the commit endpoint accepts a client-supplied `SourceFormat` only to label an audit column and a warning, as the plan accepted. The migration backfill is additive and has a `Down`.
- **Architecture**: parsers depend on the shared reader, not on each other; bank-specific recognition, geometry and integrity checks stay in each parser as planned; each PDF parser has tests rejecting the other banks' fixtures.
- **Pattern Consistency**: both new parsers mirror `VeloBankPdfParser` (primary-constructor `maxPages`, `DefaultMaxPages`, `TryRead` wrapper, band-based reading, named check constants). See F4 for the one gap.
- **Success Criteria (retrospective, no automated checks re-run)**: Progress is fully `[x]`. Automated items of phases 1-7 carry a commit SHA whose diff contains the corresponding work (tests exist for every contract bullet checked in this review: overlap boundaries and defaults in 333b83c; recognition, integrity and endpoint cases in bec4f14 and 8112efa, including the asserted blind spot `Parse_Accepts_ADeletedNewestRow` / `Parse_Accepts_ADeletedOldestRow`). Manual items 4.4, 4.5, 5.5-5.8, 6.4, 6.5 and 7.6-7.10 are checked without a SHA; they concern the real sample files, a PDF viewer and the running app, which cannot appear in a diff, so they are neither confirmed nor contradicted here. The parser comments on real-file quirks (ErstePdfParser.cs:114-116, :162-163) are indirect evidence that the real `erste.pdf` was run against the parser. Not verified in this review: that no real personal data sits in the committed PDF fixtures (binary files were not inspected).
