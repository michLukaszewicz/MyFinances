# PDF Statement Import (VeloBank) Implementation Plan

## Overview

Add PDF as a second statement input format next to CSV. VeloBank offers no CSV export, so it becomes the first bank imported only through PDF: a format-aware import pipeline, a `VeloBankPdfParser` built on PdfPig that reads the table geometry deterministically, and synthetic PDF fixtures (the real samples contain personal data). mBank and Erste PDF parsers are recorded as optional follow-up roadmap slices (S-12, S-13), not built here.

## Current State Analysis

- `IBankStatementParser` has `BankName`, `CanParse(Stream)` and `Parse(Stream)` returning `ParseResult(IReadOnlyList<NormalizedTransaction>, SkippedErrorCount)`; `NormalizedTransaction` is `(DateOnly Date, string Description, decimal Amount)` — `MyFinances/backend/Import/IBankStatementParser.cs:11-21`, `Import/NormalizedTransaction.cs:5`.
- `/import/parse` copies the upload to a `MemoryStream`, picks `parsers.FirstOrDefault(p => p.CanParse(stream))`, falls back to the first parser whose `BankName` equals the manually chosen `bank`, then parses; a missing parser returns 400 "Could not recognize this file's bank format…" — `Import/ImportEndpoints.cs:35-56`. `BankMismatch` compares `parser.BankName` with `account.BankName` — `ImportEndpoints.cs:85`.
- The only consumers of the parser collection are `ImportEndpoints` (two spots) and `GET /accounts/banks` (`Select(p => p.BankName).Distinct()` plus "Other") — `Transactions/AccountEndpoints.cs:17-26`. Parsers are registered one `AddScoped` line each — `DI/ImportServiceCollectionExtensions.cs:10-15`.
- Both CSV parsers decode leniently and never throw in `CanParse`; Erste recognises by the first line only, mBank by the `#Data księgowania` header prefix — `Import/ErsteCsvParser.cs:23-32,46-80`, `Import/MBankCsvParser.cs:104-126`.
- `DedupHash` = userId + date + amount + verbatim description + accountId, no normalization — `Import/DedupHash.cs:11-24`. Upload cap is 5 MB — `Program.cs:20-24`.
- Frontend: `SUPPORTED_BANKS = ["mBank", "Erste"]` (`MyFinances/frontend/app/routes/import.tsx:54`), file input `accept=".csv"` with label "Bank statement CSV" (`:416,:421`), and **any HTTP 400 from `/import/parse` reveals the manual bank picker** (`:135-137`); landing copy says "Upload bank CSV exports…" (`app/routes/home.tsx:12`).
- Tests: parser tests open fixtures from `AppContext.BaseDirectory/Fixtures` (`Tests/ErsteCsvParserTests.cs:9-16`); each fixture is registered individually with `CopyToOutputDirectory` (`Tests/MyFinances.Api.Tests.csproj:27-52`); `BuildUploadRequestAsync` hardcodes `text/csv` and `export.csv` (`Tests/ImportEndpointsTests.cs:69-85`); `GET /accounts/banks` is asserted exactly at `Tests/AccountEndpointsTests.cs:92`.
- PRD excludes banks other than mBank, Revolut and Erste (`context/foundation/prd.md:120`); FR-002/FR-003 are CSV-only; the roadmap has no item with Change ID `pdf-statement-import`.

### VeloBank PDF ("Historia rachunku") — measured on two real samples (1 page, 4 pages; both outside the repo)

- Text layer with Polish diacritics; wkhtmltopdf output. The table is drawn as filled thin rectangles: five columns (transaction date | booking date | description | amount | balance after) with cell edges at x ≈ 26.2 / 89.2 / 153.9 / 440.2 / 504.4 / 567.9 pt, 28 pt tall header cells, one ~1.2 pt separator per row spanning the first column.
- Date, amount and balance are vertically centred in the row while the description (1-5 lines) starts at the top, so grouping words by text baseline mixes rows; rows must come from separator bands. A thousands space splits amounts into words (`1` + `014,84`).
- The header repeats on every page (and once mid-page in the 1-page sample); a legal footer follows each page's table; no row is split across a page.
- Pending rows have booking date `-` and balance `-`; their description has the same template as booked rows.
- Prototype results: 17/17 and 62/62 rows; balance chain `balance[i] − amount[i] == balance[i+1]` held on 14/14 and 59/59 booked pairs; the card amount repeated in the description matched on 13/13 and 49/49 rows; all 17 rows of the short export appear with identical date, amount and description in the long export.

## Desired End State

A user with a VeloBank account uploads a "Historia rachunku" PDF on the import page; the bank is auto-detected, every transaction is listed (pending ones included, dated by transaction date), duplicates are flagged on re-import, and committed rows flow into categorization, charts and transfer detection like CSV imports. A PDF whose running balances do not add up is rejected with an explanatory message instead of being imported. Choosing a bank without PDF support for a PDF gives a specific message. CSV imports behave exactly as before. PRD and roadmap record VeloBank and the PDF format. Verify with `dotnet test`, `npm run typecheck`, `npm run build` and a manual import of the real samples.

### Key Discoveries:

- Only `ImportEndpoints` needs logic changes; `AccountEndpoints` already tolerates several parsers per bank name via `Distinct()` — `Transactions/AccountEndpoints.cs:19-22`.
- A recognised-but-inconsistent file needs a different HTTP status from "not recognised": the frontend treats every 400 as "show the bank picker" (`import.tsx:135-137`), which would mislead after an integrity rejection. Use 422 for integrity failures; only 400 keeps meaning "not recognised / format unsupported for the chosen bank".
- The existing manual-fallback contract (explicit bank + unreadable content → 200 with zero rows, `Tests/ImportEndpointsTests.cs:436-454`) must hold for the PDF parser too.
- PdfPig's package id is `PdfPig`; `UglyToad.PdfPig` on nuget.org is a different package. A PdfPig-generated PDF round-trips Polish diacritics with an embedded TrueType font and supports filled thin rectangles (spike on 0.1.16), although the library's README still claims ASCII-only writing; words drawn with a gap of about one space width or less merge (`1` + `014,84` became `1014,84`), so the generator should use real space characters or a gap clearly above one space width.
- Pending and booked rows share a description template in both samples, but no pending-to-booked pair has been observed.

## What We're NOT Doing

- mBank and Erste PDF parsers — recorded as proposed roadmap slices S-12/S-13 with their own plans; Erste PDF is lossy versus its CSV (no booking date, counterparty-only description).
- Any cross-format dedup policy, description normalization or rehash migration — only relevant once a bank has both a CSV and a PDF parser.
- LLM extraction, OCR for scanned PDFs, PDF-to-CSV conversion.
- Revolut (S-07 stays `proposed`).
- Checking the statement's account number or holder against the selected account; using the statement's period label.
- Foreign-currency conversion — non-PLN rows are skipped and counted (PRD FR-003 / non-goals).
- A special message for password-protected or corrupt PDFs — they count as unreadable (no rows on manual fallback, not recognised on auto-detect).
- A shared generic PDF table helper — extract one only when a second PDF bank exists.
- Excel or other formats (the format discriminator only makes room for them).
- Changes to `DedupHash`, `ParseResult`, the commit endpoint or the database schema.
- Skipping pending rows — decided: they are imported.
- The CLAUDE.md project-status line — updated at archive time as for previous slices.

## Implementation Approach

Decisions taken during planning (research settled the approach; the rest decided with the user):

- **Parser per bank and format**, all of a bank's parsers sharing one `BankName`; `IBankStatementParser` gains a `Format`. The endpoint sniffs the upload's format once from its first bytes (`%PDF-` → PDF, anything else → CSV, so today's behaviour is unchanged) and considers only parsers of that format.
- **Manual fallback** resolves by `bank` within the sniffed format. If the bank exists but has no parser for that format, return 400 `"<bank> import does not support <PDF|CSV> files."`; if nothing is recognised and no bank is given, keep the existing 400 text. Explicit bank + unreadable content keeps returning 200 with zero rows.
- **Date** = transaction date, because pending rows have no booking date and the hash must not change when a row is booked. **Description** = the description cell's lines joined top to bottom with a single space. **Amount** = the amount cell parsed with `pl-PL`.
- **Pending rows are imported** (user decision, overriding the skip recommendation): they carry the transaction date and are excluded from the balance check. Risk: if a booked description or amount ever differs from its pending one (a card hold may settle for a different amount), that payment would be counted twice; verified after the fact (see Manual Testing Steps).
- **Integrity**: the parser throws `StatementIntegrityException`; the endpoint returns 422 with its message and imports nothing. Checks (the user chose rejection for the balance check; the two sibling checks use the same policy by assumption): (1) balance chain across consecutive booked PLN rows in list order (newest first: `older.Balance == newer.Balance − newer.Amount`), where pending rows are ignored and a skipped non-PLN row breaks the chain — nothing is compared across it, because its effect on the printed balance is unverified; (2) for PLN card rows whose description also names PLN, the amount printed in the description equals the absolute amount column (a description naming another currency skips this check for that row, the row is still imported); (3) a row in a recognised table that cannot be read is an integrity failure, not a skipped row. The 422 title names the failing check, the page and, when readable, the row's transaction date (so a false reject can be located), and still contains no amounts, descriptions or names.
- **Non-PLN rows** are skipped, counted in `SkippedErrorCount`, and excluded from checks.
- **Limits**: PDFs over 200 pages are treated as unreadable; the 5 MB upload cap stays.
- **Fixtures** are synthetic: a generator in the test project (PdfPig builder + an OFL-licensed font) writes two committed PDFs; edge cases (foreign currency, tampered balance, unreadable cell, non-VeloBank PDF) are generated in memory. Real samples are only used in manual verification.
- **PRD/roadmap**: minimal PRD edit (new FR-018, wording, non-goal line) and roadmap slices S-11 (this change), S-12 and S-13 (optional, outside the M-1 done criterion).

## Critical Implementation Details

**PDF parsing gotchas (verified on the real samples)**

- Build rows from separator bands, never from text baselines; assign words to bands and columns by their centres, and tolerate sub-point shifts of column edges between header blocks on one page.
- Assemble amounts and balances per cell: PdfPig returns `1 014,84 PLN` as separate words.
- Open the PDF from a copy of the bytes so the caller's stream position is never disturbed, and never let `CanParse` throw.
- The synthetic generator writes each cell line as one string containing real space characters (those split into separate words in every probe, NBSP included); first confirm on a letter dump of a real sample that its thousands separator is a space glyph. If it is only a positional gap, draw the parts separately with a gap of at least 1.1 space widths: exactly one space width (0.278 em) still merged `1` and `014,84` into `1014,84`, and PdfPig's adaptive gap rule makes the outcome depend on the other text on the page. A fixture test asserts the split balance parses correctly.
- `PdfPageBuilder.DrawRectangle(fill: true)` strokes as well as fills (`IsStroked` stays true even at line width 0), unlike the real files; the parser therefore selects rectangles by `IsFilled` and bounding box only and never filters on `IsStroked`.

## Phase 1: Record the scope change in PRD and roadmap

### Overview

Make the documents say what is being built, so the roadmap item exists under Change ID `pdf-statement-import` and the PRD no longer excludes VeloBank or PDF.

### Changes Required:

#### 1. PRD amendment

**File**: `context/foundation/prd.md`

**Intent**: Add VeloBank and the PDF format with a minimal, edit-in-place change; no version bump.

**Contract**: add `FR-018` (numbered after FR-017, placed in the "Onboarding & Import" section right after FR-003): a must-have requirement that the user can import a PDF statement from VeloBank (no CSV export) through the same import, duplicate-review and categorization flow, PLN-only, with a statement whose printed running balances do not add up rejected rather than imported, plus a `> Socratic:` note in the file's existing style; update the "Banks other than mBank, Revolut, and Erste Bank Polska" non-goal (`:120`) to include VeloBank (PDF import only); add VeloBank to the bank lists in the Vision and Persona paragraphs (`:20`, `:26`). FR-002/FR-003 text stays as is.

#### 2. Roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: Add the slices and keep the index tables, streams, handoff table and parked list consistent.

**Contract**: add S-11 (`Change ID: pdf-statement-import`, outcome: import a VeloBank PDF statement through the same loop; PRD refs FR-018; prerequisite S-01; status `planning`), S-12 (`mbank-pdf-import`) and S-13 (`erste-pdf-import`), both `proposed`, prerequisite S-11, flagged optional, with their known unknowns (cross-format dedup, lossy Erste PDF) as slice blocks after S-10; add matching rows to "At a glance" (`:41-53`) and "Backlog Handoff" (`:222-234`); extend stream C (`:63`) to `S-07 → S-08 → S-11 (→ S-12, S-13 optional)`; add VeloBank to the M-1 title and intent and note that S-12/S-13 are optional and excluded from M-1's "Done when" (`:23-27`); update the parked "Banks other than …" line (`:251`). S-11's block lists the post-merge pending-row check as an open follow-up (Manual Testing Steps, step 5). `updated:` stays today's date.

#### 3. GitHub issues

**Intent**: Keep the issue tracker in step with the roadmap, one issue per slice as for S-01 to S-10 (per `context/foundation/lessons.md`).

**Contract**: after the roadmap edit is approved, ask the user, then create issues titled in the existing style ("S-11: Import a VeloBank PDF statement", "S-12: Import an mBank PDF statement", "S-13: Import an Erste Bank Polska PDF statement") with labels `slice` plus `status: planning` (S-11) or `status: proposed` (S-12, S-13). The S-11 issue body carries the post-merge pending-row check as an open follow-up.

### Success Criteria:

#### Automated Verification:

- PRD contains the new requirement: `grep -n "FR-018" context/foundation/prd.md` (from repo root)
- Roadmap has the three new slice blocks: `grep -c "^### S-1[123]:" context/foundation/roadmap.md` prints 3 (from repo root)
- Roadmap indexes the change: `grep -n "pdf-statement-import" context/foundation/roadmap.md` shows the At a glance row and the slice block (from repo root)

#### Manual Verification:

- You reviewed the PRD and roadmap diffs and approve the wording (FR-018, non-goal line, S-11 to S-13, M-1 note).
- GitHub issues for S-11 (`status: planning`), S-12 and S-13 (`status: proposed`) exist with the `slice` label and carry the matching roadmap status (per `context/foundation/lessons.md`).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Format-aware import pipeline

### Overview

Teach the import endpoint to tell CSV from PDF and to select parsers per format, without adding a PDF parser yet; CSV behaviour must not change.

### Changes Required:

#### 1. PdfPig dependency

**File**: `MyFinances/backend/MyFinances.Api.csproj`

**Intent**: Make PDF reading available to the API project (and, transitively, to the test project's fixture generator).

**Contract**: add `<PackageReference Include="PdfPig" Version="0.1.16" />` (exact id `PdfPig`; `UglyToad.PdfPig` is a different package). Restore uses the repo's `NuGet.Config` (nuget.org only).

#### 2. Format enum and parser contract

**Files**: `MyFinances/backend/Import/StatementFormat.cs` (new), `Import/IBankStatementParser.cs`, `Import/MBankCsvParser.cs`, `Import/ErsteCsvParser.cs`

**Intent**: Let each parser declare which input format it reads.

**Contract**: `enum StatementFormat { Csv, Pdf }`; `IBankStatementParser` gains `StatementFormat Format { get; }` and its comment states the rule "one parser per bank and format, all of a bank's parsers share `BankName`, `CanParse` must be cheap and never throw"; the two CSV parsers return `StatementFormat.Csv`.

#### 3. Format sniffer

**File**: `MyFinances/backend/Import/StatementFormatSniffer.cs` (new)

**Intent**: Decide the upload's format once, from its first bytes.

**Contract**: static `StatementFormat Detect(Stream stream)` — reads up to the first five bytes, returns `Pdf` when they are `%PDF-` and `Csv` otherwise (including empty or shorter streams), and restores the position of a seekable stream.

#### 4. Integrity exception

**File**: `MyFinances/backend/Import/StatementIntegrityException.cs` (new)

**Intent**: Carry a user-facing reason when a recognised statement cannot be trusted.

**Contract**: `StatementIntegrityException(string message) : Exception(message)`; messages must not contain amounts, descriptions or names; they may name the failing check, the page and a transaction date.

#### 5. Endpoint selection and error mapping

**File**: `MyFinances/backend/Import/ImportEndpoints.cs`

**Intent**: Filter parser candidates by sniffed format, give a precise message for a bank that lacks the format, and map integrity failures to 422.

**Contract** (`/import/parse`): `format = StatementFormatSniffer.Detect(stream)`; candidates = registered parsers with that `Format`; auto-detect and the manual-bank fallback both run over candidates only. When no parser results: if `bank` is non-empty and some registered parser (any format) has that `BankName`, return 400 titled `"{bank} import does not support {PDF|CSV} files."`; otherwise keep the existing 400 text. Wrap `parser.Parse(stream)` and map `StatementIntegrityException` to `Results.Problem(statusCode: 422, title: ex.Message)`. Everything after parsing is unchanged.

#### 6. Tests

**Files**: `MyFinances/backend/Tests/StatementFormatSnifferTests.cs` (new), `MyFinances/backend/Tests/ImportEndpointsTests.cs`

**Intent**: Pin format detection and the new endpoint behaviour, and prove CSV flows are unaffected.

**Contract**: sniffer tests — `%PDF-` bytes → `Pdf`; every existing CSV fixture and empty or 3-byte streams → `Csv`; seekable position restored; `MBankCsvParser.Format` and `ErsteCsvParser.Format` are `Csv`. Endpoint tests — extend `BuildUploadRequestAsync` with optional `fileName` and `contentType` parameters (defaults unchanged); `bank=mBank` with `%PDF-` bytes → 400 whose title names mBank and PDF; `%PDF-` bytes without `bank` → the existing 400 text; a stub `Format = Pdf` parser (registered through a `WithWebHostBuilder` override like `AuthApiFactory`) whose `Parse` throws `StatementIntegrityException` → 422 with that title and no rows; the existing manual-Erste-fallback test stays green.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Sniffer tests pass: `dotnet test --filter StatementFormatSnifferTests` (from `MyFinances/backend`)
- Import endpoint tests pass: `dotnet test --filter ImportEndpointsTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- With both servers running, importing an existing mBank CSV and an Erste CSV on the import page behaves as before (auto-detect, rows, duplicates on re-import).
- Selecting any PDF through the file dialog's "All files" filter shows the existing "could not recognize" message, and choosing "mBank" in the revealed picker then shows "mBank import does not support PDF files."

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Synthetic VeloBank PDF fixtures

### Overview

Build a generator that reproduces the measured VeloBank layout from invented data, and commit two PDFs plus the font they need, so parser tests run against realistic PDFs containing no personal data.

### Changes Required:

#### 1. Font

**Files**: `MyFinances/backend/Tests/Fixtures/fonts/<font>.ttf`, `MyFinances/backend/Tests/Fixtures/fonts/LICENSE-<font>.txt`

**Intent**: Provide an embeddable TrueType font with Polish diacritics under an open license.

**Contract**: an OFL-licensed font (for example Liberation Sans or Noto Sans) with its license text beside it. Prerequisite: ask the user to approve downloading it (or have them supply the file); never commit a system font such as Arial. Registered in the test `.csproj` with `CopyToOutputDirectory = PreserveNewest`.

#### 2. Generator

**File**: `MyFinances/backend/Tests/Support/VeloBankPdfBuilder.cs` (new)

**Intent**: Produce a VeloBank-style "Historia rachunku" PDF from a row model so tests and fixtures never need real data.

**Contract**: input = header text (fake holder/account), rows (`TransactionDate`, `BookingDate?` where null means pending and prints `-`, description lines, `Amount`, `Currency`, `Balance?`) and layout options (rows per page, mid-page repeated header block, tamper hooks for tests). Output reproduces the measured geometry: A4 595×842 pt, column edges 26.2 / 89.2 / 153.9 / 440.2 / 504.4 / 567.9, 28 pt filled header cells with header texts, one filled ~1.2 pt separator per row spanning the first column, date/amount/balance vertically centred in the row band, description lines from the top, header repeated per page and a boilerplate footer after each page's table. Each cell line is written as one string with real space characters (see Critical Implementation Details) so `1` and `014,84` stay two words as in the real files. Row pitch and font size are measured from the real samples (PdfPig `page.Paths`/`GetWords()` dump; research.md section 2a gives the edges); numbers beyond those above are the implementer's to measure.

#### 3. Committed fixtures

**Files**: `MyFinances/backend/Tests/Fixtures/velobank-sample-synthetic.pdf`, `velobank-sample-synthetic-multipage.pdf`

**Intent**: Real-format PDFs for parser and endpoint tests.

**Contract**: one page mirroring the 90-day export's shape — 17 rows in bank booking order (newest first), 2 pending rows at the top, the three description shapes (card, incoming transfer, outgoing transfer) with 1-5 lines, at least one balance of 1 000 or more, a repeated header block mid-page, and a continuous running-balance chain over booked PLN rows; three pages with exactly 40 rows, repeated header and footer per page, one 5-line description and balances continuous across pages. All names, account numbers and card numbers are obviously fake. Registered in the test `.csproj` with `CopyToOutputDirectory = PreserveNewest`.

#### 4. Regeneration entry

**File**: `MyFinances/backend/Tests/VeloBankPdfFixtureTests.cs` (new)

**Intent**: Keep the committed PDFs reproducible without making them part of normal test runs.

**Contract**: a test that returns immediately unless the environment variable `REGENERATE_PDF_FIXTURES=1` is set, in which case it writes the two PDFs into the test project's `Fixtures` source folder; plus checks that the committed PDFs open with PdfPig (1 and 3 pages), contain the Polish header text (e.g. "KSIĘGOWANIA"), have one thin separator per row, and that the split balance `1 0xx,xx` appears as two words.

#### 5. Cross-format negative tests

**File**: `MyFinances/backend/Tests/VeloBankPdfFixtureTests.cs`

**Intent**: Prove, now that PDF fixtures exist, that the CSV parsers reject PDFs.

**Contract**: `MBankCsvParser.CanParse` and `ErsteCsvParser.CanParse` return false without throwing for both fixtures.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Fixture tests pass: `dotnet test --filter VeloBankPdfFixtureTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Both fixture PDFs open in a PDF viewer and look like the real VeloBank statement in shape (header, grid, vertically centred date/amount/balance, repeated header block, boilerplate footer).
- The fixtures and generator contain no real name, address, account number, card number or counterparty from the original samples, and the committed font is OFL-licensed with its license file beside it.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: VeloBank PDF parser

### Overview

Implement `VeloBankPdfParser` behind the existing interface and prove it on the synthetic fixtures and, manually, on the real samples.

### Changes Required:

#### 1. Parser

**File**: `MyFinances/backend/Import/VeloBankPdfParser.cs` (new)

**Intent**: Recognise a VeloBank "Historia rachunku" PDF and turn its table into `NormalizedTransaction`s, rejecting statements whose printed balances do not add up.

**Contract**:
- `BankName = "VeloBank"`, `Format = StatementFormat.Pdf`; constructor takes an optional `maxPages` (default 200) so the limit is testable.
- `CanParse`: restores a seekable stream's position; false unless the bytes start with `%PDF-`, the PDF opens (from a byte copy) and has at most `maxPages` pages, and page 1 shows the title "Historia rachunku", "VeloBank" and the table header words; never throws. No `IsEncrypted` check: it is also true for owner-restricted files that read fine, and a password-protected file already fails in `Open` (`PdfDocumentEncryptedException`). Open, page read and word/path extraction all sit inside one catch-all, because `GetPage`/`GetWords` can throw too (`InvalidOperationException` for a missing font, `InvalidFontFormatException`), not only `Open` (`PdfDocumentFormatException`).
- `Parse`: for each page read the header-cell rectangles (column edges, header intervals) and the thin first-column separators (filled paths selected by `IsFilled` and bounding box only, never by `IsStroked`), build row bands between consecutive separators and the header bottom, assign each word to a band and column by its centre, drop words inside header intervals and below the last separator, and join each cell's lines top to bottom with single spaces. Row fields: transaction date `dd.MM.yyyy` → `Date`; booking date `-` or a date (only used to recognise pending); description cell → `Description`; amount cell `-82,30 PLN` → signed decimal (`pl-PL`) plus currency; balance cell `-` or an amount.
- Pending rows (booking date `-`, balance `-`) are returned like any row and take no part in the balance check. Non-PLN rows are not returned, add one to `SkippedErrorCount`, and take no part in the checks. Rows keep the file's newest-first order.
- Integrity (throw `StatementIntegrityException` with a PII-free message naming the failing check, the page and, when readable, the row's transaction date — no amounts, descriptions or names): balance chain across consecutive booked PLN rows (`older.Balance == newer.Balance − newer.Amount`), pending rows ignored and a skipped non-PLN row breaking the chain so nothing is compared across it; for PLN card rows whose description names PLN after "na kwotę", that amount equals the absolute amount column (a description naming another currency skips this check for that row, the row is still imported); a row in a recognised table whose date or amount cannot be read.
- Content that is not a VeloBank statement, or an unreadable/over-limit/corrupt PDF, yields an empty `ParseResult` with `SkippedErrorCount` 0 (the manual-fallback contract), never an exception. The same catch-all as in `CanParse` turns any PdfPig failure (open, page read, extraction) into "unreadable"; only `StatementIntegrityException` is allowed to escape it.

#### 2. Parser tests

**File**: `MyFinances/backend/Tests/VeloBankPdfParserTests.cs` (new)

**Intent**: Pin recognition, parsing, pending and foreign-currency handling and the integrity policy.

**Contract**: `CanParse` true for both fixtures and false for the mBank and Erste CSV fixtures, a valid non-VeloBank PDF, `%PDF-` plus garbage, and a PDF over a small `maxPages`; position restored. One-page fixture → 17 rows, 2 pending, dates are transaction dates, amounts signed, 1-5-line descriptions joined with single spaces, a balance of 1 000+ does not disturb parsing, Polish diacritics intact. Multi-page fixture → 40 rows, repeated headers and footers produce no rows, order preserved. Overlap stability — the same rows laid out on different page breaks yield identical date/amount/description keys. Variants generated in memory: a EUR row → skipped count 1, row absent, no balance comparison across it (the generator lets the printed balance jump at that row); a PLN-booked card row whose description names EUR → imported, card-amount check skipped; a tampered balance, a tampered card amount in a description, and an unreadable amount cell each throw `StatementIntegrityException` whose message names the failing check, the page and (when readable) the row's transaction date and contains no amount, description or name; a pending-only statement parses without error. `Parse` on a non-VeloBank PDF or corrupt PDF → empty result, skipped 0.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Parser tests pass: `dotnet test --filter VeloBankPdfParserTests` (from `MyFinances/backend`)
- Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- Running the parser (through a temporary local test that is not committed) on the real 90-day and one-year VeloBank PDFs returns 17 and 62 rows with no integrity exception, and the 17 shared rows have identical date, amount and description in both.
- No real personal data was added to the repository by this phase (fixtures, tests, exception messages).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Wire into the app

### Overview

Register the parser, expose VeloBank and PDF upload in the frontend, and verify the full HTTP flow with the synthetic and the real files.

### Changes Required:

#### 1. DI registration

**File**: `MyFinances/backend/DI/ImportServiceCollectionExtensions.cs`

**Intent**: Make the import endpoints and `/accounts/banks` aware of the parser.

**Contract**: add `services.AddScoped<IBankStatementParser, VeloBankPdfParser>();` after the Erste registration and update the adjacent comment to "one parser per bank and format".

#### 2. Bank-list test

**File**: `MyFinances/backend/Tests/AccountEndpointsTests.cs`

**Intent**: Keep `GET /accounts/banks` green with the new bank.

**Contract**: the assertion at `:92` becomes `["mBank", "Erste", "VeloBank", "Other"]`.

#### 3. Endpoint tests

**File**: `MyFinances/backend/Tests/ImportEndpointsTests.cs`

**Intent**: Prove detection, bank-mismatch, duplicate handling and error paths end to end for PDF, reusing `CreateAccountAsync` and the extended `BuildUploadRequestAsync` (`application/pdf`, `.pdf`).

**Contract**: one-page fixture with a "VeloBank" account → 200, `Bank == "VeloBank"`, 17 rows, `BankMismatch == false`, pending rows present with transaction dates; same file with an "mBank" account → `BankMismatch == true`; commit then re-parse → every row `IsDuplicate`; multi-page fixture → 200 with 40 rows; a tampered-balance PDF (generator variant) → 422 with the integrity title and no rows; `bank=VeloBank` with CSV content → 400 `"VeloBank import does not support CSV files."`; `bank=VeloBank` with a corrupt `%PDF-` file → 200 with zero rows; the VeloBank PDF with `bank=mBank` → auto-detection wins (`Bank == "VeloBank"`).

#### 4. Frontend

**Files**: `MyFinances/frontend/app/routes/import.tsx`, `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Let the user pick VeloBank and upload PDFs, and keep the comments truthful about status codes.

**Contract**: `SUPPORTED_BANKS` becomes `["mBank", "Erste", "VeloBank"]` (`import.tsx:54`, comment updated); file input `accept=".csv,.pdf"` and the label "Bank statement (CSV or PDF)" (`:416,:421`); the comment at `:132-134` now says 400 means "not recognised or format unsupported for the chosen bank" and that 422 shows its message without the picker (no logic change — only 400 reveals the picker); landing copy at `home.tsx:12` mentions PDF and VeloBank.

### Success Criteria:

#### Automated Verification:

- Full backend suite passes: `dotnet test` (from `MyFinances/backend`)
- Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- In settings, "VeloBank" is offered in the bank dropdown and a VeloBank account can be created.
- Uploading the real 90-day PDF auto-detects VeloBank and shows 17 rows including the two pending payments; committing and re-uploading flags every row as a duplicate.
- Uploading the real one-year PDF into the same account shows 62 rows with the 17 already-imported rows flagged as duplicates.
- A synthetic PDF with a tampered balance is rejected with the explanatory message and no bank picker; an unrecognised PDF with "mBank" chosen manually shows "mBank import does not support PDF files."
- Imported rows appear in the categorization queue and in the dashboard history with their transaction dates.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding.

---

## Testing Strategy

### Unit Tests:

- `StatementFormatSnifferTests` (Phase 2): PDF magic, every CSV fixture, empty and short streams, position restore, `Format` on both CSV parsers.
- `VeloBankPdfFixtureTests` (Phase 3): fixtures open with PdfPig, page counts, diacritics, separators, split balance words; CSV parsers reject both PDFs; opt-in regeneration.
- `VeloBankPdfParserTests` (Phase 4): recognition (positive and negative), row counts, pending handling, dates, amounts, multi-line descriptions, multi-page headers/footers, overlap stability, foreign-currency skip, the three integrity failures, empty results for non-VeloBank and corrupt PDFs.

### Integration Tests:

- `ImportEndpointsTests` (Phases 2 and 5): format filtering, 400 messages, 422 mapping, VeloBank parse/commit/re-import duplicates, bank mismatch, multi-page, manual fallbacks; existing mBank/Erste tests unchanged.
- `AccountEndpointsTests`: bank list with VeloBank.

### Manual Testing Steps:

1. Start backend (`dotnet run`) and frontend (`npm run dev`), log in, create a VeloBank account in settings.
2. Import the real 90-day PDF (`D:\repos\michLukaszewicz\Przykładowe pliki\veloBank\Historia rachunku 01-10-2026 09_56_04.pdf`); check 17 rows, dates and amounts against the PDF; commit; re-import and confirm all rows are duplicates.
3. Import the real one-year PDF (`…\veloBank\velobank long.pdf`); confirm 62 rows and that the 17 known rows are duplicates.
4. Import an mBank and an Erste CSV to confirm no regression.
5. Post-merge check (cannot be done in-phase; tracked as an open follow-up in S-11's roadmap block and GitHub issue): once the bank has booked the two payments that were pending on 2026-10-01 (a few days later), export again and import — both payments should be flagged as duplicates. If either is not (its description or amount differs once booked), reopen the pending-row decision.

## Performance Considerations

Statements are tens to low hundreds of rows over a few pages, parsed fully in memory; PdfPig runs in managed code. The 5 MB upload cap and the 200-page limit bound the work in practice, but PdfPig has no decompressed-size limit (a probe decoded a ~1000:1 Flate stream to 64 MB, peaking near 284 MB), so memory is not strictly bounded; accepted because only the allow-listed user can upload. Synthetic fixture PDFs embed the full font and are therefore larger than the real files (hundreds of KB), which is acceptable.

## Migration Notes

No schema or data changes. Existing mBank and Erste transactions and their hashes are untouched; VeloBank transactions belong to their own account, so dedup hashes cannot collide with other banks.

## References

- Related research: `context/changes/pdf-statement-import/research.md`
- Roadmap/PRD: `context/foundation/roadmap.md` (new S-11 to S-13), `context/foundation/prd.md` (new FR-018)
- Pattern plan: `context/archive/2026-10-01-erste-import/plan.md`
- Pattern parsers/tests: `MyFinances/backend/Import/ErsteCsvParser.cs`, `MyFinances/backend/Tests/ErsteCsvParserTests.cs`, `MyFinances/backend/Tests/ImportEndpointsTests.cs`
- Lessons: `context/foundation/lessons.md` (sync the GitHub issue with roadmap status)
- Real samples (outside the repo, contain personal data — never copy into the repo): `D:\repos\michLukaszewicz\Przykładowe pliki\veloBank\`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Record the scope change in PRD and roadmap

#### Automated

- [x] 1.1 PRD contains the new requirement: `grep -n "FR-018" context/foundation/prd.md` (from repo root)
- [x] 1.2 Roadmap has the three new slice blocks: `grep -c "^### S-1[123]:" context/foundation/roadmap.md` prints 3 (from repo root)
- [x] 1.3 Roadmap indexes the change: `grep -n "pdf-statement-import" context/foundation/roadmap.md` shows the At a glance row and the slice block (from repo root)

#### Manual

- [x] 1.4 You reviewed the PRD and roadmap diffs and approve the wording (FR-018, non-goal line, S-11 to S-13, M-1 note).
- [x] 1.5 GitHub issues for S-11 (`status: planning`), S-12 and S-13 (`status: proposed`) exist with the `slice` label and carry the matching roadmap status (per `context/foundation/lessons.md`).

### Phase 2: Format-aware import pipeline

#### Automated

- [ ] 2.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 2.2 Sniffer tests pass: `dotnet test --filter StatementFormatSnifferTests` (from `MyFinances/backend`)
- [ ] 2.3 Import endpoint tests pass: `dotnet test --filter ImportEndpointsTests` (from `MyFinances/backend`)
- [ ] 2.4 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 2.5 With both servers running, importing an existing mBank CSV and an Erste CSV on the import page behaves as before (auto-detect, rows, duplicates on re-import).
- [ ] 2.6 Selecting any PDF through the file dialog's "All files" filter shows the existing "could not recognize" message, and choosing "mBank" in the revealed picker then shows "mBank import does not support PDF files."

### Phase 3: Synthetic VeloBank PDF fixtures

#### Automated

- [ ] 3.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 3.2 Fixture tests pass: `dotnet test --filter VeloBankPdfFixtureTests` (from `MyFinances/backend`)
- [ ] 3.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 3.4 Both fixture PDFs open in a PDF viewer and look like the real VeloBank statement in shape (header, grid, vertically centred date/amount/balance, repeated header block, boilerplate footer).
- [ ] 3.5 The fixtures and generator contain no real name, address, account number, card number or counterparty from the original samples, and the committed font is OFL-licensed with its license file beside it.

### Phase 4: VeloBank PDF parser

#### Automated

- [ ] 4.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 4.2 Parser tests pass: `dotnet test --filter VeloBankPdfParserTests` (from `MyFinances/backend`)
- [ ] 4.3 Full suite passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 4.4 Running the parser (through a temporary local test that is not committed) on the real 90-day and one-year VeloBank PDFs returns 17 and 62 rows with no integrity exception, and the 17 shared rows have identical date, amount and description in both.
- [ ] 4.5 No real personal data was added to the repository by this phase (fixtures, tests, exception messages).

### Phase 5: Wire into the app

#### Automated

- [ ] 5.1 Full backend suite passes: `dotnet test` (from `MyFinances/backend`)
- [ ] 5.2 Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- [ ] 5.3 Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual

- [ ] 5.4 In settings, "VeloBank" is offered in the bank dropdown and a VeloBank account can be created.
- [ ] 5.5 Uploading the real 90-day PDF auto-detects VeloBank and shows 17 rows including the two pending payments; committing and re-uploading flags every row as a duplicate.
- [ ] 5.6 Uploading the real one-year PDF into the same account shows 62 rows with the 17 already-imported rows flagged as duplicates.
- [ ] 5.7 A synthetic PDF with a tampered balance is rejected with the explanatory message and no bank picker; an unrecognised PDF with "mBank" chosen manually shows "mBank import does not support PDF files."
- [ ] 5.8 Imported rows appear in the categorization queue and in the dashboard history with their transaction dates.
