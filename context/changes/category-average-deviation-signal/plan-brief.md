# Category Average-Deviation Signal — Plan Brief

> Full plan: `context/changes/category-average-deviation-signal/plan.md`

## What & Why

Show, per category, whether this month's spend is above, below or in line with the user's own historical average (FR-013) — the product's "is this normal for me" differentiator that works without any budget. The signal appears once a category has at least 1 prior month of history; before that, only the amount is shown.

## Starting Point

The S-04 spend donut exists (`GET /api/dashboard/category-spend`, current month only, amounts visible only on hover). Nothing aggregates past months, and the frontend has no visible per-category list.

## Desired End State

Below the spend donut is a list of categories (colour dot, name, amount). Categories with prior-month history carry an "Above average" / "Below average" / "In line" badge plus the pace-adjusted average it was compared to; others show no badge. Clicking a row filters transactions like clicking its slice. The income donut is unchanged.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| -------- | ------ | ---------------- | ------ |
| Current vs average | Pace-adjusted: spend through today's day-of-month vs past months' spend through that same day | A raw comparison against full-month averages would read "below" for almost everything early in the month | Plan |
| "In line" band | ±10% of the average (inclusive); above if > 1.1×, below if < 0.9× | Simple and sensitive; chosen over ±20% and std-deviation | Plan |
| Average window | All calendar months from the category's first-ever spend to last month, zero months counted | Reflects true typical monthly spend including skipped months; N = 0 → no signal | Plan |
| Granularity | Month only (no week signal) | Keeps the slice small; FR-013's week option not needed for the MVP | Plan |
| UI placement | Visible category list below the spend donut (the two donuts share a row, so not literally beside), Legend kept | Signal must be visible without hover and leaves room for S-05 budget figures | Plan |
| Where logic lives | Backend calculator with injectable `TimeProvider`; frontend only renders | Keeps logic testable in the existing xUnit setup | Plan |
| Testing | Backend unit + endpoint tests; frontend typecheck + manual | No frontend test framework exists; not worth adding for one component | Plan |
| Income donut | No signal | FR-013 is about spend per category | Plan |

## Scope

**In scope:** pace-adjusted deviation calculation; extended spend endpoint DTO; per-category list with badge on the spend donut; backend tests.

**Out of scope:** budgets (S-05), income signal, week signal, month picker, std-deviation thresholds, frontend test framework, per-user timezone.

## Architecture / Approach

The spend endpoint loads earlier-month spend rows with the same exclusions as the donut (categorized, non-transfer, negative amounts, same user), and a pure `CategoryDeviation` calculator returns `(averageToDate, deviation)` per category or null if there is no prior-month history. `TimeProvider` is registered in DI so tests can fix "today". The frontend extends its DTO type with two optional fields and renders a clickable list with the badge.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| ----- | ---------------- | -------- |
| 1. Backend deviation signal | Calculator, extended spend DTO/endpoint, DI clock, tests | Off-by-one at month/day boundaries; history query correctness |
| 2. Frontend category list with badge | Visible list with badges and click-to-filter parity | Duplicated information next to the existing Legend |

**Prerequisites:** S-04 merged (done).
**Estimated effort:** ~2 sessions across 2 phases.

## Open Risks & Assumptions

- Full end-to-end verification needs real data spanning more than 1 month (roadmap note); manual testing relies on backdated manual transactions.
- Future-dated current-month transactions count toward the displayed amount but not the comparison.
- Accepted: a category whose only history is a partial month (late first spend, mid-month import) reads "Above average" for any spend this month, since its average-to-date is 0.
- A category whose prior history is only in income-signed or transfer rows has no spend history, so it gets no signal.

## Success Criteria (Summary)

- A category with prior-month spend shows a correct above/below/in-line badge against the pace-adjusted average.
- A category whose first spend is this month shows no badge.
- Existing donut behaviour (click-to-filter, income chart, empty states) is unchanged; `dotnet test` and `npm run typecheck` pass.
