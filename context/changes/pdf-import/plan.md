# PDF Statement Import (mBank and Erste) Implementation Plan

## Overview

Add PDF as a second statement format for mBank and Erste Bank Polska, on top of the format-aware import pipeline and VeloBank PDF parser that `pdf-statement-import` already shipped. The change extracts a thin shared PDF helper from `VeloBankPdfParser`, records each import batch's source format so importing the same period once as CSV and once as PDF is flagged instead of silently duplicated, and adds `MBankPdfParser` and `ErstePdfParser` with synthetic PDF fixtures. mBank lands before Erste so Erste (the lossiest, weakest-anchored of the two) can be cut without losing mBank.

## Current State Analysis

- Parsers are per bank and format: `IBankStatementParser` has `BankName`, `Format`, `CanParse`, `Parse`; the endpoint sniffs the upload's format and only runs parsers of that format; `StatementIntegrityException` maps to 422 — `MyFinances/backend/Import/IBankStatementParser.cs:11-26`, `Import/ImportEndpoints.cs:39-82`. mBank and Erste only have CSV parsers; `GET /accounts/banks` already de-duplicates bank names (`Transactions/AccountEndpoints.cs:17-26`), so a second parser per bank changes no list.
- `VeloBankPdfParser` (471 lines) holds everything a PDF parser needs and mixes it with VeloBank specifics: byte-copy open behind a catch-all that restores stream position (`:113-167`), word/rectangle extraction (`:169-189`), band and column assembly (`:254-340`), `pl-PL` money parsing (`:381-397`), PII-free rejections (`:447-456`). Its tests (`Tests/VeloBankPdfParserTests.cs`, `Tests/VeloBankPdfFixtureTests.cs`) pin its behaviour; the generator is `Tests/Support/VeloBankPdfBuilder.cs` with row data in `Tests/Support/VeloBankSampleData.cs`.
- `ImportBatch` has no source-format column — `Transactions/ImportBatch.cs:4-20`; the commit request carries only `AccountId`, `SkippedErrorCount` and rows — `Import/ImportContracts.cs:27-29`; the parse response carries `Bank`, `BankMismatch`, rows, `SkippedErrorCount` — `Import/ImportContracts.cs:16`. Manual entries have a null `ImportBatchId` (`Transactions/Transaction.cs`). `DedupHash` hashes userId, date, amount, verbatim description and accountId (`Import/DedupHash.cs:11-24`), so CSV and PDF rows of one transaction do not collide.
- Frontend already accepts `.csv,.pdf`, lists the three banks, shows a non-blocking amber `bankMismatch` banner and sends `SkippedErrorCount` on commit — `MyFinances/frontend/app/routes/import.tsx:36-43,55,177-183,252,424`.
- Latest migration is `20261002054938_AddAccountBank` (backfill by raw SQL against `Accounts.Bank`) — `MyFinances/backend/Migrations/`.
- PRD FR-018 and the non-goal line cover VeloBank PDF only — `context/foundation/prd.md:66,122`; the roadmap carries S-12 (`mbank-pdf-import`, GitHub #29) and S-13 (`erste-pdf-import`, #30), both `proposed` — `context/foundation/roadmap.md:55-56,236-261`.

### The two real samples (outside the repo, contain personal data; analysed in `research.md` §2b/2c and re-checked on these files)

- **mBank "Elektroniczne zestawienie operacji"** (Ibex PDF Creator, 2 pages, 32 rows, 2026-09-10..2026-10-01): rows are anchored by a line whose first token is an ISO date (`yyyy-MM-dd`, booking date then operation date, equal on all 32 rows); the description starts with the operation type line (`ZAKUP PRZY UŻYCIU KARTY`, `BLIK P2P-WYCHODZĄCY`, `POS ZWROT TOWARU`, …) and continues with merchant/counterparty lines; amount and balance carry no currency (the account currency is the "Waluta" header field). Three independent checks exist in the document: opening balance (`Saldo początkowe`), closing balance (`Saldo końcowe`, printed beside the last row) and the turnover summary (credits 7 / 4 274,36, debits 25 / 2 484,84, total 32 / 1 789,52). Rows are chronological, oldest first.
- **Erste "Lista transakcji"** (Chromium/Skia, 3 pages, 55 rows): 30 pt row bands between thin filled separators, four columns (date block | operation | amount | balance); the date cell holds the operation date as `28 wrz 2026` and, below it, the label "Data księgowania" with no value; the operation cell is only the counterparty/merchant name (refunds add a second line); amount cells print `-0,10 PLN`. Rows are newest first by the unprinted booking date, so same-day order does not follow the balance. The extracted text never names the bank ("Lista transakcji" and a "Konto:" line are the only anchors; the document date is printed as "Dokument z dnia: 01 paź 2026").
- Neither sample contains a foreign-currency row; mBank prints currency once per statement, Erste per amount cell.

## Desired End State

A user uploads an mBank or Erste Bank Polska PDF on the import page; the bank is auto-detected next to the CSV and VeloBank formats, rows are listed and flow through duplicate review, categorization, charts and transfer detection like any import, and a PDF whose own balance figures do not add up is rejected with an explanation. Importing a file whose date range overlaps transactions that came from the other format on the same account shows a non-blocking warning with the number of overlapping rows. CSV and VeloBank imports behave as before. PRD and roadmap record the new coverage. Verify with `dotnet test`, `npm run typecheck`, `npm run build` and manual imports of the real `mbank.pdf` and `erste.pdf`.

### Key Discoveries:

- The transaction already points at its `ImportBatch`, so the source format belongs on the batch (one column), and manual entries need no handling because their batch is null — `Transactions/Transaction.cs`, `Transactions/ImportBatch.cs`.
- The commit endpoint never sees the parser, so the format must travel from the parse response back in the commit request, like `SkippedErrorCount` already does — `Import/ImportEndpoints.cs:118-185`, `import.tsx:177-183`. It only labels an audit column and a warning, so the server accepting the echoed value is acceptable.
- The only mBank CSV/PDF description difference found is padding, but a CSV/PDF pair for one period does not exist, so hash-compatibility cannot be verified; the plan therefore warns rather than normalizes (`research.md` §2b).
- Erste's balance linkage is order-independent only (12 of 54 adjacent pairs break), so the chain check cannot be row-by-row — `research.md` §2c.
- Pending-style rows do not occur in either sample; both banks list booked rows only.

## What We're NOT Doing

- Changes to `DedupHash`, description normalization, whitespace collapsing, rehash migrations, or any attempt to rebuild mBank's padded CSV descriptions from the PDF (unverifiable without a CSV/PDF pair for one period).
- Blocking a mixed-format import — the warning is non-blocking, like `BankMismatch`.
- Revolut (S-07 stays `proposed`), Excel or other formats, LLM extraction, OCR, PDF-to-CSV conversion.
- Foreign-currency conversion — non-PLN rows are skipped and counted; an mBank statement whose "Waluta" is not PLN is skipped entirely (PRD PLN-only).
- Checking the statement's account number or holder against the selected account; using the statement period for anything but the mBank integrity checks.
- Special messages for password-protected or corrupt PDFs — they count as unreadable, as for VeloBank.
- Making Erste PDF "equivalent" to Erste CSV: it stays lossy (operation date, counterparty-only description); the import page does not steer users between formats beyond the overlap warning.
- Backfilling the mixed-format warning for manual entries (they have no batch and are ignored).
- The CLAUDE.md project-status line — updated at archive time as for previous slices.

## Implementation Approach

Decisions taken during planning (research and the VeloBank plan settled the pipeline; the rest decided with the user):

- **One change, one roadmap item** (user): S-12 and S-13 merge into S-12 with Change ID `pdf-import`; S-13 is removed as merged and its GitHub issue is closed with a pointer (issue changes only after asking the user, per the repo's issue-sync lesson).
- **Order** (user): shared helper and mixed-format warning first, then mBank (fixtures, then parser and wiring), then Erste (same). After the mBank phases the app is already useful; Erste's phases are separable.
- **Thin shared helper** (user): extract the PDF mechanics (open behind one catch-all, page word/shape model, cell joining, `pl-PL` number parsing, page limit) from `VeloBankPdfParser`; each bank keeps its own row anchoring, recognition and integrity checks. VeloBank is migrated onto the helper and must not change behaviour.
- **Mixed formats** (user): `ImportBatch` records `SourceFormat`; the parse response reports `MixedFormatOverlapCount` = the user's transactions on this account that belong to an import batch of a *different* format than the parsed file and whose date lies within `[earliest, latest]` of the parsed rows (inclusive; zero rows → 0). The frontend shows an amber banner when it is greater than 0; nothing blocks. The commit request carries the format echoed from the parse response (missing → `Csv`, matching every pre-existing batch except VeloBank's). Existing batches are backfilled in the migration: accounts whose `Bank` is `VeloBank` → `Pdf`, everything else → `Csv`.
- **Policy for failed integrity checks** is the VeloBank one (rejection, 422, PII-free message naming the check, page and, when readable, the date); a recognised table row that cannot be read is an integrity failure, not a skipped row.
- **mBank**: `Date` = booking date (equals the CSV's `Date`); `Description` = all lines of the description cell joined top to bottom with single spaces (the operation-type line included — more to categorize by; it is never compared with CSV hashes). Integrity: (1) `opening + amount[0] == balance[0]` and `balance[i-1] + amount[i] == balance[i]` over the file's chronological order, (2) the last balance equals the closing balance, (3) credit count/sum, debit count/sum and total count/net sum equal the turnover summary. A missing opening balance, closing balance or summary on a recognised statement is a rejection. A statement whose "Waluta" is not PLN is read, checked, and returned with no transactions and every row counted in `SkippedErrorCount`.
- **Erste**: `Date` = operation date (the only date the PDF carries), `Description` = the operation cell's lines joined with single spaces, month abbreviations (`sty lut mar kwi maj cze lip sie wrz paź lis gru`) read diacritic-insensitively. Recognition is a composite (the "Lista transakcji" title, a "Konto:" line, the table header words, and at least one table header block of cells on page 1 — the weakest anchor of the three banks, so the structure check is part of it). Integrity is order-independent: over the PLN rows the multiset of "balance before" values (`balance − amount`) must equal the multiset of "balance after" values with exactly one value on each side unmatched (the oldest row's before-balance and the newest row's after-balance); any other difference is a rejection. A foreign-currency row is skipped, counted and disables this check for the file (a skipped row's effect on the printed balance is unverified), as VeloBank's chain does across such a row. The check cannot see a missing oldest or newest row.
- **PDF parser recognition must reject each other**: with three PDF parsers competing in auto-detection, each `CanParse` is tested against the other banks' fixtures.
- **Fixtures** are synthetic like VeloBank's: one generator per bank in the test project (reusing the committed OFL font), two committed PDFs per bank (a short one and a three-page one), edge cases generated in memory; the real samples are used only in manual verification and never copied into the repo.

## Critical Implementation Details

- **Check what PdfPig extracts before choosing anchor words.** `pdftotext` drops Polish diacritics from both samples ("Ksigowania", "pa"); PdfPig's letters may or may not. First dump the words of each real sample (as done for VeloBank) and pick recognition words and month tokens that survive; fold diacritics when comparing month tokens either way.
- **mBank's grid is stroked lines, VeloBank's and Erste's separators are filled rectangles.** The shared helper exposes shapes with their `IsFilled`/`IsStroked` flags; the mBank generator must draw lines (not filled-and-stroked rectangles) so the parser is not tuned to a builder artifact (`PdfPageBuilder.DrawRectangle(fill: true)` also strokes — `VeloBankPdfParser.cs:179-186`).
- **Thousands are separated by a space in all three banks**, so cells are assembled from words, never parsed word by word; the generators write each cell line as one string with real space characters (VeloBank plan, Critical Implementation Details).
- **Migration ordering**: add the column non-null with default `Csv`, then run the backfill `UPDATE` for VeloBank accounts, same shape as `AddAccountBank`.

## Phase 1: Record the scope in PRD and roadmap

### Overview

Make the documents say what is being built, so the roadmap has one item with Change ID `pdf-import` and the PRD covers mBank and Erste PDF.

### Changes Required:

#### 1. PRD amendment

**File**: `context/foundation/prd.md`

**Intent**: Extend the existing PDF requirement from VeloBank to mBank and Erste; edit in place, no version bump.

**Contract**: FR-018 (`:66`) now covers importing a PDF statement from mBank, Erste Bank Polska and VeloBank through the same flow, PLN-only, with the balance-rejection rule kept, plus a sentence that importing a period already imported from the other format on the same account shows a non-blocking overlap warning; the `> Socratic:` note keeps its text; the non-goal line (`:122`) says mBank and Erste are supported through CSV and PDF, VeloBank through PDF only.

#### 2. Roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: One item instead of two, kept consistent across the index tables, streams and handoff.

**Contract**: S-12 becomes "mBank and Erste Bank Polska PDF import" with `Change ID: pdf-import`, `Status: planning`, PRD ref FR-018, prerequisite S-11, the cross-format unknown rewritten as the decided approach (source format on the batch + overlap warning) and the Erste-lossiness note kept as a risk; the S-13 block and its "At a glance" row (`:55-56`), "Backlog Handoff" row (`:279-280`) and the `Parallel with` lines are removed or reworded; stream C (`:66`) reads `… → S-11 (→ S-12 optional)`; the M-1 intent (`:25`) and "Done when" (`:27`) mention only S-12 as the optional follow-up. `updated:` is today's date.

#### 3. GitHub issues

**Intent**: Keep the tracker in step with the roadmap (per `context/foundation/lessons.md`).

**Contract**: after the roadmap edit is approved, ask the user, then retitle issue #29 to the merged S-12 title with label `status: planning` and close #30 with a comment that it was merged into S-12 (`pdf-import`).

### Success Criteria:

#### Automated Verification:

- Roadmap has no S-13 block: `grep -c "^### S-13:" context/foundation/roadmap.md` prints 0 (from repo root)
- Roadmap indexes the merged change and drops the old IDs: `grep -cE "mbank-pdf-import|erste-pdf-import" context/foundation/roadmap.md` prints 0 and `grep -nF "Change ID:** pdf-import" context/foundation/roadmap.md` shows the slice block (from repo root)
- PRD names the banks in FR-018: `grep -n "FR-018" context/foundation/prd.md` shows mBank and Erste in the requirement text (from repo root)

#### Manual Verification:

- You reviewed the PRD and roadmap diffs and approve the wording (FR-018, non-goal line, merged S-12, M-1 note).
- GitHub issue #29 carries `status: planning` and the merged title, and #30 is closed with the merge comment (per `context/foundation/lessons.md`).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Shared PDF helper

### Overview

Extract the bank-independent PDF mechanics from `VeloBankPdfParser` into a small helper and move VeloBank onto it, with no behaviour change.

### Changes Required:

#### 1. Helper

**File**: `MyFinances/backend/Import/Pdf/PdfStatementReader.cs` (new; companion records in the same folder if clearer)

**Intent**: One place for the PdfPig access pattern and the page model every PDF parser consumes, so the three banks cannot drift on error handling.

**Contract**: a static reader that, given a stream, a `maxPages` limit and an `allPages` flag, returns the pages (word texts with centre X/Y and baseline; shapes as bounding rectangles with `IsFilled` and `IsStroked`) or `null` when the bytes are not a `%PDF-` file, the file cannot be opened/read, it exceeds the page limit, or a bank-supplied recognition predicate rejects page 1; it opens from a byte copy, restores a seekable stream's position, never throws, and keeps the single catch-all that wraps open, page read and extraction (`VeloBankPdfParser.cs:109-167`). Also: cell joining (words → lines by baseline within a tolerance → single-space-joined text, `:322-340`), `pl-PL` number parsing with a bounded digit pattern and thousands spaces (`:61-64,396-397`) with the currency left to the caller (mBank has none), and a PII-free rejection factory taking the bank name (`:447-456`). Bank-specific pieces stay in the parsers: recognition words, header/separator/line detection, row reading, integrity checks.

#### 2. VeloBank migration

**File**: `MyFinances/backend/Import/VeloBankPdfParser.cs`

**Intent**: Use the helper without changing what the parser accepts, returns or rejects.

**Contract**: public surface unchanged (`BankName`, `Format`, `maxPages` constructor argument and `DefaultMaxPages`, `CanParse`, `Parse`); the rejection messages stay byte-for-byte the same ("VeloBank statement rejected: …"); `Tests/VeloBankPdfParserTests.cs` and `Tests/VeloBankPdfFixtureTests.cs` are not edited except where they reference moved members.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- VeloBank tests pass unchanged: `dotnet test --filter "VeloBankPdfParserTests|VeloBankPdfFixtureTests"` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- A temporary local test (not committed) run on the real 90-day and one-year VeloBank PDFs still returns 17 and 62 rows with no integrity exception and identical date, amount and description for the 17 shared rows.
- The refactor did not add any real personal data to the repository.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Source format on import batches and the overlap warning

### Overview

Record the format each import came from and warn when a new import's date range overlaps rows that came from the other format on the same account. Independent of the new parsers; verifiable with the existing CSV and VeloBank PDF imports.

### Changes Required:

#### 1. Batch source format and migration

**Files**: `MyFinances/backend/Transactions/ImportBatch.cs`, `MyFinances/backend/AppDbContext.cs`, `MyFinances/backend/Migrations/<timestamp>_AddImportBatchSourceFormat.cs` (generated)

**Intent**: Persist whether a batch was imported from CSV or PDF.

**Contract**: `ImportBatch.SourceFormat` of type `StatementFormat`, stored as text (default `Csv`, so existing tests that construct batches keep compiling); the migration adds the column non-null with default `Csv` and then backfills `Pdf` for batches whose `Accounts.Bank` is `VeloBank`, in the style of `20261002054938_AddAccountBank.cs`.

#### 2. Contracts and endpoint

**Files**: `MyFinances/backend/Import/ImportContracts.cs`, `MyFinances/backend/Import/ImportEndpoints.cs`

**Intent**: Carry the format from parse to commit and compute the overlap on parse.

**Contract**: `ImportParseResponse` gains `Format` (the parser's `StatementFormat`) and `MixedFormatOverlapCount`; `ImportCommitRequest` gains `SourceFormat` (missing in older clients → `Csv`) which `/import/commit` stores on the new batch. `/import/parse` sets `MixedFormatOverlapCount` to the number of the user's transactions on the selected account whose `ImportBatch.SourceFormat` differs from `parser.Format` and whose `Date` is in `[min, max]` of the parsed rows' dates, inclusive; no parsed rows → 0; transactions without a batch never count; other accounts and other users never count.

#### 3. Frontend banner

**File**: `MyFinances/frontend/app/routes/import.tsx`

**Intent**: Tell the user, without blocking, that the file overlaps rows imported from the other format, and keep the format flowing back to commit.

**Contract**: `ImportParseResponse` type gains `format` and `mixedFormatOverlapCount`; the commit call (`:177-183`) sends the echoed `SourceFormat`; when the count is greater than 0 an amber banner (same style as the `bankMismatch` one, `:252`) says that N transactions in this period were imported from the other format (CSV/PDF), that their descriptions and dates differ between formats so duplicates will not be detected, and that the user can still continue.

#### 4. Tests

**Files**: `MyFinances/backend/Tests/ImportEndpointsTests.cs` (and the commit/summary assertions near `:347-350`)

**Intent**: Pin the warning's boundaries and the stored format.

**Contract**: parse warns with the right count when overlapping other-format batch rows exist (a seeded `Pdf` batch plus a CSV upload on the same account); no warning for same-format overlap, for other-format rows entirely outside the range, for rows exactly at the range's first and last date (counted, inclusive), for manual entries, for another account of the same user, or for another user; zero rows → 0; commit stores the `SourceFormat` sent and defaults to `Csv` when it is absent; the migration backfill is covered by a test or, if migrations are not exercised by the suite, by the manual step below.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Import endpoint tests pass: `dotnet test --filter ImportEndpointsTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)
- Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- After the migration on the dev database, existing batches on VeloBank accounts show `Pdf` and all others `Csv`.
- With a dev batch's `SourceFormat` flipped to `Pdf` by SQL, re-uploading the same mBank CSV into that account shows the amber overlap banner with the right count and the import can still be committed; with the original value restored the banner is gone.
- Importing an mBank CSV, a VeloBank PDF and re-importing both behaves as before apart from the banner.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: mBank synthetic PDF fixtures

### Overview

Build a generator that reproduces the measured mBank layout from invented data and commit two PDFs, so parser tests run against realistic mBank PDFs without personal data.

### Changes Required:

#### 1. Generator

**Files**: `MyFinances/backend/Tests/Support/MBankPdfBuilder.cs`, `MyFinances/backend/Tests/Support/MBankSampleData.cs` (new)

**Intent**: Produce an mBank-style "Elektroniczne zestawienie operacji" PDF from a row model so tests and fixtures never need real data.

**Contract**: input = header data (fake holder, account number, period, "Waluta"), opening balance, rows (`BookingDate`, `OperationDate`, description lines, `Amount`, `Balance`) and layout options (rows per page, tamper hooks for tests); the turnover summary and closing balance are computed from the rows unless a hook overrides them. Output reproduces the measured geometry: page about 595×828 pt, letterhead block, account block, summary table (credits, debits, total with counts and sums), table header, stroked horizontal grid lines with column edges near x 40 / 96.7 / 148.2 / 452.2 / 503.7 / 555.2, a date line per row at x ≈ 48 with amount and balance on that line and the description continuing on the following lines at x ≈ 152, "Saldo początkowe" above the table, "Saldo końcowe" beside the last row, the repeated table header and boilerplate footer with "Strona : n / N" per page. Lines are drawn as lines, text cells as single strings with real spaces (Critical Implementation Details). Row pitch and font sizes are measured from the real sample by a throwaway PdfPig dump (not committed).

#### 2. Committed fixtures

**Files**: `MyFinances/backend/Tests/Fixtures/mbank-pdf-sample-synthetic.pdf`, `mbank-pdf-sample-synthetic-multipage.pdf`; registered in `Tests/MyFinances.Api.Tests.csproj` with `CopyToOutputDirectory = PreserveNewest`

**Intent**: Real-format PDFs for parser and endpoint tests.

**Contract**: a two-page file mirroring the real shape (about 20 rows oldest first; card purchase with `DATA TRANSAKCJI`, BLIK, a positive `POS ZWROT TOWARU` refund, an incoming external transfer with name/address/account/title lines, at least one balance of 1 000 or more, summary and closing balance consistent with the rows); a three-page file with exactly 40 rows, header and footer repeated per page and balances continuous across pages. All names, account numbers and card data are obviously fake.

#### 3. Fixture tests and regeneration

**File**: `MyFinances/backend/Tests/MBankPdfFixtureTests.cs` (new)

**Intent**: Keep the PDFs reproducible and prove their shape without making regeneration part of normal runs.

**Contract**: a test that returns immediately unless `REGENERATE_PDF_FIXTURES=1` is set (same switch as `VeloBankPdfFixtureTests`), then writes both PDFs into the test project's `Fixtures` source folder; plus checks that both open with PdfPig (2 and 3 pages), contain the title and header text, have one date-led line per row, and that a split balance such as `3 473,70` appears as separate words; and that `MBankCsvParser.CanParse`, `ErsteCsvParser.CanParse` and `VeloBankPdfParser.CanParse` are false for both PDFs without throwing.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Fixture tests pass: `dotnet test --filter MBankPdfFixtureTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Both fixture PDFs open in a PDF viewer and look like the real mBank statement in shape (letterhead, summary table, grid, per-page header, footer).
- The fixtures and generator contain no real name, address, account number, card number or counterparty from `mbank.pdf`.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: mBank PDF parser and wiring

### Overview

Implement `MBankPdfParser` behind the existing interface, register it, and prove it on the synthetic fixtures and, manually, on the real sample.

### Changes Required:

#### 1. Parser

**File**: `MyFinances/backend/Import/MBankPdfParser.cs` (new)

**Intent**: Recognise an mBank statement PDF and turn its operations table into `NormalizedTransaction`s, rejecting statements whose own figures do not add up.

**Contract**:
- `BankName = "mBank"`, `Format = StatementFormat.Pdf`; optional `maxPages` constructor argument (default 200) as for VeloBank; built on the shared helper. `CanParse`: page 1 shows the statement title words, "mBank" and the table header words (anchor words chosen from what PdfPig actually extracts from the real sample — Critical Implementation Details); never throws; restores position.
- `Parse`: column edges from the stroked horizontal lines, rows anchored by lines whose first column holds an ISO date, later lines' description-column text appended until the next date line or a non-date first-column token (page headers, summary labels, footers end a row); read opening balance, closing balance and the turnover summary from their own lines. `Date` = booking date (first date column), `Description` = the description cell's lines joined with single spaces, `Amount` and balance parsed with the helper (no currency in the cell; the statement currency comes from "Waluta").
- Integrity (`StatementIntegrityException`, PII-free message naming the failing check, page and, when readable, the booking date): the chronological balance chain from the opening balance, the closing balance equal to the last row's balance, credit/debit/total counts and sums equal to the summary, a missing opening balance, closing balance or summary on a recognised statement, and an unreadable date, amount or balance in a recognised row. A statement whose "Waluta" is not PLN returns no transactions and every row in `SkippedErrorCount`, after the same checks.
- Not a mBank statement, or an unreadable/over-limit/corrupt PDF → empty `ParseResult`, skipped 0, never an exception (manual-fallback contract).

#### 2. DI registration and bank list

**Files**: `MyFinances/backend/DI/ImportServiceCollectionExtensions.cs`, `MyFinances/backend/Tests/AccountEndpointsTests.cs`

**Intent**: Make the endpoint use the parser; confirm the bank list is unaffected.

**Contract**: `services.AddScoped<IBankStatementParser, MBankPdfParser>();` beside the other PDF parser; the `GET /accounts/banks` assertion in `AccountEndpointsTests.cs` stays `["mBank", "Erste", "VeloBank", "Other"]` (the existing `Distinct()` already absorbs the second mBank parser).

#### 3. Parser and endpoint tests

**Files**: `MyFinances/backend/Tests/MBankPdfParserTests.cs` (new), `MyFinances/backend/Tests/ImportEndpointsTests.cs`

**Intent**: Pin recognition, parsing, the three integrity checks, currency handling and the end-to-end HTTP behaviour.

**Contract**: `CanParse` true for both fixtures and false for the mBank CSV fixtures, the VeloBank PDF fixtures, a valid non-mBank PDF, `%PDF-` plus garbage, and a PDF over a small `maxPages`; `VeloBankPdfParser.CanParse` is false for the mBank fixtures; position restored. Two-page fixture → expected row count, booking dates, signed amounts, the multi-line description joined with single spaces, a balance of 1 000+ unharmed, Polish diacritics intact, order oldest first. Multi-page fixture → 40 rows, repeated headers/footers produce no rows. In-memory variants: tampered balance, tampered closing balance, tampered summary count and sum, missing opening balance, unreadable amount cell → `StatementIntegrityException` whose message names the check and page and holds no amount, description or name; a "Waluta" other than PLN → no transactions, all rows skipped; corrupt or non-mBank PDF → empty result. Endpoint tests (reusing `BuildUploadRequestAsync` with `application/pdf`): fixture with an "mBank" account → 200, `Bank == "mBank"`, `BankMismatch == false`, `Format == Pdf`; with a "VeloBank" account → `BankMismatch == true`; commit then re-parse → every row duplicate; tampered statement → 422 with the integrity title and no rows; `bank=mBank` with a corrupt `%PDF-` file → 200 with zero rows; an mBank CSV into an account that already holds a `Pdf` batch overlapping its range → `MixedFormatOverlapCount` greater than 0.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Parser tests pass: `dotnet test --filter MBankPdfParserTests` (from `MyFinances/backend`)
- Import endpoint and account tests pass: `dotnet test --filter "ImportEndpointsTests|AccountEndpointsTests"` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- A temporary local test (not committed) on the real `mbank.pdf` returns 32 rows with no integrity exception, and the credit/debit/total counts and sums equal the statement's 7 / 4 274,36, 25 / 2 484,84 and 32 / 1 789,52.
- On the import page, uploading `mbank.pdf` into an mBank account auto-detects mBank and lists the 32 rows; committing and re-uploading flags every row as a duplicate; a tampered synthetic PDF is rejected with the explanatory message and no bank picker.
- Importing an mBank CSV whose dates overlap the committed PDF rows (or vice versa) shows the overlap banner with a plausible count.
- No real personal data was added to the repository by this phase.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 6: Erste synthetic PDF fixtures

### Overview

Build a generator that reproduces the measured Erste layout from invented data and commit two PDFs.

### Changes Required:

#### 1. Generator

**Files**: `MyFinances/backend/Tests/Support/ErstePdfBuilder.cs`, `MyFinances/backend/Tests/Support/ErsteSampleData.cs` (new)

**Intent**: Produce an Erste-style "Lista transakcji" PDF from a row model.

**Contract**: input = account header ("Konto:" line), rows (operation date, operation text lines, `Amount`, `Currency`, `Balance`) and layout options (rows per page, same-day ordering independent of the balance chain, tamper hooks). Output reproduces the measured geometry: A4, column edges near x 6.7 / 123 / 402.7 / 495.7 / 588.7, a header row with "Data operacji / Operacja / Kwota / Saldo", 30 pt row bands closed by thin filled separators, the date cell with `dd mmm yyyy` (Polish abbreviations) and, beneath it, the label "Data księgowania" with no value, amounts and balances as `-0,10 PLN` / `1 098,64 PLN`, "Strona n z N" and "Dokument z dnia: …" in the footer. Text cells are single strings with real spaces; measurements come from a throwaway dump of the real sample.

#### 2. Committed fixtures

**Files**: `MyFinances/backend/Tests/Fixtures/erste-pdf-sample-synthetic.pdf`, `erste-pdf-sample-synthetic-multipage.pdf`; registered in the test `.csproj` with `CopyToOutputDirectory = PreserveNewest`

**Intent**: Real-format PDFs for parser and endpoint tests.

**Contract**: a one-page file (about 12 rows, newest first by booking date, same-day rows whose printed order does not follow the balance, a refund with a two-line operation cell, a group of rows sharing date, amount and description, a balance of 1 000+); a three-page file with exactly 40 rows, header and footer repeated per page. All names and account numbers are fake.

#### 3. Fixture tests and regeneration

**File**: `MyFinances/backend/Tests/ErstePdfFixtureTests.cs` (new)

**Intent**: Same regeneration switch and shape checks as the mBank and VeloBank fixtures.

**Contract**: returns immediately unless `REGENERATE_PDF_FIXTURES=1`; fixtures open with PdfPig (1 and 3 pages), contain "Lista transakcji" and the header words, have one separator per row, the split balance appears as separate words, the month abbreviations are Polish; the other banks' CSV and PDF parsers return false from `CanParse` for both PDFs without throwing.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Fixture tests pass: `dotnet test --filter ErstePdfFixtureTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Both fixture PDFs open in a PDF viewer and look like the real Erste statement in shape.
- The fixtures and generator contain no real name, account number or counterparty from `erste.pdf`.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 7: Erste PDF parser and wiring

### Overview

Implement `ErstePdfParser`, register it, finish the copy, and prove it on the fixtures and the real sample.

### Changes Required:

#### 1. Parser

**File**: `MyFinances/backend/Import/ErstePdfParser.cs` (new)

**Intent**: Recognise an Erste Bank Polska statement PDF and turn its transaction list into `NormalizedTransaction`s, rejecting files whose balances do not link up.

**Contract**:
- `BankName = "Erste"`, `Format = StatementFormat.Pdf`; optional `maxPages` (default 200); built on the shared helper. `CanParse` is the composite from the Implementation Approach: title, "Konto:" line, header words and at least one table header block on page 1; never throws; restores position.
- `Parse`: band-anchored like VeloBank (header-cell rectangles give column edges and header intervals, thin first-column rectangles give row separators, words assigned by centre; the header repeats on every page and the footer follows the table). Date cell: first line parsed as `dd mmm yyyy` with the Polish abbreviations, diacritic-insensitive; the "Data księgowania" label line is ignored; an unknown month token or unreadable date is an integrity failure. `Description` = operation cell lines joined with single spaces; amount cell `-0,10 PLN` → signed decimal plus currency; balance cell likewise. Non-PLN rows are skipped, counted and disable the balance check for the file.
- Integrity (`StatementIntegrityException`, PII-free message naming the failing check, page and, when readable, the operation date): the order-independent balance linkage from the Implementation Approach, and an unreadable row inside a recognised table. A page after the first with no header block is a failure, as for VeloBank.
- Not an Erste statement or an unreadable/over-limit/corrupt PDF → empty result, skipped 0, never an exception.

#### 2. DI registration, copy

**Files**: `MyFinances/backend/DI/ImportServiceCollectionExtensions.cs`, `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Make the endpoint use the parser and keep the user-facing copy truthful.

**Contract**: `services.AddScoped<IBankStatementParser, ErstePdfParser>();`; the landing copy (`home.tsx:12`) says PDF statements are accepted for mBank, Erste and VeloBank, CSV for mBank, Revolut and Erste, if it does not already.

#### 3. Parser and endpoint tests

**Files**: `MyFinances/backend/Tests/ErstePdfParserTests.cs` (new), `MyFinances/backend/Tests/ImportEndpointsTests.cs`

**Intent**: Pin recognition, parsing, the order-independent check, currency handling and the end-to-end flow, plus the three-way PDF recognition matrix.

**Contract**: `CanParse` true for both Erste fixtures and false for the Erste CSV fixtures, the mBank and VeloBank PDF fixtures, a valid non-Erste PDF whose page 1 lacks the table header block, `%PDF-` plus garbage, and a PDF over a small `maxPages`; `MBankPdfParser` and `VeloBankPdfParser` are false for the Erste fixtures; position restored. One-page fixture → expected rows, operation dates (including a `paź` date), signed amounts, a two-line operation cell joined with a single space, balance of 1 000+ unharmed, same-day rows in printed order. Multi-page fixture → 40 rows. In-memory variants: a tampered balance and a deleted middle row → `StatementIntegrityException` naming the check, with no amounts or names; a deleted newest or oldest row passes (the documented blind spot — asserted so it stays a conscious choice); an unknown month token and an unreadable amount cell → rejection; a EUR row → skipped count 1, row absent, balance check disabled; corrupt or non-Erste PDF → empty result. Endpoint tests: fixture with an "Erste" account → 200, `Bank == "Erste"`, `Format == Pdf`, `BankMismatch == false`; with an "mBank" account → `BankMismatch == true`; commit then re-parse → every row duplicate, including the group of identical-key rows; tampered statement → 422; the three PDF fixture families each auto-detect to their own bank in one test run.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Parser tests pass: `dotnet test --filter ErstePdfParserTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)
- Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- A temporary local test (not committed) on the real `erste.pdf` returns 55 rows with no integrity exception and no unreadable row.
- On the import page, uploading `erste.pdf` into an Erste account auto-detects Erste and lists the rows; committing and re-uploading flags every row as a duplicate; importing an Erste CSV for an overlapping period shows the overlap banner.
- mBank CSV, Erste CSV, VeloBank PDF and mBank PDF imports still behave as before.
- Imported rows appear in the categorization queue and the dashboard history with their dates.
- No real personal data was added to the repository by this phase.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding.

---

## Testing Strategy

### Unit Tests:

- `MBankPdfFixtureTests` / `ErstePdfFixtureTests` (Phases 4, 6): fixtures open with PdfPig, page counts, header text, one row anchor per row, split balances, other parsers reject them, opt-in regeneration.
- `MBankPdfParserTests` (Phase 5): recognition (positive and negative), counts, dates, amounts, multi-line descriptions, multi-page headers/footers, the five integrity failures, non-PLN statement, empty results for foreign and corrupt PDFs.
- `ErstePdfParserTests` (Phase 7): recognition including the structure check, Polish month parsing, order-independent linkage (tamper, deleted middle row, accepted deleted extreme rows), foreign-currency row, empty results.
- Existing `VeloBankPdfParserTests` / `VeloBankPdfFixtureTests` unchanged and green after the Phase 2 refactor.

### Integration Tests:

- `ImportEndpointsTests` (Phases 3, 5, 7): `Format` and `MixedFormatOverlapCount` boundaries, stored `SourceFormat`, per-bank PDF parse/commit/re-import duplicates, bank mismatch, 422 mapping, manual fallbacks, the three PDF families auto-detecting to their own bank.
- `AccountEndpointsTests`: bank list unchanged.

### Manual Testing Steps:

1. Start backend (`dotnet run` from `MyFinances/backend`) and frontend (`npm run dev` from `MyFinances/frontend`), log in, make sure an mBank and an Erste account exist in settings.
2. Import `D:\repos\michLukaszewicz\Przykładowe pliki\mbank\mbank.pdf` into the mBank account; check the 32 rows against the PDF; commit; re-import and confirm all rows are duplicates.
3. Import `D:\repos\michLukaszewicz\Przykładowe pliki\erste\erste.pdf` into the Erste account; same checks with the 55 rows.
4. Import one of the CSV files from the same folders for an overlapping period into the same account and confirm the overlap banner and count.
5. Import an existing VeloBank PDF and the mBank and Erste CSVs to confirm no regression.

## Performance Considerations

Statements are tens to low hundreds of rows over a few pages, parsed fully in memory; the 5 MB upload cap and the 200-page limit apply to all three PDF parsers, and the PdfPig decompression caveat accepted for VeloBank (`pdf-statement-import` plan) applies unchanged. The overlap warning is one indexed range query per parse (account, date range, batch format) over a single user's data.

## Migration Notes

One additive migration: `ImportBatches.SourceFormat`, non-null, default `Csv`, backfilled to `Pdf` for batches on accounts whose `Bank` is `VeloBank`. No transaction rows or hashes change; existing mBank and Erste data stay valid. Rolling back drops the column.

## References

- Related research: `context/changes/pdf-statement-import/research.md` (§2b mBank, §2c Erste, §4 cross-cutting points)
- Pattern plan: `context/changes/pdf-statement-import/plan.md`
- Pattern parser and tests: `MyFinances/backend/Import/VeloBankPdfParser.cs`, `MyFinances/backend/Tests/VeloBankPdfParserTests.cs`, `MyFinances/backend/Tests/Support/VeloBankPdfBuilder.cs`
- Migration pattern: `MyFinances/backend/Migrations/20261002054938_AddAccountBank.cs`
- Roadmap/PRD: `context/foundation/roadmap.md` (merged S-12), `context/foundation/prd.md` (FR-018)
- Lessons: `context/foundation/lessons.md` (sync the GitHub issue with roadmap status)
- Real samples (outside the repo, contain personal data — never copy into the repo): `D:\repos\michLukaszewicz\Przykładowe pliki\mbank\mbank.pdf`, `D:\repos\michLukaszewicz\Przykładowe pliki\erste\erste.pdf`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Record the scope in PRD and roadmap

#### Automated

- [x] 1.1 Roadmap has no S-13 block: `grep -c "^### S-13:" context/foundation/roadmap.md` prints 0 (from repo root) — 85afcc4
- [x] 1.2 Roadmap indexes the merged change and drops the old IDs: `grep -cE "mbank-pdf-import|erste-pdf-import" context/foundation/roadmap.md` prints 0 and `grep -nF "Change ID:** pdf-import" context/foundation/roadmap.md` shows the slice block (from repo root) — 85afcc4
- [x] 1.3 PRD names the banks in FR-018: `grep -n "FR-018" context/foundation/prd.md` shows mBank and Erste in the requirement text (from repo root) — 85afcc4

#### Manual

- [x] 1.4 You reviewed the PRD and roadmap diffs and approve the wording (FR-018, non-goal line, merged S-12, M-1 note). — 85afcc4
- [x] 1.5 GitHub issue #29 carries `status: planning` and the merged title, and #30 is closed with the merge comment (per `context/foundation/lessons.md`). — 85afcc4

### Phase 2: Shared PDF helper

#### Automated

- [x] 2.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [x] 2.2 VeloBank tests pass unchanged: `dotnet test --filter "VeloBankPdfParserTests|VeloBankPdfFixtureTests"` (from `MyFinances/backend`)
- [x] 2.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [x] 2.4 A temporary local test (not committed) run on the real 90-day and one-year VeloBank PDFs still returns 17 and 62 rows with no integrity exception and identical date, amount and description for the 17 shared rows.
- [x] 2.5 The refactor did not add any real personal data to the repository.

### Phase 3: Source format on import batches and the overlap warning

#### Automated

- [ ] 3.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 3.2 Import endpoint tests pass: `dotnet test --filter ImportEndpointsTests` (from `MyFinances/backend`)
- [ ] 3.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)
- [ ] 3.4 Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- [ ] 3.5 Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual

- [ ] 3.6 After the migration on the dev database, existing batches on VeloBank accounts show `Pdf` and all others `Csv`.
- [ ] 3.7 With a dev batch's `SourceFormat` flipped to `Pdf` by SQL, re-uploading the same mBank CSV into that account shows the amber overlap banner with the right count and the import can still be committed; with the original value restored the banner is gone.
- [ ] 3.8 Importing an mBank CSV, a VeloBank PDF and re-importing both behaves as before apart from the banner.

### Phase 4: mBank synthetic PDF fixtures

#### Automated

- [ ] 4.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 4.2 Fixture tests pass: `dotnet test --filter MBankPdfFixtureTests` (from `MyFinances/backend`)
- [ ] 4.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 4.4 Both fixture PDFs open in a PDF viewer and look like the real mBank statement in shape (letterhead, summary table, grid, per-page header, footer).
- [ ] 4.5 The fixtures and generator contain no real name, address, account number, card number or counterparty from `mbank.pdf`.

### Phase 5: mBank PDF parser and wiring

#### Automated

- [ ] 5.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 5.2 Parser tests pass: `dotnet test --filter MBankPdfParserTests` (from `MyFinances/backend`)
- [ ] 5.3 Import endpoint and account tests pass: `dotnet test --filter "ImportEndpointsTests|AccountEndpointsTests"` (from `MyFinances/backend`)
- [ ] 5.4 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 5.5 A temporary local test (not committed) on the real `mbank.pdf` returns 32 rows with no integrity exception, and the credit/debit/total counts and sums equal the statement's 7 / 4 274,36, 25 / 2 484,84 and 32 / 1 789,52.
- [ ] 5.6 On the import page, uploading `mbank.pdf` into an mBank account auto-detects mBank and lists the 32 rows; committing and re-uploading flags every row as a duplicate; a tampered synthetic PDF is rejected with the explanatory message and no bank picker.
- [ ] 5.7 Importing an mBank CSV whose dates overlap the committed PDF rows (or vice versa) shows the overlap banner with a plausible count.
- [ ] 5.8 No real personal data was added to the repository by this phase.

### Phase 6: Erste synthetic PDF fixtures

#### Automated

- [ ] 6.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 6.2 Fixture tests pass: `dotnet test --filter ErstePdfFixtureTests` (from `MyFinances/backend`)
- [ ] 6.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 6.4 Both fixture PDFs open in a PDF viewer and look like the real Erste statement in shape.
- [ ] 6.5 The fixtures and generator contain no real name, account number or counterparty from `erste.pdf`.

### Phase 7: Erste PDF parser and wiring

#### Automated

- [ ] 7.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 7.2 Parser tests pass: `dotnet test --filter ErstePdfParserTests` (from `MyFinances/backend`)
- [ ] 7.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)
- [ ] 7.4 Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- [ ] 7.5 Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual

- [ ] 7.6 A temporary local test (not committed) on the real `erste.pdf` returns 55 rows with no integrity exception and no unreadable row.
- [ ] 7.7 On the import page, uploading `erste.pdf` into an Erste account auto-detects Erste and lists the rows; committing and re-uploading flags every row as a duplicate; importing an Erste CSV for an overlapping period shows the overlap banner.
- [ ] 7.8 mBank CSV, Erste CSV, VeloBank PDF and mBank PDF imports still behave as before.
- [ ] 7.9 Imported rows appear in the categorization queue and the dashboard history with their dates.
- [ ] 7.10 No real personal data was added to the repository by this phase.
