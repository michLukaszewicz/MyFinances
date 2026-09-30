---
change_id: categorize-queue-only
title: Categorize page lists only uncategorized transactions
status: archived
created: 2026-09-30
updated: 2026-09-30
archived_at: 2026-09-30T00:00:00Z
---

## Notes

Follow-up to S-03 (`categorization-queue`), requested by the user after manual use of the page.

**Decision:** the `/categorize` page shows only transactions that still need a category: the first queue item as the "Up next" form, the rest as a read-only "Remaining" list. When the queue is empty the page shows only the placeholder "Yay, all done!". The previous "Handled" list (already-categorized transactions with inline edit controls) was removed from this page.

**Why:** mixing already-categorized transactions into the categorization page made it unclear what still needed work; the page's purpose is to clear the queue.

**Consequences:**
- FR-015 (re-categorize any transaction at any time) is no longer served by `/categorize`; changing a transaction's **category** remains available through edit on the dashboard transaction list (`home.tsx`).
- **Known gap:** the internal-transfer flag (FR-009 override) of an already-handled transaction can no longer be toggled from any UI — the dashboard edit form has no such control. Tracked as a follow-up (see GitHub issue linked from roadmap S-03).
- Frontend-only change ([categorize.tsx](../../../MyFinances/frontend/app/routes/categorize.tsx)). The backend `GET /api/categorization/handled` endpoint is unchanged but no longer called by the UI.
- No PRD change; FR-007 and FR-015 still hold.

**Verification:** `npm run typecheck` passes; page checked manually in the browser.
