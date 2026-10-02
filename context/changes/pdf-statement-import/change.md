---
change_id: pdf-statement-import
title: Import bank statements from PDF (VeloBank, mBank, Erste)
status: implemented
created: 2026-10-01
updated: 2026-10-02
archived_at: null
---

## Notes

Add PDF statement import as a second input format next to CSV, for all three banks: VeloBank (new bank, PDF only — the bank offers no CSV export), mBank and Erste (CSV already supported; PDF as an additional format).

VeloBank replaces Revolut as the next bank to add: there is no Revolut account with transactions to export a real sample from, whereas the VeloBank account is active and a real PDF sample exists. Revolut (S-07) stays `proposed` and untouched by this change.

See `research.md` for the PDF sample analysis, the evaluated options (PdfPig vs. LLM extraction vs. converters) and the per-bank scope points.

Scope decided in `/10x-plan` (2026-10-01): this change covers the PDF groundwork and VeloBank only. The mBank and Erste PDF parsers stay optional follow-ups, recorded as proposed roadmap slices S-12 and S-13 (see `plan.md`).
