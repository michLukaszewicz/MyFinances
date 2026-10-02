---
change_id: testing-import-integrity-dedup
title: Import integrity and dedup test rollout (Phase 1)
status: preparing
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Open a change folder for rollout Phase 1 of context/foundation/test-plan.md: "Import integrity and dedup". Risks covered: #1, #4. Test types planned: integration. Risk response intent: #1 - a second import of the same transactions leaves totals unchanged and every detected duplicate is surfaced to the user for a decision; #4 - importing a PDF after a CSV of the same period on the same account produces the overlap warning and does not inflate totals. After creating the folder, follow the downstream continuation rule.
