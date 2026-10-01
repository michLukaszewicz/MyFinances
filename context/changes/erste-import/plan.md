# Erste Bank Polska CSV Import Implementation Plan

## Overview

Add a second `IBankStatementParser` (`ErsteCsvParser`) so a user can upload an Erste Bank Polska "Historia" CSV export through the existing parse → review duplicates → commit flow. Everything downstream of the parser (dedup hash, `/import/parse`, `/import/commit`, transfer detection, bank list for accounts) already consumes parsers generically; the work is the parser itself, a redacted fixture with tests, DI registration, and one frontend list entry.

## Current State Analysis

- `IBankStatementParser` has `BankName`, `CanParse(Stream)` and `Parse(Stream)` returning `ParseResult(IReadOnlyList<NormalizedTransaction>, SkippedErrorCount)` — `MyFinances/backend/Import/IBankStatementParser.cs:11`. `NormalizedTransaction` is `(DateOnly Date, string Description, decimal Amount)`.
- `/import/parse` tries every registered parser's `CanParse` in turn, falls back to a manually chosen `bank` name, and sets `BankMismatch` by comparing `parser.BankName` to the chosen account's `BankName` — `ImportEndpoints.cs:41-85`.
- Parsers are registered in `DI/ImportServiceCollectionExtensions.cs:12`; the comment there already says adding banks only needs another `AddScoped` line. `GET /accounts/banks` derives its list from registered parsers (`AccountEndpoints.cs:17-26`), so the new bank appears in the account form automatically.
- The frontend hardcodes the manual-fallback bank picker list: `SUPPORTED_BANKS = ["mBank"]` at `MyFinances/frontend/app/routes/import.tsx:54`.
- `MBankCsvParser` is the pattern: CsvHelper with `;` delimiter, `HasHeaderRecord = false`, `MissingFieldFound = null`, `BadDataFound = null`, `pl-PL` decimal parsing, row-level `FormatException`/`OverflowException` counted in `skippedErrorCount`, `CanParse` resets the stream position — `Import/MBankCsvParser.cs:104-178`.
- `DedupHash` = userId + date + amount + description + accountId; identical rows within one file are not flagged on first import (dedup compares to the DB only) — `Import/DedupHash.cs:11`, `ImportEndpoints.cs:66-83`.
- Internal-transfer detection pairs opposite amounts on different accounts within ±2 days and ignores description — `Categorization/TransferDetectionService.cs:64-67`, so Erste rows pair with mBank rows without any parser involvement.

### Erste export format (from the two provided samples)

- UTF-8 without BOM, LF line endings, **no header row**, every line ends with a trailing delimiter (a 9th empty field).
- Erste's export dialog offers four delimiters, all with the identical field layout: `;`, `,`, tab and `|` (samples `erste historia *.csv`, `historia erste tab|pipe|przecinek.csv`). In the `,` variant the amount/balance fields are double-quoted (`"727,51"`) because they contain a decimal comma; the other variants are unquoted.
- Line 1 is a statement summary, not a transaction: `export date (yyyy-MM-dd); period start (dd-MM-yyyy); '<account number>; holder name + address; currency; opening balance; closing balance; transaction count;`.
- Data lines (0-indexed fields): `0` booking date `dd-MM-yyyy`, `1` transaction date `dd-MM-yyyy`, `2` description, `3` counterparty name, `4` counterparty account, `5` amount (pl-PL decimal, signed), `6` balance after, `7` sequence number. Card rows have empty fields 3 and 4.
- Rows are newest-first. The sample has three identical `PRZELEW ŚRODKÓW +500,00` rows on the same date.

## Desired End State

Uploading an Erste export on the import page auto-detects the bank, returns 29 rows for the sample, flags duplicates on re-import, and the committed transactions flow into categorization, charts and transfer detection like mBank ones. Users can create an "Erste" account in settings and, if auto-detection fails, pick "Erste" in the manual bank picker. Verify by `dotnet test` plus a manual import of the (redacted) sample.

### Key Discoveries:

- Fields are addressed by index in `MBankCsvParser` (`csv.GetField(3)`, `GetField(6)`); the Erste parser does the same, so the column map above is the contract.
- Absence of a header means `CanParse` must recognise the file by the structure of line 1, not by a header prefix; it must not match mBank files (they lack this shape) and mBank's `CanParse` will not match Erste (it needs `#Data księgowania`).
- Real samples contain real IBANs, holder name and home address — a redacted fixture is required, as for mBank (`Tests/Fixtures/mbank-sample-redacted.csv`).
- Test fixtures must be listed in `Tests/MyFinances.Api.Tests.csproj` with `CopyToOutputDirectory` (`:28-38`).

## What We're NOT Doing

- Revolut import (S-07) — Erste is planned ahead of it by explicit decision, overriding the roadmap gate; Revolut remains `proposed`.
- Verifying the statement's account number against the selected `Account.AccountNumber`, or validating opening balance + rows = closing balance / the row count in the summary line.
- Using the counterparty name/account columns (description is field 2 only) and the balance/sequence columns.
- Recognising round-up savings transfers ("Wpłata końcówek …") as internal transfers; they import as ordinary expenses.
- Any change to `IBankStatementParser`, `ParseResult`, `DedupHash`, the import endpoints, or the DB schema.
- Handling non-PLN statements beyond skipping them (no currency conversion, no explicit error response).

## Implementation Approach

Mirror `MBankCsvParser`'s structure: a small class behind the existing interface, CsvHelper for field splitting, row-level error counting. Decisions taken during planning:

- **Date** = transaction date (field 1), not booking date (field 0), so month-end card purchases land in the month they happened; this differs from mBank's booking-date behaviour and is within transfer detection's ±2 day tolerance.
- **Description** = field 2 verbatim; counterparty columns ignored.
- **Currency**: the summary line's currency (field 4). If it is not `PLN`, return no transactions and set `SkippedErrorCount` to the number of data rows (PRD FR-003: other currencies filtered with a skipped count).
- **Row errors**: an unparseable transaction date or amount skips that row and increments `SkippedErrorCount`; fully blank lines are ignored without counting.
- **Delimiter**: all four Erste delimiters (`;`, `,`, tab, `|`) are supported. The parser detects the delimiter by trying each in turn against line 1 and accepting the first for which the structural check passes (a wrong delimiter leaves field 0 as the whole line, so exactly one matches); it does not count characters, since `;` files also contain decimal commas.
- **Within-file identical rows** are all returned (no in-parser collapsing) — consistent with mBank's identical BLIK rows.

## Phase 1: Erste parser, redacted fixture, unit tests

### Overview

Deliver `ErsteCsvParser` and prove it against a redacted real-format fixture, independent of DI and HTTP.

### Changes Required:

#### 1. Parser

**File**: `MyFinances/backend/Import/ErsteCsvParser.cs`

**Intent**: Implement `IBankStatementParser` for the Erste export so it can be recognised without a header and parsed into `NormalizedTransaction`s.

**Contract**:
- `BankName` = `"Erste"` (the string stored on `Account.BankName` and offered by `/accounts/banks`).
- `CanParse`: decode leniently as UTF-8 (strip BOM if present; invalid bytes become replacement characters and `CanParse` must never throw — every registered parser's `CanParse` runs on every upload, including cp1250 mBank files), take the first non-empty line and, for each candidate delimiter in `;`, `,`, tab, `|`, split it with CsvHelper (so quoted fields in the `,` variant are honoured); true when for some delimiter it has at least 8 fields, field 0 parses as `yyyy-MM-dd`, field 1 parses as `dd-MM-yyyy`, field 2 starts with `'`, and field 4 is a 3-letter currency code. Restore the stream position afterwards when seekable (same contract as `MBankCsvParser.CanParse`). The detection logic is shared with `Parse` (one private helper returning the matching delimiter or none).
- `Parse`: detect the delimiter as above, skip line 1 (summary), read currency from it; for each following non-blank line (split with the detected delimiter) take Date = field 1 (`dd-MM-yyyy`), Description = field 2, Amount = field 5 parsed with `pl-PL`. Rows with an unparseable date or amount increment `SkippedErrorCount` and parsing continues. Non-`PLN` currency → empty transaction list and `SkippedErrorCount` = number of data rows. If no delimiter matches (the endpoint calls `Parse` on a manually chosen bank even when `CanParse` rejected the file), return an empty list with `SkippedErrorCount` 0 rather than throwing — mirrors `MBankCsvParser`.
- Use CsvHelper configured like `MBankCsvParser` (`HasHeaderRecord = false`, `MissingFieldFound = null`, `BadDataFound = null`) with `Delimiter` set to the detected delimiter.

#### 2. Redacted fixtures

**Files**: `MyFinances/backend/Tests/Fixtures/erste-sample-redacted.csv` (`;`, from the September sample) and `erste-sample-redacted-tab.csv`, `-pipe.csv`, `-comma.csv` (from the August sample: 21 rows, same content in each of the three other delimiters).

**Intent**: Real-format copies of the provided samples with personal data replaced, so tests run against the true shape of every delimiter variant (UTF-8, no header, summary line, trailing delimiter, Polish characters, quoted amounts in the `,` variant).

**Contract**: Keep the line structure, amounts and dates of the originals (September: 1 summary + 29 data rows; August: 1 summary + 21 data rows). Replace the account number, holder name and address in the summaries, the counterparty names and accounts in transfer rows, and the phone number/person name inside descriptions with obviously fake values; keep Polish diacritics (e.g. `Ś`, `Ł`) in at least one description. Keep the three identical `PRZELEW ŚRODKÓW 500,00` rows dated 04-09-2026 in the `;` fixture, and the booking/transaction date difference on rows such as 03-09 / 02-09 and 01-09 / 31-08. The three August fixtures must stay row-for-row identical in content so one expected-values set covers them. Register all four files in `Tests/MyFinances.Api.Tests.csproj` with `CopyToOutputDirectory = PreserveNewest`, next to the mBank fixtures.

#### 3. Unit tests

**File**: `MyFinances/backend/Tests/ErsteCsvParserTests.cs`

**Intent**: Pin the parser's behaviour, following `MBankCsvParserTests` (xUnit `[Fact]`, fixture opened from `AppContext.BaseDirectory/Fixtures`).

**Contract**: tests for —
- `CanParse` true for each of the four Erste fixtures (`;`, tab, pipe, comma) and false, without throwing, for the cp1250 mBank fixture (`mbank-sample-redacted.csv`) and the UTF-8 remojibake one (and mBank's `CanParse` false for the Erste fixtures);
- `Parse` on a non-Erste file (e.g. the mBank fixture) returns zero transactions and zero skipped;
- `Parse` returns 29 transactions, 0 skipped, for the `;` fixture, and 21 transactions, 0 skipped, for each of the tab/pipe/comma fixtures with identical dates, descriptions and amounts across the three (including the quoted `"-261,54"`-style amounts in the comma variant);
- Date is the transaction date (row booked 03-09-2026, paid 02-09-2026 → 2026-09-02; row booked 06-09 / paid 04-09 → 2026-09-04; row booked 01-09 / paid 31-08 in the August files → 2026-08-31) and the summary line is not parsed as a transaction;
- amounts are signed pl-PL decimals (e.g. `-69,98` → `-69.98m`, `+45,00` refund → `45.00m`);
- the three identical +500 rows are all returned;
- a Polish-diacritic description round-trips intact;
- an in-memory copy with a malformed amount in one row yields one skipped error and the other rows still parse;
- an in-memory copy whose summary currency is changed to `EUR` yields zero transactions and `SkippedErrorCount` = 29.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Parser tests pass: `dotnet test --filter ErsteCsvParserTests` (from `MyFinances/backend`)
- Existing suite still passes: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- The fixtures contain no real IBAN, holder name, address, phone number or counterparty name from the original samples.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Wire into the app and update the roadmap

### Overview

Register the parser, expose "Erste" in the frontend manual picker, verify the full HTTP flow, and record the sequencing override in the roadmap.

### Changes Required:

#### 1. DI registration

**File**: `MyFinances/backend/DI/ImportServiceCollectionExtensions.cs`

**Intent**: Make the import endpoints and `/accounts/banks` aware of the new parser.

**Contract**: add `services.AddScoped<IBankStatementParser, ErsteCsvParser>();` alongside the mBank registration, and update the adjacent comment so it no longer says Erste is future work.

#### 2. Frontend bank list

**File**: `MyFinances/frontend/app/routes/import.tsx`

**Intent**: Let the user pick Erste in the manual-fallback bank selector.

**Contract**: `SUPPORTED_BANKS` becomes `["mBank", "Erste"]` (line 54), and the comment above it is updated.

#### 3. Endpoint tests

**File**: `MyFinances/backend/Tests/ImportEndpointsTests.cs`

**Intent**: Prove detection, bank-mismatch and duplicate handling work end to end for Erste using the redacted fixture, reusing the existing helpers (`CreateAccountAsync`, `BuildUploadRequestAsync`).

**Contract**: tests for —
- parse of the Erste fixture with an "Erste" account → 200, `Bank == "Erste"`, 29 rows, `BankMismatch == false`;
- the same file with an "mBank" account → `BankMismatch == true`;
- commit then re-parse the same file → every previously committed row (including all three identical +500 rows) is flagged `IsDuplicate`;
- parse of the tab, pipe and comma fixtures through the HTTP endpoint → 200, `Bank == "Erste"`, 21 rows;
- manual `bank=Erste` fallback path resolves the Erste parser for a file `CanParse` rejects (e.g. `bank=Erste` with the mBank-format or unrecognised content) → 200 with 0 rows, not an error.

#### 4. Bank-list test

**File**: `MyFinances/backend/Tests/AccountEndpointsTests.cs`

**Intent**: Keep the existing `GET /accounts/banks` test green now that a second parser is registered.

**Contract**: the assertion at line 92 changes from `["mBank", "Other"]` to `["mBank", "Erste", "Other"]` (order follows parser registration, "Other" last).

#### 5. Roadmap sync

**File**: `context/foundation/roadmap.md`

**Intent**: Reflect that S-08 no longer waits for S-07.

**Contract**: set S-08's prerequisite to `S-01` in the `At a glance` table (`roadmap.md:51`), the S-08 block (`:188`) and the dependency table (`:232`, "Depends on S-07"), and reword the stream C row (`:63`, "sequentially gated … Erste only after Revolut") so it no longer says Erste waits for Revolut; add a one-line note that the PRD FR-003 sequencing gate was waived by the user on 2026-10-01; leave S-07 `proposed`. (Status flips to `in-progress`/`done` are handled by `/10x-implement` and `/10x-archive`; per `lessons.md`, sync the matching GitHub issue after each commit/PR that changes roadmap status.)

### Success Criteria:

#### Automated Verification:

- Backend builds and the full suite passes: `dotnet test` (from `MyFinances/backend`)
- Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- With both servers running, creating an "Erste" account in settings works (Erste is offered in the bank dropdown).
- Uploading a redacted Erste file on the import page auto-detects Erste and shows 29 rows; committing and re-uploading shows duplicates for every row.
- The imported rows appear in the categorization queue and dashboard history with the transaction dates (e.g. the 03-09 booked / 02-09 paid row shows 2026-09-02).
- Uploading an mBank file to an Erste account still works and shows the bank-mismatch notice.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding.

---

## Testing Strategy

### Unit Tests:

- `ErsteCsvParserTests` as listed in Phase 1: detection (positive/negative), 29 rows, transaction-date semantics, signed amounts, identical rows kept, diacritics, malformed-amount row, non-PLN currency.

### Integration Tests:

- `ImportEndpointsTests` Erste scenarios as listed in Phase 2 (detect, mismatch, re-import duplicates, manual-bank fallback).

### Manual Testing Steps:

1. Start backend (`dotnet run`) and frontend (`npm run dev`), log in, add an Erste account in settings.
2. Import the redacted Erste fixture; check row count, dates and amounts against the file.
3. Commit, re-import, and confirm all rows are flagged as duplicates.
4. Open the categorization queue and confirm the imported rows appear.

## Performance Considerations

None — statements are tens to low hundreds of rows, parsed fully in memory like mBank.

## Migration Notes

No schema or data changes. Existing mBank imports are unaffected; dedup hashes are per account and Erste transactions use their own account.

## References

- Roadmap item: `context/foundation/roadmap.md` (S-08, Change ID `erste-import`)
- PRD: `context/foundation/prd.md` FR-003
- Pattern parser: `MyFinances/backend/Import/MBankCsvParser.cs`
- Pattern tests: `MyFinances/backend/Tests/MBankCsvParserTests.cs`, `MyFinances/backend/Tests/ImportEndpointsTests.cs`
- Lessons: `context/foundation/lessons.md` (sync GitHub issue with roadmap)
- Sample exports: `D:\repos\michLukaszewicz\Przykładowe pliki\erste\` (six files: `;` ×2 for September, plus tab/pipe/comma for August; contain real personal data — do not copy into the repo)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Erste parser, redacted fixture, unit tests

#### Automated

- [ ] 1.1 Backend builds: `dotnet build` (from `MyFinances/backend`)
- [ ] 1.2 Parser tests pass: `dotnet test --filter ErsteCsvParserTests` (from `MyFinances/backend`)
- [ ] 1.3 Existing suite still passes: `dotnet test` (from `MyFinances/backend`)

#### Manual

- [ ] 1.4 The fixtures contain no real IBAN, holder name, address, phone number or counterparty name from the original samples.

### Phase 2: Wire into the app and update the roadmap

#### Automated

- [ ] 2.1 Backend builds and the full suite passes: `dotnet test` (from `MyFinances/backend`)
- [ ] 2.2 Frontend types check: `npm run typecheck` (from `MyFinances/frontend`)
- [ ] 2.3 Frontend builds: `npm run build` (from `MyFinances/frontend`)

#### Manual

- [ ] 2.4 With both servers running, creating an "Erste" account in settings works (Erste is offered in the bank dropdown).
- [ ] 2.5 Uploading a redacted Erste file on the import page auto-detects Erste and shows 29 rows; committing and re-uploading shows duplicates for every row.
- [ ] 2.6 The imported rows appear in the categorization queue and dashboard history with the transaction dates (e.g. the 03-09 booked / 02-09 paid row shows 2026-09-02).
- [ ] 2.7 Uploading an mBank file to an Erste account still works and shows the bank-mismatch notice.
