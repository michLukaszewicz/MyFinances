# Category Trend Line Chart — Plan Brief

> Full plan: `context/changes/category-trend-line-chart/plan.md`

## What & Why

A dashboard line chart showing, per category, the summed spend (or income) over time, bucketed by week, month or year, with one line per selected category. It replaces the dropped S-05 (budget-vs-actual) with better data exploration.

## Starting Point

Nothing aggregates amounts over time today; the dashboard only has two single-period donuts. Counting rules (categorized, no internal transfers, sign by kind, Warsaw "today") already exist and recharts is already a dependency. A parallel change (`chart-period-selector`) is editing `DashboardEndpoints.cs` and `home.tsx`.

## Desired End State

A full-width "Category trend" card under the donuts. First open: last 12 months of spend, top 5 categories ticked. The user can switch granularity, range and spend/income, and tick any categories; colours stay stable per category.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Time range | Own from/to control, independent of the donut selector | Avoids a dependency on the unmerged change and allows long trends. |
| Defaults | 12 weeks / 12 months / all years (max 5) | A readable trend on first open. |
| Weeks and edges | ISO weeks (Mon–Sun), edge buckets clipped to the range, empty buckets zero | Conventional for Poland; totals equal transactions in range. |
| Categories | Multi-select, top 5 by total ticked by default | Useful on first open and fully user-controlled. |
| Data kind | Spend/income toggle, same exclusions as the donuts | Mirrors the existing charts without mixing signs. |
| Placement and caps | Dashboard card; API rejects future end and more than 120 buckets | Protects the query and chart; current unfinished bucket allowed. |
| Testing | Backend xUnit with `FixedTimeProvider` + manual UI | No frontend test tooling in a time-boxed MVP. |

## Scope

**In scope:** `GET /api/dashboard/category-trend`, bucketing and validation, backend tests, the `CategoryTrendChart` component and its dashboard placement.

**Out of scope:** reuse of the shared period selector, spend+income on one axis, point drilldown, export, frontend test framework, persistence of selections.

## Architecture / Approach

One new endpoint in its own file returns ordered buckets and zero-filled series for every category with data; the frontend chooses the default top 5, handles selection and colours. Edits to `DashboardEndpoints.cs`/`home.tsx` are limited to endpoint registration and one component insertion to avoid conflicts.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Trend endpoint | Bucketed aggregation, validation, tests | Week/year boundary and edge-clipping bugs. |
| 2. Line chart UI | Component, controls, dashboard placement | Many lines unreadable; merge friction in `home.tsx`. |

**Prerequisites:** None beyond the merged donut slice.
**Estimated effort:** ~2 sessions across 2 phases.

## Open Risks & Assumptions

- Partial first/last buckets can look low (clipping is intentional).
- Default top 5 is computed per range/kind; changing range resets the selection.
- Two period-like controls on one dashboard may confuse; revisit after both changes land.

## Success Criteria (Summary)

- The chart shows correct per-bucket sums that match the underlying transactions.
- Granularity, range, kind and category selection work, including blocked invalid ranges.
- Existing dashboard behaviour is unchanged and tests pass.
