---
change_id: mbank-single-page-pdf
title: mBank PDF import must handle single-page statements
status: new
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Spawned from testing-import-integrity-dedup (Phase 3): a single-page mBank PDF appeared to parse to zero rows with no error (MBankPdfParser.FindColumnEdges). Verify against a real single-page export supplied by the user, fix the parser (or reject with a clear error), and add regression tests. The real sample file stays outside the repo; tests use synthetic fixtures.

## Outcome (2026-10-02)

- Verified against the user's real single-page mBank export (kept outside the repo): `CanParse` is true and `MBankPdfParser.Parse` returns all rows, the balance/summary integrity checks pass. No production bug.
- Root cause of the original suspicion: `MBankPdfBuilder` drew neighbouring rows with one shared border, so on a single page every grid line had 10 segments; the real export draws each row as its own cell with a ~0.5 pt gap (one run of 5 segments per line).
- Fix: `MBankPdfLayout.RowGap` in the test builder (default 0, existing fixtures unchanged). `MBankPairedStatement` now uses a single page with `RowGap = 0.5`.
- Tests added: `MBankPdfParserTests.Parse_ReadsAStatementThatFitsOnOnePage` (1, 4, 8 rows) and `ImportEndpointsTests.Parse_MBankSinglePagePdf_ReturnsAllRows`. Break-check: widening the expected segment count in `FindColumnEdges` turns all four red.
- Not done: the parser still reads 0 rows for a hypothetical single-page PDF whose adjacent rows share borders (builder default, RowGap = 0). Not seen in any real export; harden `FindColumnEdges` (deduplicate coincident segments) only if one appears.
