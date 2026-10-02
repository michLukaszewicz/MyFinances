---
date: 2026-10-01T09:54:05Z
researcher: Claude Sonnet 5.5
git_commit: 5ac5b3bf9f946d7fc6441f26f919fc8c3945bacc
branch: main
repository: MyFinances (Kurs 10xDev)
topic: "PDF bank-statement import for VeloBank, mBank and Erste"
tags: [research, codebase, backend, import, pdf, pdfpig, velobank, mbank, erste]
status: complete
last_updated: 2026-10-01
last_updated_by: Claude Sonnet 5.5
last_updated_note: "Second pass: real PDF samples for all three banks analyzed and prototyped (VeloBank short + long multi-page, mBank, Erste), cross-checked against the repo's CSV parsers and the user's CSV files. Supersedes the first pass's unverified mBank/Erste inferences."
---

# Research: PDF statement import (VeloBank, mBank, Erste)

## Research Question

Can MyFinances import bank statements from PDF, which approach is best (read the PDF directly, convert to another format, or use an AI model), and what has to change in the existing CSV import to support PDF for all three banks: VeloBank (new, PDF only), mBank and Erste (CSV already supported)?

## Summary

- **Reading PDF directly with PdfPig works for all three banks, on real samples.** Throwaway prototypes (outside the repo) recovered every row with independent integrity checks passing:

  | Bank / sample | Rows | Integrity checks |
  |---|---|---|
  | VeloBank, 1 page | 17 | balance chain 14/14, card amount in description 13/13 |
  | VeloBank, 4 pages | 62 | balance chain 59/59, card amount in description 49/49 |
  | mBank, 2 pages | 32 | balance chain 32/32 from the opening balance, last balance = closing balance, credit/debit counts and sums equal the statement's turnover summary (7 / 25 / 32) |
  | Erste, 3 pages | 55 | 0 unparsed rows; balance chain closes as a single chain (exactly one oldest row, no ambiguity) but only order-independently |

- **Each bank needs its own parsing strategy.** The three PDFs come from three different generators and three different layouts (see below). Only a thin common part exists (words, baselines, join-per-cell). A generic table reader is not justified.
- **No intermediate format and no LLM at runtime.** Converting PDF to CSV/JSON adds a lossy step; an LLM call adds cost, non-determinism and personal data leaving the app. The target is the existing `NormalizedTransaction`.
- **PDF is not equally valuable per bank:**
  - **VeloBank** — only route in (no CSV export); two exports on the same day produced identical descriptions for the 17 overlapping rows, so dedup across exports is safe.
  - **mBank** — richest PDF (explicit period, opening/closing balance, turnover summary, ISO dates), but its descriptions can never hash-match the CSV ones (the CSV pads descriptions with runs of spaces, the PDF does not).
  - **Erste** — lossy compared to the CSV: no booking date, descriptions reduced to the counterparty name, dates 0-2 days different from the CSV, list order not chronological. Lowest value; CSV remains the better route.
- **Cross-format dedup (same transactions imported once as CSV and once as PDF) does not work for mBank or Erste with the current hash** — now shown with real data, not inferred. The practical policy is one format per account unless a normalization step is added.
- **The existing import code assumes one parser per bank name.** The manual-bank fallback ([ImportEndpoints.cs:47](MyFinances/backend/Import/ImportEndpoints.cs#L47)) breaks once a bank has a CSV and a PDF parser. Decision (user, 2026-10-01): one parser per bank and format, with the format sniffed up front (see cross-cutting point 1).
- **This is a scope change.** The PRD excludes banks other than mBank, Revolut and Erste ([prd.md:120](context/foundation/prd.md)), and FR-002/FR-003 are CSV-only.

## Detailed Findings

### 1. Current import architecture (what PDF has to plug into)

- **Parser contract**: [IBankStatementParser.cs:11-21](MyFinances/backend/Import/IBankStatementParser.cs#L11-L21) — `BankName`, `CanParse(Stream)` (must leave a seekable stream rewound), `Parse(Stream)` returning `ParseResult(Transactions, SkippedErrorCount)`. Takes a `Stream`, so PDF fits without an interface change.
- **Bank-agnostic output**: [NormalizedTransaction.cs:5](MyFinances/backend/Import/NormalizedTransaction.cs#L5) — `(DateOnly Date, string Description, decimal Amount)`. No balance, booking date or currency.
- **What the CSV parsers put into those fields** (matters for cross-format parity):
  - mBank: `Date` = *Data księgowania* (booking date, field 0), `Description` = the *Tytuł* column only (field 3) ([MBankCsvParser.cs:152-167](MyFinances/backend/Import/MBankCsvParser.cs#L152-L167)).
  - Erste: `Date` = field 1 (`dd-MM-yyyy`), `Description` = field 2, non-PLN statements are counted as skipped ([ErsteCsvParser.cs:118-143](MyFinances/backend/Import/ErsteCsvParser.cs#L118-L143)).
- **Dedup hash**: `SHA256(userId | yyyy-MM-dd | amount F2 | description | accountId)`, description verbatim, no normalization ([DedupHash.cs:11-24](MyFinances/backend/Import/DedupHash.cs#L11-L24)). Collisions are intentionally routed to the duplicate-review UI, not resolved in the hash (archived mBank research, option C).
- **Endpoint flow**: [ImportEndpoints.cs:18-88](MyFinances/backend/Import/ImportEndpoints.cs#L18-L88) — copies the upload to a `MemoryStream`, picks `parsers.FirstOrDefault(p => p.CanParse(stream))`, falls back to `parsers.FirstOrDefault(p => p.BankName == bank)` when the caller chose a bank manually, then `Parse`. `bankMismatch` compares `parser.BankName` with `account.BankName` ([ImportEndpoints.cs:85](MyFinances/backend/Import/ImportEndpoints.cs#L85)).
- **Registration**: [ImportServiceCollectionExtensions.cs:10-15](MyFinances/backend/DI/ImportServiceCollectionExtensions.cs#L10-L15).
- **Bank list for accounts** is derived from the registered parsers: [AccountEndpoints.cs:17-26](MyFinances/backend/Transactions/AccountEndpoints.cs#L17-L26) (`Select(p => p.BankName).Distinct()` plus "Other"). `Distinct()` already tolerates several parsers sharing a name. [AccountEndpointsTests.cs:92](MyFinances/backend/Tests/AccountEndpointsTests.cs#L92) asserts the exact list `["mBank", "Erste", "Other"]`, so adding VeloBank changes that test.
- **Upload cap**: 5 MB for both Kestrel and multipart ([Program.cs:20-24](MyFinances/backend/Program.cs#L20-L24)). The samples are 38-101 KB.
- **Frontend touchpoints**: `SUPPORTED_BANKS = ["mBank", "Erste"]` hardcoded ([import.tsx:54](MyFinances/frontend/app/routes/import.tsx#L54)); file input `accept=".csv"` ([import.tsx:421](MyFinances/frontend/app/routes/import.tsx#L421)) with label "Bank statement CSV" ([import.tsx:416](MyFinances/frontend/app/routes/import.tsx#L416)); landing copy "Upload bank CSV exports from mBank, Revolut, and Erste." ([home.tsx:12](MyFinances/frontend/app/routes/home.tsx#L12)).
- **Test conventions**: xUnit, parser unit tests per bank with redacted fixtures under `MyFinances/backend/Tests/Fixtures/` (`mbank-sample-redacted*.csv`, `erste-sample-redacted*.csv`).
- **NuGet**: [MyFinances/backend/NuGet.Config](MyFinances/backend/NuGet.Config) restricts restore to nuget.org. A private Azure DevOps feed in the machine-level NuGet config returned 401 for scratch projects outside the repo; it does not affect the backend project.

### 2. The three PDF samples (real, user-supplied 2026-10-01, stored outside the repo)

All three **contain personal data** (account numbers, holder name and address, counterparties' names/accounts, masked card numbers) and must not be committed; see Fixtures below. All three have a real text layer (no OCR needed) and come from deterministic generators, so the layout is stable per bank.

| | VeloBank | mBank | Erste |
|---|---|---|---|
| Producer | wkhtmltopdf 0.12.6 (Qt 4.8.7) | Ibex PDF Creator 4.3 | Chromium / Skia |
| Pages in samples | 1 and 4 (A4) | 2 (595x828) | 3 (A4) |
| Period in file | label only ("Ostatnie 90 dni" / "Ostatni rok") | explicit "od 2026-09-10 do 2026-10-01" | none (only "Dokument z dnia") |
| Table drawn as | filled thin rectangles (row separators, header cells) | stroked grid lines | filled thin rectangles (row separators) |
| Row anchor | separator bands | line starting with a date | separator bands (30 pt each) |
| Date format | `dd.MM.yyyy` | `yyyy-MM-dd` | `dd mmm yyyy`, Polish abbreviations (`wrz`, `paź`, `sie`) |
| Dates per row | transaction date + booking date (`-` when pending) | booking date + operation date | operation date only (booking date label printed with no value) |
| Amount cell | `-82,30 PLN` | `-54,99` (no currency) | `-0,10 PLN` |
| Running balance per row | yes (`-` when pending) | yes, plus opening and closing balance | yes |
| Order of rows | bank booking order, newest first | chronological, oldest first | newest first by booking date; same-day order arbitrary |
| Header per page | repeated on every page | repeated on every page | repeated on every page |

Common to all three: thousands are separated by a space and PdfPig returns the parts as separate words (`1` + `014,84`), so amounts and balances must be assembled per cell, never per word. Descriptions wrap over several lines.

#### 2a. VeloBank ("Historia rachunku")

- **Row geometry.** Five columns (transaction date | booking date | description | amount | balance), cell edges at x ≈ 26.2 / 89.2 / 153.9 / 440.2 / 504.4 / 567.9 pt. Header cells are 28 pt tall filled rectangles; every row ends with a ~1.2 pt filled separator spanning the first column. Nothing is stroked.
- **Do not group by text baseline.** Date, amount and balance are vertically centered in the cell while the description starts at the top (1-5 lines). `pdftotext -layout` interleaves amounts with other rows' descriptions for this reason. Rows come from separator bands, columns from x edges.
- **Header repeats mid-page** (the first sample has a second header block on the same page, edges shifted by 0.6 pt) and at the top of every page; a legal boilerplate footer follows each page's table. Both must be skipped; edges must be matched with tolerance.
- **Pending rows**: booking date `-` and balance `-` (2 of 17 / 2 of 62 rows). They cannot take part in the balance-chain check.
- **Description shapes**: card operations ("Operacja kartą <masked card> na kwotę <amount> <ccy> w <merchant>..."), incoming transfers ("Przelew z rachunku: <iban>, Nadawca: <name>, Tytuł: <title>"), outgoing transfers ("Przelew na rachunek: <iban>, Odbiorca: <name>, Tytuł: <title>"). A card row repeats its amount and currency inside the description.
- **Multi-page works with the same algorithm.** The 4-page file has four header blocks and no row split across a page break (the balance chain closes over all 62 rows).
- **Cross-export stability (real data).** All 17 rows of the 90-day export appear in the one-year export with identical date, amount and description, so the dedup hash is identical across VeloBank exports.
- **Not covered**: foreign-currency card operations, refunds, BLIK, ATM, fees, standing orders (none occur in either VeloBank file; both are PLN only), and whether a pending row keeps an identical description once booked.

#### 2b. mBank ("Elektroniczne zestawienie operacji")

- **Rows are line-anchored, not band-anchored.** Each transaction starts on a line whose first token (x ≈ 48) is an ISO date; amount and balance sit on that same line, and the description continues on the following lines (x ≈ 152). Page headers, summary labels and footers are recognized by non-date text in the first column and end the current row.
- **Columns** (edges from the stroked horizontal lines): x ≈ 40 | 96.7 | 148.2 | 452.2 | 503.7 | 555.2. Amount and balance cells have no currency; the account currency comes from the statement header ("Waluta PLN").
- **Three independent integrity checks exist in the document itself**: *Saldo początkowe* and *Saldo końcowe*, a running balance per row, and a turnover summary (*Uznania* 7 / 4 274,36, *Obciążenia* 25 / 2 484,84, *Łącznie* 32 / 1 789,52). The prototype matched all of them exactly. This is the strongest verification of the three banks.
- **Description structure**: line 1 = *Opis operacji* (e.g. `ZAKUP PRZY UŻYCIU KARTY`, `POS ZWROT TOWARU`, `BLIK P2P-WYCHODZĄCY`, `PRZELEW WEWNĘTRZNY PRZYCHODZĄCY`); following lines = merchant and city plus `DATA TRANSAKCJI: yyyy-MM-dd` for card operations, or counterparty name and address, a 26-digit account number and the title for transfers (not labelled; the CSV keeps these in separate columns).
- **Booking date equals operation date on all 32 rows.** The real card-purchase date exists only inside the text (`DATA TRANSAKCJI`), as in the CSV.
- **Compare with the existing CSV parser (real files).** CSV `Date` is the booking date, which matches the PDF's first date column. CSV `Description` is the *Tytuł* column; for card purchases that is the merchant/city text, and the PDF's continuation lines have the same content. **But the CSV pads it with runs of spaces**: 59 of 75 sample rows contain runs of 2+ spaces, card descriptions are all exactly 97 characters long with space runs up to 44 characters before `DATA TRANSAKCJI`; the PDF text has single spaces (0 of 32 rows with a double space). A PDF-derived description can therefore not equal the CSV one without an explicit normalization.
- **No real CSV/PDF pair exists for mBank**: the supplied CSVs cover 2026-06-01..2026-08-31, the PDF covers 2026-09-10..2026-10-01 (zero overlap). Parity of the transfer descriptions (where the PDF lumps name, account and title) is unverified.

#### 2c. Erste ("Lista transakcji")

- **Geometry.** Four columns (date block | operation | amount | balance), edges x ≈ 6.7 / 123 / 402.7 / 495.7 / 588.7; rows are 30 pt bands delimited by filled thin separators (Chromium's table borders). The date cell holds the operation date and, below it, the label "Data księgowania" with **no date value**.
- **Descriptions are short.** The *Operacja* cell contains only the merchant or counterparty name (plus a second line for card refunds). Average 20 characters in the PDF versus 65 in the CSV for the same transactions; only 25 distinct descriptions across 55 rows (`Allegro` x13). The CSV carries the operation type, title and card text that the PDF drops.
- **Same transactions, different dates.** Matching by amount, all 29 rows of the September CSV were found in the PDF; the CSV date is 0 days after the PDF date for 7 rows, 1 day for 16 and 2 days for 6, consistent with the CSV date being the booking date while the PDF shows the operation date (inference; the CSV column semantics were not checked against Erste documentation). Either way, neither date nor description match across formats.
- **Collisions get worse.** With the short descriptions, the same `(date, amount, description)` key occurs on 3 groups of rows in the PDF versus 1 in the CSV, which means more duplicate-review noise.
- **Balance chain is order-independent only.** Within a day, and between rows of different dates, the printed order does not follow the balance (12 of 54 adjacent pairs break), because the list is ordered by the unprinted booking date. Every row except the oldest has exactly one predecessor row with `balance - amount`, so the check must be "each row links to a predecessor" rather than "row i+1 follows row i".
- **Currency** is printed per amount cell (`PLN`); non-PLN rows can be counted as skipped, as the Erste CSV parser does. No account currency or period in the header.

#### 2d. Algorithms confirmed by the prototypes (prose only; prototypes live in the session scratchpad, not in the repo)

- **VeloBank / Erste (band-anchored):** read header-cell rectangles for column edges and header intervals, take thin first-column rectangles as row separators, build bands between consecutive separators (and the header bottom), assign each word to a band by its vertical centre and to a column by its horizontal centre, drop words inside header intervals and below the last separator, join text per cell (lines in baseline order), then parse date/amount/balance with regexes and `pl-PL` decimals.
- **mBank (line-anchored):** take column edges from the stroked horizontal lines, group words into lines by baseline, start a row at every line whose first column is an ISO date, append later lines' description-column text until the next date line or a non-date first-column token, and read *Saldo początkowe/końcowe* and the turnover summary from their own lines.

### 3. Options evaluated

| Option | Verdict | Notes |
|---|---|---|
| **PdfPig + per-bank parser** | **Recommended** | Free, offline, deterministic. [PdfPig](https://www.nuget.org/packages/PdfPig) 0.1.16 (2026-08-22), Apache-2.0, targets net6+ and netstandard2.0/2.1, no dependencies on net6+. Package id is `PdfPig`; `UglyToad.PdfPig` on nuget.org resolves to a different package (only a custom prerelease build was found), so use the exact id. Opens from a `Stream`. Exposes words with bounding boxes and `page.Paths` (the table grid). Confirmed working on all three real samples. |
| LLM extraction (Claude API, one call) | Fallback only | Native PDF input (`document` block, [PDF support](https://platform.claude.com/docs/en/build-with-claude/pdf-support): 32 MB, 600 pages with a 1M-context model else 100, no encrypted PDFs); official C# SDK has `DocumentBlockParam` with `Base64PdfSource`. Rejected as the primary path: (a) non-deterministic output breaks the verbatim-description dedup hash; (b) a dropped row is silent unless reconciled against balances; (c) holder name, address and IBANs leave the app (API data reportedly not used for training and deleted within 30 days per [a secondary source](https://www.getvoibe.com/resources/claude-api-data-retention/) — verify against official terms); (d) recurring cost, my estimate about 5-10 US cents for a 5-page statement on a Sonnet-class model. The prototypes show the deterministic path needs no model. |
| Convert PDF to CSV/JSON first (Tabula/Camelot) | Rejected | Extra lossy step before parsing anyway. A [comparison article](https://dev.to/urios/parsing-bank-statement-pdfs-5-tools-compared-for-developers-2026-4b70) reports about 81% accuracy for Tabula/Camelot on bank statements (low-grade source, directional only). [tabula-sharp](https://github.com/BobLd/tabula-sharp) is the .NET port (built on PdfPig). Neither does OCR. The mBank layout (line-anchored rows, no row borders) and the vertical centering in VeloBank/Erste are exactly where table extractors guess wrong. |
| Azure Document Intelligence | Rejected | The [prebuilt bank-statement model](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/prebuilt/bank-statement?view=doc-intel-4.0.0) supports US statements in en-US only. |
| Paid converters (Nanonets, DocParser, PDFTables) | Rejected | Cost and personal-data exposure out of proportion for a single-user app. |

Revolut note (moot for this change): per [scancompte](https://www.scancompte.com/banks/export-revolut-statement) and [bankxlsx](https://bankxlsx.com/blog/can-i-export-revolut-transactions-to-csv-or-excel), personal accounts export Excel and PDF in the mobile app and CSV may be offered in the web app depending on account type; one file per currency. Sources partly conflict. Revolut stays as S-07, unblocked only by a real sample.

### 4. Adding PDF for all three banks (VeloBank, mBank, Erste)

| Bank | CSV today | PDF | Parser strategy | Value and caveats |
|---|---|---|---|---|
| **VeloBank** | none (bank offers no CSV export) | real samples, verified | band-anchored; transaction date as `Date` | **High.** Only route in. Dedup-safe across exports. Pending rows need a policy. |
| **mBank** | `MBankCsvParser` | real sample, verified | line-anchored; booking date as `Date` | **Medium.** Best integrity checks (opening/closing balance, turnover summary). Description composition cannot match the CSV (padding), so cross-format dedup needs normalization. No overlapping CSV/PDF pair to confirm transfer parity. |
| **Erste** | `ErsteCsvParser` (four delimiter variants) | real sample, verified | band-anchored; operation date as `Date` | **Low.** Lossy versus the CSV (no booking date, counterparty-only description). Dates differ by 0-2 days from CSV, so no cross-format dedup. More hash collisions. Prefer CSV; PDF is a fallback. |

**Cross-cutting design points for planning**

1. **Several parsers can share one `BankName`.** `/accounts/banks` already de-duplicates (`Distinct()`), and `bankMismatch` compares by name, so CSV and PDF parsers of one bank should report the same `BankName` ("mBank", "Erste"). But the manual fallback `FirstOrDefault(p => p.BankName == bank)` ([ImportEndpoints.cs:47](MyFinances/backend/Import/ImportEndpoints.cs#L47)) would return whichever parser is registered first, i.e. the CSV one for a PDF upload. **Decision (user, 2026-10-01): every bank gets a separate parser per format** (`MBankCsvParser` + `MBankPdfParser`, `ErsteCsvParser` + `ErstePdfParser`, `VeloBankPdfParser`, later e.g. an Excel parser), all reporting the same `BankName`. Mechanics, to be confirmed in the plan: add a format discriminator to `IBankStatementParser`; sniff the upload's format once from its first bytes (`%PDF-` means PDF, anything else is treated as CSV, so today's behaviour and the existing manual-Erste-fallback test at [ImportEndpointsTests.cs:436-440](MyFinances/backend/Tests/ImportEndpointsTests.cs#L436-L440) stay unchanged); run `CanParse` only over parsers of that format; in the manual fallback pick `p.Format == sniffed && p.BankName == bank` and return a specific 400 ("<bank> has no PDF import") when that combination does not exist (e.g. VeloBank with a CSV). The only consumers of the parser collection are `ImportEndpoints` (auto-detect and the fallback) and `AccountEndpoints` (`Distinct()`), so nothing else changes; `bankMismatch` and the frontend bank picker stay as they are. The rejected alternative was one composite parser per bank that dispatches CSV vs PDF internally.
2. **`CanParse` on the wrong format.** CSV parsers already decode lossily and must not throw ([ErsteCsvParser.cs:23-32](MyFinances/backend/Import/ErsteCsvParser.cs#L23-L32)). Reading the code: `ErsteCsvParser.DetectDelimiter` looks only at the first non-empty line, which for a PDF is `%PDF-1.x` and fails `LooksLikeSummary`; `MBankCsvParser.CanParse` looks for a line starting with the `#Data księgowania` header prefix. Both should reject PDF bytes, but this is a reading of the code, not a test — add explicit tests that every CSV parser rejects a PDF and every PDF parser rejects every CSV fixture. PDF parsers should check the `%PDF-` magic before opening the document so a CSV upload never reaches PdfPig. Recognition anchors available per bank: VeloBank — the "Historia rachunku" title plus the "VeloBank S.A." footer and "DATA TRANSAKCJI / DATA KSIĘGOWANIA" header; mBank — "Elektroniczne zestawienie operacji" plus the "mBank S.A." letterhead; Erste — "Lista transakcji" plus the "Konto:" header (weakest anchor, no bank name printed in the extracted text — verify before relying on it, the producer string is Chromium so it cannot be used).
3. **`NormalizedTransaction.Date` must mean the same thing as in the bank's CSV, or cross-format dedup is impossible, and within one format it must be stable over a transaction's life.** Findings: mBank CSV = booking date (PDF has it: use it). Erste CSV = most likely the booking date (it lags the PDF's operation date by 0-2 days; the PDF has no booking date, so it cannot match). VeloBank has no CSV; use the **transaction date**, because pending rows have no booking date and the hash must not change when the row is booked.
4. **Cross-format dedup is broken for mBank and Erste by construction (now evidenced).** mBank: CSV descriptions are space-padded (97 chars for card rows), PDF ones are not. Erste: PDF descriptions are a short counterparty name versus the CSV's long text, and dates differ by 0-2 days. Importing the same period once per format into one account would create undetected duplicates. Options: (a) policy and UI copy "one format per account" (cheapest; recommended for the first iteration); (b) normalize descriptions (collapse whitespace) for hashing for all parsers — this changes the hash of every already-stored mBank transaction and needs a rehash migration, and still does not help Erste; (c) match on `(date, amount)` plus fuzzy description — new concept, out of scope.
5. **Integrity checks are per bank, not generic.** VeloBank: balance chain (booked rows only) plus card amount in description. mBank: balance chain from the opening balance, closing balance, and the turnover summary (counts and sums). Erste: order-independent balance linkage. What to do on failure (reject the file with a 400, import with a warning, or count rows into `SkippedErrorCount`) is a plan decision; recommendation: fail loudly on a broken chain rather than import wrong amounts. All three PDF generators are deterministic, so a failure means the bank changed its layout.
6. **Pending rows (VeloBank).** Pending rows (booking date `-`, no balance) exist in both VeloBank exports. Because the hash uses the transaction date, a pending row imported now and the same row later when booked collide in the hash and go to duplicate review — desirable, provided the description is identical in both states (unverified). Open question: import pending rows or skip them.
7. **Shared helper only after a second PDF bank is implemented, and only a thin one.** The prototypes show two different anchoring strategies (separator bands vs date-anchored lines) and two ways to get column edges (header-cell rectangles vs stroked lines). What is common: words with centres, grouping by baseline, per-cell joining, `pl-PL` money parsing, header/footer skipping.
8. **Security and robustness.** PdfPig is managed code (no native parsing surface), the 5 MB cap already exists, encrypted/password PDFs should surface as "cannot read file" rather than 500. A page-count or time limit on parsing is worth a decision; a crafted PDF can still consume CPU and memory.
9. **Fixtures.** None of the real PDFs can be committed. A black rectangle over text leaves the text in the layer, so redaction must actually remove it, or a synthetic PDF with the same grid and invented data must be generated (PdfPig can also write PDFs through `PdfDocumentBuilder`; Polish diacritics need an embedded TrueType font there — verify during planning). Follows the existing "redacted real-format fixture" convention; three bank layouts means three fixture sets.
10. **Frontend and copy.** Add "VeloBank" to `SUPPORTED_BANKS`, widen the file input to `.csv,.pdf` and relabel it, update the landing copy; settings' bank dropdown comes from the backend list automatically. `AccountEndpointsTests` expects the exact bank list and must be updated.
11. **Currency.** PRD is PLN-only with a skipped-count for other currencies. VeloBank and Erste print the currency per amount cell; mBank prints it once in the header ("Waluta"). Neither VeloBank file contains a foreign-currency row, so how a foreign card operation looks (description and amount column) is unverified.
12. **Scope and tracking.** PRD FR-002/FR-003 are CSV-only and exclude other banks ([prd.md:120](context/foundation/prd.md)); the roadmap has S-07 (Revolut) as `proposed` ([roadmap.md:50](context/foundation/roadmap.md)). PDF and VeloBank need PRD/roadmap entries (new slice or slices) before implementation. Lesson on record: sync the GitHub issue with `roadmap.md` after every commit that changes a roadmap status ([lessons.md](context/foundation/lessons.md)).

## Code References

- `MyFinances/backend/Import/IBankStatementParser.cs:11-21` - parser contract (`CanParse`/`Parse`, `Stream` in, `ParseResult` out)
- `MyFinances/backend/Import/NormalizedTransaction.cs:5` - bank-agnostic output record
- `MyFinances/backend/Import/DedupHash.cs:11-24` - verbatim-description hash
- `MyFinances/backend/Import/ImportEndpoints.cs:41-53,85` - parser selection, manual-bank fallback, `bankMismatch`
- `MyFinances/backend/Import/MBankCsvParser.cs:104-126,152-167` - header-prefix recognition; booking date and Tytuł column as Date/Description
- `MyFinances/backend/Import/ErsteCsvParser.cs:23-32,46-80,118-143` - lenient decoding, first-line recognition, PLN-only and field mapping
- `MyFinances/backend/DI/ImportServiceCollectionExtensions.cs:10-15` - parser registration
- `MyFinances/backend/Transactions/AccountEndpoints.cs:17-26` - bank list from parsers
- `MyFinances/backend/Program.cs:20-24` - 5 MB upload cap
- `MyFinances/backend/Tests/AccountEndpointsTests.cs:92` - asserts exact bank list
- `MyFinances/frontend/app/routes/import.tsx:54,416,421` - bank list, file label, `accept=".csv"`
- `MyFinances/frontend/app/routes/home.tsx:12` - landing copy

## Architecture Insights

- The `IBankStatementParser` design isolates every format quirk behind `Stream` in, `NormalizedTransaction` out. A PDF parser is the same kind of thing as a CSV parser with different internals (PdfPig instead of CsvHelper), as the archived mBank research already anticipated.
- The implicit assumption "one parser per bank name" is the first place the architecture leaks; the second is the meaning of `NormalizedTransaction.Date` and `Description`, which is defined by each parser and therefore by each format, not by the bank.
- The dedup design (hash on verbatim description, collisions to human review) favors deterministic parsers, is hostile to generative extraction, and makes mixing formats within one account unsafe.

## Historical Context (from prior changes)

- `context/archive/2026-09-25-mbank-import-with-dedup/research.md` — recorded that a future PDF parser only needs to implement the same interface and that format quirks must stay inside the parser; also the hash-collision decision (option C).
- `context/archive/2026-10-01-erste-import/plan.md` — second parser, redacted-fixture convention, `CanParse` negative tests across banks.
- `context/foundation/roadmap.md` — S-07 Revolut `proposed`; PRD FR-003 sequencing gate waived on 2026-10-01.

## Related Research

None beyond the archived mBank research above. External sources: PdfPig docs via Context7 (`/uglytoad/pdfpig`: word extraction and layout analysis), the Claude PDF-support docs, and the NuGet/Azure/tabula-sharp/Revolut pages linked in section 3.

## Open Questions

1. **Policy for formats within one account** (cross-cutting point 4): one format per account (recommended first iteration), or add description normalization with a rehash migration? Decide in `/10x-plan`.
2. ~~Format discrimination (point 1)~~ — **Resolved (user, 2026-10-01)**: a separate parser per bank and format, a format discriminator on the interface and one up-front format sniff; see cross-cutting point 1.
3. **Failure policy** for a failed integrity check: reject the file, warn, or count as skipped errors?
4. **VeloBank pending rows**: import them, or only booked rows? Does a pending row's description stay identical once booked? (Needs a re-export of the same period a few days later.)
5. **Foreign-currency and other operation types** are unverified for all three banks: foreign-currency card operation, ATM, fee, standing order (VeloBank has none of these; mBank and Erste samples contain BLIK and card refunds but no foreign currency). A sample with a foreign-currency card payment for each bank would settle the PLN-only filter.
6. **mBank**: a CSV for the same period as the PDF (2026-09-10..2026-10-01) would settle whether transfer descriptions can match at all (the PDF lumps counterparty, account number and title; the CSV keeps them in columns, and only *Tytuł* feeds the hash).
7. **Erste value judgement**: given the lossy PDF, is Erste PDF worth building at all, or should the plan list it as an explicit, deprioritized item? (Recommendation: build last, keep optional.)
8. **Erste recognition anchor**: the extracted text has no bank name; confirm a sufficiently unambiguous `CanParse` signal ("Lista transakcji" header plus "Konto:" line plus column header text) or require manual bank selection.
9. **Fixture strategy**: genuine redaction of a real PDF vs. a synthetic PDF generated with PdfPig.
10. **Roadmap/PRD**: one slice for PDF infrastructure plus VeloBank, then one slice per additional bank; amend PRD FR-002/FR-003 and the exclusion at prd.md:120.
11. **Revolut (S-07)** remains `proposed`; unblocked only by a real sample.

## Proposed scope points for `/10x-plan`: PDF for all three banks

Ordered by value and dependency; the order (VeloBank first, mBank and Erste optionally afterwards) was confirmed by the user on 2026-10-01. All three parsers can be built from the samples already supplied (no sample blockers remain).

1. **Roadmap/PRD amendment**: add VeloBank and the PDF format; decide slice boundaries.
2. **PDF groundwork**: add the `PdfPig` package; `%PDF-` sniffing; add the format discriminator and up-front format sniffing in `ImportEndpoints` (decided, cross-cutting point 1); CSV-rejects-PDF and PDF-rejects-CSV tests; frontend file input, label and copy; decide the date semantics and the integrity-failure policy (points 3 and 5).
3. **VeloBank PDF import** (highest value, the only route in for this bank): `VeloBankPdfParser` with the band algorithm, balance-chain and card-amount checks, bank name "VeloBank", transaction date as `Date`, pending-row policy, redacted/synthetic fixtures for the one-page and multi-page layouts, unit tests, DI registration, frontend `SUPPORTED_BANKS` entry, update `AccountEndpointsTests`, manual end-to-end import of the real samples.
4. **mBank PDF import**: `MBankPdfParser` (same bank name as the CSV parser), line-anchored parsing, booking date as `Date`, checks on opening/closing balance and turnover summary, fixture and tests. Document "one format per account" (or implement normalization per Open Question 1).
5. **Erste PDF import** (lowest value, optional): `ErstePdfParser` (same bank name as the CSV parser), band-anchored parsing with Polish month abbreviations, order-independent balance linkage, fixture and tests. Document that the PDF is lossy versus the CSV.
6. **Cross-format policy implementation or documentation** (Open Question 1), including user-facing copy on the import page.
7. **Close-out**: roadmap status update and GitHub issue sync (per lessons.md), CLAUDE.md project-status line, archive.
