# Category Spend Donut Chart (S-04) — Plan Brief

> Full plan: `context/changes/category-spend-donut-chart/plan.md`
> Research: `context/changes/category-spend-donut-chart/research.md`

## What & Why

Add a donut chart to the dashboard showing spend share per category for the current month, excluding uncategorized and internal-transfer transactions — the visualization the PRD's Primary Success Criterion is built around. Clicking a slice filters the existing transaction list to that category, satisfying FR-011's category filter with a single chart-native control instead of a separate dropdown.

## Starting Point

`Transaction.CategoryId`/`IsInternalTransfer` (S-03) and the dashboard transaction list (S-09) already exist, but no aggregation endpoint, current-month date logic, or filter UI exist anywhere yet. The dashboard (`home.tsx`) fetches an unfiltered, paginated transaction list via `GET /api/transactions`; no chart library is installed. Amount is confirmed negative-for-spend from mBank imports.

## Desired End State

A logged-in user opens the dashboard and sees a donut chart of this month's spend by category above their transaction list. Clicking a slice narrows the list below to that category/month; clicking again (or a "Clear" affordance) restores the full list. With no categorized spend yet this month, the chart area shows a message linking to the categorization queue instead of an empty chart.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Aggregation location | New backend endpoint (`GET /api/dashboard/category-spend`) | Matches the existing per-feature endpoint architecture and keeps sign/exclusion logic tested in one place, avoiding client-side pagination through all transactions | Plan |
| Filter interaction | Click a donut slice to filter the transaction list | Satisfies FR-011 with one chart-native control instead of a separate dropdown; matches the roadmap's "chart ... filterable by category" wording | Plan |
| Filter state | Local `useState` on the dashboard route | Matches the only pattern this codebase uses for equivalent toggles — no `useSearchParams` usage exists anywhere yet | Plan |
| Slice colors | Fixed array indexed by category position (auto-assigned, not per-category-name mapping) | Categories are fixed/seeded (12, not user-editable), so a small palette array is cheap and stable without per-ID curation | Plan |
| Chart placement | New section inside existing `home.tsx`, not a separate route | PRD requires the chart and "how much did I spend" list in the same view | Plan |
| Empty state | Muted message + link to `/categorize` | Matches the existing empty-state visual convention and points at the likely cause | Plan |
| Amount sign convention | Negative = spend (confirmed via mBank parser tests) | No normalization exists elsewhere in the codebase; this is the ground truth to aggregate against | Research |
| Chart library | Recharts v3.3.0 | Already locked in by the tech-stack hand-off; `Pie`+`innerRadius` gives the donut, `Cell` is deprecated in v3 in favor of a `fill` field per data item | Research |

## Scope

**In scope:**
- `GET /api/dashboard/category-spend` aggregation endpoint (current month, excludes uncategorized/internal-transfer, positive amounts)
- Extending `GET /api/transactions` with optional `categoryId`/`currentMonth` filters
- `CategorySpendDonut` frontend component + Recharts dependency
- Click-to-filter wiring between the chart and the existing transaction list

**Out of scope:**
- Budget-vs-actual (FR-017/S-05) and historical-average deviation (FR-013/S-06)
- Any date range other than the current month (no month picker, no arbitrary range — parked FR-016)
- Per-user timezone handling for the month boundary (server UTC only)
- A separate category-filter dropdown
- Changes to how manually-entered transactions store amount sign

## Architecture / Approach

Backend computes the aggregation server-side (`GroupBy`+`Sum` over `Transaction`, filtered to categorized/non-transfer/current-month/negative-amount), following the existing `Categorization`/`Transactions` two-file endpoint convention. A shared `CurrentMonthRange` helper keeps the new endpoint and the extended `/transactions` filter in agreement on month boundaries. The frontend adds a self-contained `CategorySpendDonut` component (own fetch/loading/error/empty state) that reports slice selection up to `home.tsx` via a callback; `home.tsx` re-fetches the transaction list with the filter applied when a category is selected.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend | Aggregation endpoint + transaction-list filter extension, fully tested | Getting the current-month boundary and exclusion filter exactly right, since it drives the PRD's primary success criterion |
| 2. Frontend | Recharts-based donut chart wired into the dashboard with click-to-filter | First chart/dependency in the app — new interaction pattern (slice click) and dark-theme color contrast |

**Prerequisites:** S-03 (categorization queue) and S-09 (transaction history view), both already `done`.
**Estimated effort:** ~2 sessions across 2 phases (one backend, one frontend).

## Open Risks & Assumptions

- Month boundary uses server UTC time, not the user's local timezone — could be off by a few hours right at month edges; acceptable for this single-user personal tool per PRD's NFRs.
- Manually-entered (S-02) transactions were not independently re-verified for the same negative-for-spend sign convention in this research pass — worth a quick sanity check during implementation if manual entries look off in the chart.

## Success Criteria (Summary)

- The donut chart accurately reflects current-month, categorized, non-transfer spend per category.
- Clicking a slice filters the transaction list to that category/month and can be cleared.
- Users with no categorized spend yet this month see a clear next step (link to `/categorize`) instead of a blank chart.
