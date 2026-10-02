---
change_id: mutation-testing-stryker
title: Add Stryker.NET mutation testing with incremental runs
status: implementing
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Phase 1 finding (2026-10-02): Stryker 5.0.0 has no `--test-case-filter` CLI flag; it works only via `stryker-config.json`. With it an Import-scoped run used 423 of 577 tests, but total time did not drop (~3:00 vs ~2:39) because build, mutant generation and coverage capture dominate. Not adopted. Phase 4 added: fix weak existing tests (all 4 mutants in `Import/DedupHash.cs` survived).

Add Stryker.NET (local dotnet tool) to MyFinances/backend, scoped to Import/** parsers, with incremental mode (--since) so only changed files are mutated; plus decide trigger (PR/CI vs git hook).
