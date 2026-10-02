---
date: 2026-10-02T12:16:32+02:00
researcher: Claude (Sonnet 5.5)
git_commit: d3923d2
branch: main
repository: Kurs 10xDev (MyFinances)
topic: "Rollout Phase 2 (Parser correctness, Risk #2): where can a CSV/PDF parser misread a row without warning, and what do existing tests already prove?"
tags: [research, codebase, import, parsers, culture, balance-check, test-plan]
status: complete
last_updated: 2026-10-02
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Parser correctness (Risk #2)

**Date**: 2026-10-02T12:16:32+02:00 · **Git Commit**: d3923d2 · **Branch**: main

## Research Question

Risk #2 of `context/foundation/test-plan.md`: a CSV or PDF parser misreads a row (amount, date, locale-dependent format) and totals diverge from the statement without any warning. Ground it in code: how are dates/amounts parsed, does machine culture matter, what does the balance check actually guarantee, what do the existing tests already prove, and what remains unprotected. Verify (not accept) the plan's response guidance. Paths below are relative to `MyFinances/backend` unless noted.

## Summary

1. **Culture independence holds by construction, on the inspected paths.** Every date/number parse in the five parsers passes an explicit culture (`InvariantCulture` for dates, a pinned `pl-PL` for amounts); none uses `CurrentCulture` (CSV: `Import/MBankCsvParser.cs:155,167`, `Import/ErsteCsvParser.cs:137-138`; PDF: `Import/Pdf/PdfStatementReader.cs:27,133`, `Import/MBankPdfParser.cs:384-385`, `Import/VeloBankPdfParser.cs:279-280`, `Import/ErstePdfParser.cs:307-308`). No test sets a non-default thread culture, so this is currently *unproven*, not *broken*. The real culture hazard is different: the pinned `pl-PL` object depends on ICU/globalization data. No `InvariantGlobalization` setting exists in the csproj files, but the production image is Debian `mcr.microsoft.com/dotnet/aspnet:10.0` (`Dockerfile:28`) and ICU presence was not checked. The plan's challenge "date format is stable across cultures" is therefore true for the thread culture and open for the ICU-less runtime.
2. **The balance check protects PDFs only, and its strength differs sharply per bank.** CSV parsers have no balance/total check at all (neither reads the Saldo column or summary totals). Among PDFs, mBank is anchored (opening + per-row chain + closing + turnover summary, `Import/MBankPdfParser.cs:487-515`); VeloBank and Erste check only internal consistency of printed numbers (`Import/VeloBankPdfParser.cs:299-344`, `Import/ErstePdfParser.cs:352-388`). The plan's challenge "the balance check catches every misread" is **false** for specific, enumerable cases (below).
3. **Existing tests are broad but share one weakness: PDF expectations come from a builder that encodes the same author's reading of the bank format as the parser.** Expected values are authored literals (good, not parser output), yet `FormatMoney`/`FormatDate` in the builders emit what the parser expects, so a shared misunderstanding passes. All six PDF fixtures are synthetic; only the CSV fixtures are redacted real exports.
4. **Concrete test gaps for Risk #2** (all confirmed by search of `Tests/**`): no non-default-culture run; no assertion of mBank `dd.MM.yyyy` dates by value (count-only); no thousands-separated or NBSP Kwota in CSV; no mid-file bad-date row for mBank CSV (silent truncation); no U+2212/NBSP/dot-decimal rejection test for PDF; no compensating-error / both-column sign-flip cases.

## Detailed Findings

### Date and amount parsing (inspected: the 5 parsers + `PdfStatementReader`)

- **mBank CSV**: dates `TryParseExact` with `["yyyy-MM-dd","dd.MM.yyyy"]`, Invariant (`Import/MBankCsvParser.cs:16,155`). Both unambiguous; no `dd/MM` or `MM/dd` accepted. Only column 0 (booking date) is used. Amount: `decimal.Parse(raw, Number|AllowLeadingSign, pl-PL)` (`:167`).
- **Erste CSV**: transaction date fixed `dd-MM-yyyy` in column 1 (booking date column 0 ignored); amount `decimal.TryParse` with pl-PL (`Import/ErsteCsvParser.cs:137-138`). Delimiter is detected from the first non-blank line, order `;` `,` tab `|`, requires >= 8 fields plus a summary-line shape check (`LooksLikeSummary`, `:77-78`).
- **PDF amounts (all banks)**: text must first match `NumberPattern` (comma decimal, exactly 2 decimals, optional `+`/`-`, ASCII space between thousand groups, digit run bounded) then `decimal.Parse` with pl-PL after stripping U+0020 only (`Import/Pdf/PdfStatementReader.cs:24,132-133`). Anything else (U+2212, NBSP, dot decimal) fails the regex and the row is treated as unreadable.
- **PDF dates**: mBank `yyyy-MM-dd`, VeloBank `dd.MM.yyyy`, Erste `d MMM yyyy` with a hardcoded Polish month-abbreviation table after diacritic folding and `DaysInMonth` validation (`Import/ErstePdfParser.cs:63-72,307-332`).
- **Bad-row behaviour differs by format.** PDF: an unreadable row inside a table **throws** `StatementIntegrityException` ("a table row could not be read") rather than skipping (`Import/MBankPdfParser.cs:369-381`, `Import/VeloBankPdfParser.cs:246-277`, `Import/ErstePdfParser.cs:271-294`). CSV: a bad amount on a valid-date row is **skipped and counted** in `SkippedErrorCount` (`Import/MBankCsvParser.cs:171-176`); a bad date on mBank CSV **stops the parse** (`break`, `:155-161`) with no error count.

### Silent-misread paths (observed from code; severity is a hypothesis for planning)

- **mBank CSV early stop**: the first row whose column 0 is not a parseable date ends the loop; every later transaction is dropped and not counted as skipped (`Import/MBankCsvParser.cs:155-161`). A mid-file row with an unexpected date such as `1.8.2026` would silently truncate the import. No CSV balance check exists to notice. Only the genuine footer is tested.
- **CSV has no integrity check**: no Saldo or summary reconciliation in either CSV parser (greps of both parsers and the integrity exception usage, which is confined to PDF parsers and `Import/ImportEndpoints.cs:77`). FR-018 (`context/foundation/prd.md:66`) specifies the balance check for PDFs, so this is by design, but it means CSV correctness rests entirely on parser tests.
- **VeloBank PDF check is pairwise on booked PLN rows**: pending rows are skipped; a foreign-currency row resets the chain (`previousBooked = null`, `Import/VeloBankPdfParser.cs:304-309`); no opening/closing/summary anchor, so a dropped newest/oldest row, or a drop adjacent to a pending/foreign row, is invisible.
- **Erste PDF check is an order-independent multiset linkage** with exactly one unmatched value tolerated on each side (`Import/ErstePdfParser.cs:352-388`): dropped newest/oldest row accepted (pinned by tests `ErstePdfParserTests.cs:450,463`), swaps of equal-valued rows pass, and any non-PLN row disables the whole check (`:354-357`).
- **Compensating errors**: all checks compare printed numbers to printed numbers. A misread amount whose balance is misread consistently passes; dates and descriptions are never verified (only a VeloBank card-amount cross-check touches descriptions, `:332-344`).
- **Endpoint**: a rejected parse returns 422 ProblemDetails and the parse endpoint only reads the database (`Import/ImportEndpoints.cs:73-82,131`); persistence is only in `/commit` (`:134-211`) which takes client-supplied rows and does not re-validate, so the integrity check is advisory at the HTTP level. Endpoint tests assert 422 + title prefix (`Tests/Import/ImportEndpointsTests.cs:657,822,1019,1164`); the mBank test asserts no `rows` property; DB-empty after 422 was not confirmed.
- **Wrong-bank manual choice**: `Parse` runs even if `CanParse` was false (`Import/ImportEndpoints.cs:51-54`), giving 200 with zero rows instead of a rejection (side finding; adjacent to Risk #2, likely out of scope).
- **ICU**: `PlPl` is a static initializer `CultureInfo.GetCultureInfo("pl-PL")`. Under invariant-globalization/no-ICU it may throw (`TypeInitializationException`) outside the `TryRead` catch-all, or parse commas as thousands separators. Unverified at runtime.

### Existing tests (`Tests/Import/Parsers/*`, `Tests/Support/*`, `Tests/Fixtures/*`)

- **CSV**: `MBankCsvParserTests.cs` (4 fixture variants) and `ErsteCsvParserTests.cs` (4 delimiter variants, EUR file, bad amount) assert authored literals for dates/amounts, including duplicate identical rows, skipped-error counts, and transaction-vs-booking date for Erste. Weakness: the dmy test asserts count only (`MBankCsvParserTests.cs:93-106`), so a day/month swap passes. Delimiter-variant equality (`Assert.Equal(tab.Transactions, pipe.Transactions)`) compares parser outputs to each other, though counts are literal.
- **PDF**: three `*PdfParserTests.cs` (each 650-680 lines) plus three `*PdfFixtureTests.cs`; extensive integrity-rejection coverage (tampered row/closing balance, summary tamper, unreadable/absurd amount, unreadable date, missing opening/closing/summary, foreign-currency behaviour, Erste blind spots pinned). Expected values are hand-authored rows fed to a builder, then compared with `Parse` output; some literal spot checks (`MBankPdfParserTests.cs:~255-273`).
- **Fixture provenance**: CSVs are real exports with personal data replaced (`mbank-sample-redacted*.csv` from 6ec32c2; `erste-sample-redacted*.csv` from a736348) plus hand-derived mojibake/comma-wrapped variants; all six PDFs are synthetic, built from invented rows in `*SampleData.cs` by `*PdfBuilder.cs`, with opt-in regeneration (`REGENERATE_PDF_FIXTURES=1`). There is no real bank PDF in the repo; PdfPig word-splitting of thousands spaces/minus signs is asserted only for synthetic PDFs (`Fixture_PrintsThousandsBalanceAsTwoWords`). An independent-oracle pattern already exists: `MBankCsvBuilder` and `MBankPairedStatement` (`Tests/Support/`).
- **Culture**: `CultureInfo` appears in `Tests/**` only inside builders (Invariant, plus pl-PL in `MBankCsvBuilder.cs:18`); no test sets `CurrentCulture`/`CurrentUICulture`.
- **Test infra**: net10.0, xunit 2.9.2; no `InvariantGlobalization` in either csproj, Dockerfile or render.yaml. mBank CSV tests need `CodePagesEncodingProvider` registered (`Import/MBankCsvParser.cs:23-30`).

## Code References

- `Import/MBankCsvParser.cs:16,19,155-176` - date formats, pl-PL, early-stop `break`, per-row skip
- `Import/ErsteCsvParser.cs:77-78,137-138` - summary detection, transaction date + amount parse
- `Import/Pdf/PdfStatementReader.cs:24,27,132-144` - NumberPattern, pinned pl-PL, ParseNumber, Rejection
- `Import/MBankPdfParser.cs:384-385,487-515` - date parse; opening/row/closing/summary integrity
- `Import/VeloBankPdfParser.cs:279-280,299-344` - date parse; pairwise chain, reset on foreign row, card cross-check
- `Import/ErstePdfParser.cs:63-72,307-332,352-388` - month table, date parse, multiset linkage
- `Import/ImportEndpoints.cs:51-54,73-82,131,134-211` - manual-bank parse, 422 mapping, read-only parse, commit
- `Tests/Import/Parsers/MBankCsvParserTests.cs:93-106` - count-only dmy test
- `Tests/Support/MBankCsvBuilder.cs`, `Tests/Support/MBankPairedStatement.cs` - existing independent-oracle builders

## Architecture Insights

- Parsers convert bank text to `NormalizedTransaction(DateOnly, string, decimal)` (`Import/NormalizedTransaction.cs`); culture is pinned inside each parser, not configured globally, so a culture test must vary `CultureInfo.CurrentCulture` and `CurrentUICulture` around the parse and assert identical literals.
- Policy asymmetry: PDF fails loud (throw -> 422), CSV fails quiet (skip count / silent stop). Risk #2's "without any warning" therefore concentrates in CSV and in the weaker PDF checks.
- Test pattern to continue: authored literal rows -> file builder -> parser -> literal assertion (lessons.md: Arrange/Act/Assert comments, shared per-class setup).

## Historical Context (from prior changes)

- Interview Q2 ("parser refactor changed date format, import threw") has no matching doc. The only date-format commit in the last 60 days is 6ec32c2 (additive: `yyyy-MM-dd` -> `["yyyy-MM-dd","dd.MM.yyyy"]`, added the dmy fixture); the other parser commits are feature work. Treat as consistent with the interview, but the exact incident is not reconstructable from git.
- `context/archive/2026-10-02-pdf-statement-import/research.md:71,95` - date formats across banks and in-statement integrity checks; `context/archive/2026-10-01-erste-import/reviews/impl-review.md:32` - flagged DetectDelimiter skips whitespace-only leading lines while Parse does not (the test for that now exists in ErsteCsvParserTests).
- `context/changes/testing-import-integrity-dedup/plan.md:23` - test CSV writer should emit cp1250 or ASCII-only; `MBankCsvBuilder` follows this.

## Related Research

- `context/changes/testing-import-integrity-dedup/` (Phase 1, complete) - dedup/overlap; shares helpers `ImportTestHelpers`, `MBankCsvBuilder`, `MBankPairedStatement`.

## Verification of the test plan's response guidance

- "Passes on the fixture so it parses correctly": **partly wrong** for PDFs (synthetic fixtures encode the author's format reading) and for the dmy fixture (count-only). Needs oracles taken from outside the builder: printed values written as literals straight from a real-statement layout, or at minimum literal date/amount assertions per row.
- "The balance check catches every misread": **wrong** (see Silent-misread paths); also nonexistent for CSV.
- "Date format is stable across cultures": **holds for thread culture by construction; unproven by tests; open for no-ICU runtime**.
- Cheapest layer "unit with independent fixtures + a different-culture run": **confirmed**, no integration needed.
- Plan anti-pattern "asserting only row count": one live instance exists (dmy test).

## Open Questions

1. Does .NET's pl-PL parse accept a regular space as group separator for CSV Kwota (`"1 234,56"`), and NBSP? Behavior was not run; settle empirically in a test (outcome either way is fine if pinned: parsed correctly, or skipped and counted, never mis-valued).
2. Is ICU present in the production Debian image, and is that in scope for a unit test (e.g. assert `CultureInfo.GetCultureInfo("pl-PL").NumberFormat.NumberDecimalSeparator == ","`) or only for the deployment phase?
3. Should the mBank CSV early-stop on a bad mid-file date be tested as a characterization (pin as known limitation) or fixed? Fixing is a product change beyond a test rollout; default recommendation: pin as `_KnownLimitation`.
4. Are the VeloBank/Erste weaker-check blind spots (dropped newest/oldest row, compensating errors) to be pinned as characterization tests or left documented? Erste's are already pinned.
5. Whether the endpoint's 422 leaves the DB empty is unverified (Phase 1 helpers `GetStoredAsync` could assert it cheaply).
