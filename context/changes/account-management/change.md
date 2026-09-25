---
change_id: account-management
title: User-managed bank accounts (add/edit own accounts, used across manual entry and transfer detection)
status: implemented
created: 2026-09-25
updated: 2026-09-25
archived_at: null
---

## Notes

Deferred out of S-02 (manual-transaction-entry) during its `/10x-plan`: the user requested a
settings page to add bank accounts (account number + bank name), then pick from those accounts
when manually entering a transaction, instead of S-02's minimal bank-name dropdown.

This overlaps with FR-009's "user's own known accounts" concept, which S-03
(categorization-queue, not yet planned) needs for its internal-transfer detection heuristic.
Not currently tracked in `context/foundation/roadmap.md` — see roadmap update below before
running `/10x-plan`.
