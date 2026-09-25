---
change_id: import-account-linking
title: Link imported transactions to a specific account instead of a free-text bank string
status: implemented
created: 2026-09-25
updated: 2026-09-25
archived_at: null
---

## Notes

On the import transactions page (S-01, `mbank-import-with-dedup`) the user needs to pick which of their accounts (from `account-management`) the imported transactions belong to. Today `Transaction.Bank` is a plain string set by the parser; it should become a foreign key to `Account` instead, so a transaction is tied to a real user account (id), not a bank-name string. Touches both the import parse/commit endpoints and frontend import flow, plus an EF Core migration + backfill for existing `Transaction` rows.
