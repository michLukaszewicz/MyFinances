# PDF Statement Import (VeloBank) — Plan Brief

> Full plan: `context/changes/pdf-statement-import/plan.md`
> Research: `context/changes/pdf-statement-import/research.md`

## What & Why

Add PDF as a second statement input format next to CSV, starting with VeloBank — a bank used daily that offers no CSV export, so PDF is its only way into MyFinances. The PDF is read deterministically with PdfPig (no AI at runtime), through a per-bank-and-format parser behind the existing `IBankStatementParser` interface. mBank and Erste PDF parsers are recorded as optional follow-ups.

## Starting Point

The import pipeline is CSV-only: two parsers (mBank, Erste), a `/import/parse` endpoint that tries each parser's `CanParse` and falls back to a manually chosen bank name, a verbatim-description dedup hash, and a frontend hardcoded to `.csv` and two banks. Research measured the real VeloBank PDF layout (vector table grid, vertically centred cells, repeated headers, pending rows) and prototyped a parser that recovered 17/17 and 62/62 rows with the running-balance chain holding on every booked pair.

## Desired End State

A VeloBank account holder uploads a "Historia rachunku" PDF, sees every transaction (pending ones included), reviews duplicates on re-import, and the rows flow into categorization and charts like any CSV import. A PDF whose balances do not add up is rejected with a clear message. CSV imports are unchanged, and PRD and roadmap record VeloBank and the PDF format.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Extraction approach | PdfPig deterministic parser; no LLM, no intermediate format | Prototypes pass integrity checks on real files, and the verbatim-description hash needs deterministic output | Research |
| Parser structure | One parser per bank and format, shared `BankName`, format sniffed from the first bytes | Fits the existing interface; only the manual-bank fallback needed fixing | Research (user decision) |
| Scope | VeloBank only; mBank and Erste PDF become optional roadmap slices S-12/S-13 | Smallest shippable unit, no cross-format policy needed yet | Plan |
| Order | VeloBank first, others optionally later | VeloBank has no CSV alternative | Research (user decision) |
| Date semantics | Transaction date | Pending rows have no booking date; the hash must not change when a row is booked | Research |
| Pending rows | Imported with their transaction date | User choice over the skip recommendation; checked after the fact against the bank's booked version | Plan |
| Integrity failure | Reject the file with HTTP 422 and a PII-free message | Wrong amounts must not reach the database; 422 (not 400) because the frontend shows the bank picker on every 400 | Plan |
| Non-PLN rows | Skipped and counted | PRD FR-003 | Research / PRD |
| Page limit | 200 pages, otherwise unreadable | Bounds work on top of the 5 MB cap | Plan |
| Fixtures | Synthetic generator (PdfPig builder + OFL font) with two committed PDFs; edge cases generated in memory | No personal data by construction, and edge cases the real files lack | Plan |
| Documents | Minimal PRD edit (new FR-018) plus roadmap S-11 to S-13 | Keeps the PRD consistent and enables status sync via Change ID | Plan |

## Scope

**In scope:** PdfPig dependency; format enum, sniffer and endpoint selection; `VeloBankPdfParser`; synthetic fixtures and generator; DI, frontend (bank list, `.pdf`, copy); tests; PRD and roadmap edits.

**Out of scope:** mBank/Erste PDF parsers; cross-format dedup policy or description normalization; LLM or OCR; Revolut; account-number verification; foreign-currency conversion; a generic PDF table helper; schema changes.

## Architecture / Approach

The endpoint sniffs the upload once (`%PDF-` → PDF, else CSV) and only considers parsers of that format; the manual fallback resolves by bank name inside that format and answers "`<bank>` import does not support PDF files" when the bank lacks it. `VeloBankPdfParser` builds rows from the table's separator rectangles and columns from header cells, joins cell text per cell, imports pending rows, skips and counts non-PLN rows, and throws `StatementIntegrityException` when the balance chain, the card amount in the description, or a row read fails, which the endpoint maps to 422.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. PRD and roadmap | FR-018, slices S-11 to S-13, milestone note | Wording of the locked PRD needs your review |
| 2. Format-aware pipeline | PdfPig package, `Format`, sniffer, endpoint selection, 422 mapping, tests | CSV regressions |
| 3. Synthetic fixtures | OFL font, generator, two committed PDFs, fixture tests | Generator fidelity to the real layout |
| 4. VeloBank parser | `VeloBankPdfParser`, integrity checks, unit tests, real-sample check | Real-file edge cases the samples lack |
| 5. Wire into the app | DI, frontend, endpoint tests, manual end-to-end | Frontend/endpoint interplay |

**Prerequisites:** approval to download (or supply) an OFL-licensed `.ttf`; nuget.org access via the repo `NuGet.Config`; the real samples in `Przykładowe pliki\veloBank\` (outside the repo).
**Estimated effort:** ~5 sessions across 5 phases.

## Open Risks & Assumptions

- Pending rows are imported on the assumption that a booked row keeps the same description; no pending-to-booked pair has been observed. If a booked description differs, the payment is counted twice. Check by re-exporting after the 2026-10-01 pending payments book (post-merge step in the plan).
- Foreign-currency, refund, BLIK, ATM and fee rows have never appeared in a VeloBank sample; their layout is unverified, and the balance check is skipped around non-PLN rows.
- The sibling integrity checks (card amount, unreadable row) use the same reject policy by assumption; you chose it explicitly only for the balance check.
- Synthetic fixtures test the generator's copy of the layout; only the manual real-file checks cover the real PDFs.
- The generator needs an OFL font file, so the implementer must ask for your approval to download it; the fixture PDFs embed the full font and are larger than the real files.
- The PRD is marked as the locked scope source; Phase 1 edits it.

## Success Criteria (Summary)

- The real 90-day and one-year VeloBank PDFs import as 17 and 62 rows; re-import flags every row as a duplicate and the overlapping 17 rows match across exports.
- A PDF with inconsistent balances is rejected with a clear message, and mBank/Erste CSV imports behave exactly as before.
- `dotnet test`, `npm run typecheck` and `npm run build` pass, and PRD/roadmap describe VeloBank and PDF import.
