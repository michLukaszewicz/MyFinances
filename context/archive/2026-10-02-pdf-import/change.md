---
change_id: pdf-import
title: PDF statement import for mBank and Erste Bank Polska
status: archived
created: 2026-10-02
updated: 2026-10-02
archived_at: 2026-10-02T07:29:31Z
---

## Notes

Follow-up to `pdf-statement-import` (VeloBank PDF, implemented): adds PDF as a second input format for mBank and Erste Bank Polska, planned together in one change. Replaces the roadmap's optional slices S-12 (`mbank-pdf-import`) and S-13 (`erste-pdf-import`), which are merged into one item with this Change ID.

Decided in `/10x-plan` (2026-10-02): mixing CSV and PDF imports on one account is handled by recording the source format on each import batch and warning on overlap (no hash or description normalization); a thin shared PDF helper is extracted from `VeloBankPdfParser`; mBank lands before Erste so Erste can be cut without losing mBank.

Research: `context/changes/pdf-statement-import/research.md` (PDF sample analysis for all three banks; the real mBank and Erste samples it describes are the ones used here). Design pattern: `context/changes/pdf-statement-import/plan.md`.
