---
change_id: manual-transaction-entry
title: Manual transaction add/edit/delete with dedup-hash parity (S-02)
status: implementing
created: 2026-09-25
updated: 2026-09-28
archived_at: null
---

## Notes

Roadmap slice S-02 (`context/foundation/roadmap.md`). Delivers FR-010: user can manually add,
edit, and delete transactions; a manually-entered transaction is recognized as the same one if
it later appears in an imported statement, via the existing S-01 dedup hash
(`MyFinances/backend/Import/DedupHash.cs`). Consumes S-10's account picker
(`GET /accounts`, already implemented) instead of the free-text bank dropdown originally
deferred during S-10's own planning.
