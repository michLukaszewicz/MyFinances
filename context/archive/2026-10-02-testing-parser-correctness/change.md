---
change_id: testing-parser-correctness
title: Parser correctness tests (rollout Phase 2)
status: archived
created: 2026-10-02
updated: 2026-10-02
archived_at: 2026-10-02T11:19:41Z
---

## Notes

Open a change folder for rollout Phase 2 of context/foundation/test-plan.md: "Parser correctness". Risks covered: #2. Test types planned: unit with fixtures. Risk response intent: #2 — a fixture row must yield exactly the amount and date printed in the source, regardless of the machine's culture; a statement with inconsistent balances must be rejected, not imported. Challenge "passes on the fixture so it parses correctly" and "the balance check catches every misread". Avoid expected values produced by the parser itself, fixtures generated from parser output, and asserting only row count. After creating the folder, follow the downstream continuation rule.
