---
change_id: categorization-queue
title: Categorization queue (assign category per transaction, auto-flagged internal transfers)
status: implementing
created: 2026-09-25
updated: 2026-09-25
archived_at: null
---

## Notes

Roadmap slice S-03. Implements FR-007 (manual category selection, no auto-suggestion),
FR-009 (internal-transfer auto-flagging with manual override), and FR-015 (re-categorize
any transaction at any time). Prerequisites S-01 (mbank-import-with-dedup) and S-10
(account-management) are both merged into `main` as of this plan (S-10's `Account` entity
was purpose-built as "the 'known accounts' input to S-03's transfer-detection heuristic" —
see `MyFinances/backend/Transactions/Account.cs:4-5`).

This is a from-scratch replan; two earlier planning attempts on other worktrees/branches are
not referenced or reconciled here.

Deliberately avoids touching `MyFinances/backend/Transactions/TransactionContracts.cs` /
`TransactionEndpoints.cs` and `MyFinances/frontend/app/routes/home.tsx` — those are owned by
the in-flight sibling change S-09 (transaction-history-view, branch
`claude/10x-transaction-history-view-02cb0c`), which does not exist in this worktree yet.
S-02 (manual-transaction-entry) already collided with S-09 by recreating those same two
backend files; this plan uses its own backend module (`Categorization/`) and its own frontend
route (`categorize.tsx`) instead.
