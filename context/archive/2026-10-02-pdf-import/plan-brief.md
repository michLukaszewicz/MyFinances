# PDF Statement Import (mBank and Erste) — Plan Brief

> Full plan: `context/changes/pdf-import/plan.md`
> Research: `context/changes/pdf-statement-import/research.md` (shared with the VeloBank change; §2b mBank, §2c Erste)

## What & Why

Add PDF as a second statement format for mBank and Erste Bank Polska, planned together in one change. Both banks already import from CSV; the PDF route covers statements the user only has as PDF and reuses the pipeline VeloBank PDF import built. Importing one period through both formats would silently duplicate rows, so the change also makes the app notice that.

## Starting Point

`pdf-statement-import` shipped the format-aware import endpoint, the `StatementFormat` discriminator, 422 for failed integrity checks, PdfPig, synthetic-fixture generators and `VeloBankPdfParser` (471 lines mixing generic PDF mechanics with VeloBank specifics). mBank and Erste have CSV parsers only; `ImportBatch` does not record where an import came from.

## Desired End State

Uploading an mBank or Erste PDF on the import page auto-detects the bank and lists the rows like any other import; a PDF whose own balance figures do not add up is rejected with an explanation. If the file's date range overlaps transactions imported from the other format on the same account, a non-blocking banner shows how many. CSV and VeloBank imports are unchanged; PRD and roadmap describe the new coverage.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| CSV vs PDF on one account | Store `SourceFormat` on `ImportBatch`; parse response reports an overlap count; non-blocking banner | Hashes cannot match across formats (padded mBank CSV descriptions, lossy Erste PDF), so the app flags overlap instead | Plan (user) |
| Shared code | Thin helper extracted from `VeloBankPdfParser`; VeloBank migrated, behaviour unchanged | Three PDF parsers would otherwise copy PdfPig error handling and number parsing | Plan (user) |
| Roadmap | Merge S-12 and S-13 into S-12, Change ID `pdf-import`; close issue #30 as merged | One change, one item, unambiguous status sync | Plan (user) |
| Order | Helper and warning, then mBank, then Erste | App is useful after mBank and Erste can be cut | Plan (user) |
| Integrity failure | Reject with 422 and a PII-free message | Same policy as VeloBank; a misread row would skew totals unnoticed | Research / VeloBank plan |
| mBank checks | Opening-balance chain, closing balance, turnover summary counts and sums | The statement carries three independent figures | Research |
| Erste check | Order-independent balance linkage (multiset), skipped if a non-PLN row exists | Same-day row order does not follow the balance | Research |
| Dates | mBank booking date (as CSV); Erste operation date (only date present) | Matches the CSV where possible | Research |
| Descriptions | All cell lines joined with single spaces | Keeps the operation type for categorization; hash compatibility is not attempted | Plan |
| Erste recognition | Composite text anchor plus a table-header structure check | The extracted text never names the bank | Plan |
| Hash / normalization | `DedupHash` untouched, no rehash migration | Compatibility cannot be verified without a CSV/PDF pair for one period | Plan |

## Scope

**In scope:** shared PDF helper; `ImportBatch.SourceFormat` with backfill migration, overlap warning (API and banner); `MBankPdfParser` and `ErstePdfParser` with synthetic generators and fixtures; PRD FR-018 and roadmap update; GitHub issue sync.

**Out of scope:** description normalization or rehash; blocking mixed imports; Revolut, Excel, OCR, LLM extraction; foreign-currency conversion; account-number or holder checks; special messages for encrypted or corrupt PDFs.

## Architecture / Approach

The existing endpoint already picks parsers by sniffed format, so each new parser registers as a PDF parser of its bank and `GET /accounts/banks` is unaffected. Each parser sits on the shared helper (open behind one catch-all, word/shape model, cell joining, `pl-PL` numbers) and owns its row anchoring: mBank by date-led lines between stroked grid lines, Erste by separator bands like VeloBank. The parse response gains `Format` and `MixedFormatOverlapCount`; the frontend echoes the format on commit so the batch records it.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. PRD and roadmap | FR-018 extended, S-12/S-13 merged, issues synced | Issue edits are external; asked before acting |
| 2. Shared PDF helper | Generic PDF mechanics extracted, VeloBank on it | Regressing a verified parser; existing tests plus real samples guard it |
| 3. Source format and warning | Migration, API fields, banner | Boundary semantics of "overlap"; pinned by tests |
| 4. mBank fixtures | Generator and two synthetic PDFs | Generator tuned to a builder artifact instead of the real layout |
| 5. mBank parser and wiring | `MBankPdfParser`, checks, DI, real-sample check | PdfPig text extraction of mBank's fonts (diacritics) |
| 6. Erste fixtures | Generator and two synthetic PDFs | Matching Chromium's geometry from one sample |
| 7. Erste parser and wiring | `ErstePdfParser`, linkage check, copy | Weak recognition anchor; check misses a lost oldest/newest row |

**Prerequisites:** `pdf-statement-import` merged (it is); real samples at `D:\repos\michLukaszewicz\Przykładowe pliki\{mbank,erste}\`; user approval for GitHub issue changes and any font download.
**Estimated effort:** about 7 sessions across 7 phases; Phases 6-7 can be dropped without affecting mBank.

## Open Risks & Assumptions

- The PDFs may show diacritics differently through PdfPig than the `pdftotext` check suggests; the plan makes anchor words and month tokens tolerant and starts parser work with a word dump.
- Erste's linkage check cannot see a missing newest or oldest row, and Erste PDF rows collide more (few distinct descriptions); both are accepted and tested as known behaviour.
- No foreign-currency row exists in either sample, so the PLN-only handling is tested synthetically only.
- mBank card-row row splitting across pages is not observed in the 2-page sample; manual verification on the real file confirms the 32 rows.

## Success Criteria (Summary)

- Real `mbank.pdf` imports as 32 rows matching the statement's own totals (7 / 25 / 32); real `erste.pdf` imports as 55 rows; re-uploading either flags every row as a duplicate.
- A tampered statement is rejected with an explanation; an overlapping other-format import shows the banner with a count.
- `dotnet test`, `npm run typecheck` and `npm run build` pass, VeloBank behaviour is unchanged, and no real personal data enters the repository.
