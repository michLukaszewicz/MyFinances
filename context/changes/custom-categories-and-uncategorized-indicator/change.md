---
change_id: custom-categories-and-uncategorized-indicator
title: Custom categories (create/delete) and an uncategorized-transactions indicator
status: implementing
created: 2026-10-09
updated: 2026-10-09
archived_at: null
---

## Notes

1. Users can create their own categories and delete them. Deleting a category that is used on transactions warns the user and asks for consent; on confirm, those transactions become uncategorized and reappear in the categorization queue.
2. A dot/star indicator on the Categorize nav tab and a toast notification, both shown only while uncategorized transactions exist.

## Decisions (2026-10-09)

- Every category belongs to a user (`Category.UserId`, required). The 14 defaults are only a starter set (`DefaultCategories`) copied to a user at registration; afterwards they are ordinary categories the user can delete. Migration `PerUserCategories` copied the starter set to every existing user, re-pointed their transactions and removed the shared rows (applied to the dev Neon database on 2026-10-09; `Down` does not restore the transaction re-pointing).
- `Category.ColorSlot` is a stable chart palette slot (lowest slot not used by the user, at creation), so chart colours no longer depend on list position and survive adding/deleting categories; `chartColor` generates golden-angle hues beyond the 12 token slots.
- `DELETE /api/categorization/categories/{id}` answers 409 with `transactionCount` when the category is in use; `?uncategorizeTransactions=true` sets those transactions back to uncategorized (they reappear in the queue) and deletes the category (same pattern as account delete). Category management lives on the Categorize page.
- `GET /api/categorization/queue/count` feeds the dot on the Categorize tab and a toast (sonner via shadcn); the toast is announced on page load and when the count grows, never while on /categorize.
- Header logo no longer shrinks in a narrow window (`shrink-0`), and the header wraps instead of overflowing.
