# Chart Period Selector — Plan Brief

> Full plan: `context/changes/chart-period-selector/plan.md`

## What & Why

The spend and income donuts on the dashboard only ever show the current month. We add one shared period selector (last 30 days, last 90 days, selected month, custom range) that drives both donuts, the click-to-filter transaction list and the average-deviation badge. It replaces the dropped S-05 (budget-vs-actual) with better data exploration.

## Starting Point

Chart endpoints take no parameters and compute the Warsaw current month themselves; `/api/transactions` only knows a `currentMonth` flag (on the system clock, not the injected one). The deviation calculator assumes the displayed month contains "today". The frontend has no test framework.

## Desired End State

A selector above the donuts, defaulting to the current month on each load. Picking any period refetches both donuts, clears slice selection, and the list after a slice click shows exactly that period's rows so they reconcile with the slice. The deviation badge works for every period.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Selector scope | One shared selector for both donuts | One unambiguous period for the shared transaction list. |
| Default / persistence | Current month, not persisted | Matches today's behaviour and the local-state pattern. |
| Wire format | Inclusive `from`/`to` dates; `currentMonth` removed | One mechanism for presets and custom ranges; only one caller. |
| List drilldown | Same period as charts | Slice amount and listed rows always reconcile. |
| Deviation signal | Generalised: monthly logic for full months, preceding equal windows otherwise | User chose a signal for every period; current-month behaviour stays unchanged. |
| Future dates | Rejected, except the current calendar month | Strict choice, with the exception keeping existing future-dated current-month rows. |
| Testing | Backend xUnit with `FixedTimeProvider` + manual UI | No frontend test tooling in a time-boxed MVP. |

## Scope

**In scope:** period parameter on both donut endpoints and the list; clock injection for the list; generalised deviation; shared selector UI; period-aware titles/copy.

**Out of scope:** per-chart selectors, persistence/URL state, frontend test framework, changes to exclusion rules, per-user timezone, the trend line chart.

## Architecture / Approach

Frontend turns each preset into inclusive `from`/`to` dates; the backend validates them in one helper against the injected Warsaw "today" and uses the range for the amount queries, the list filter and the deviation history (`Date < from`). A period equal to one full calendar month uses monthly deviation logic; any other period compares its total with the average of preceding windows of the same length.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Period on endpoints | `from`/`to` on donuts and list, validation, tests | Removing `currentMonth` breaks the UI list filter until phase 3. |
| 2. Deviation for any period | Month and window modes, tests | Misleading baselines near the start of history. |
| 3. Selector UI | Shared selector, donut and list wiring, copy | Timezone edge: browser-local dates vs server Warsaw "today". |

**Prerequisites:** None beyond the merged donut and deviation slices.
**Estimated effort:** ~3 sessions across 3 phases; phases 1 and 3 should land in the same PR.

## Open Risks & Assumptions

- Browser-local "today" can differ from the server's Warsaw "today" shortly around midnight; the server's validation may reject a just-valid range.
- Windows for non-month periods are back-aligned from the period start; a partial first window lowers the average and can read "above".
- Assumes future-dated rows in the current month remain counted in the donut amount, as today.

## Success Criteria (Summary)

- Both donuts and the slice-click list respect the chosen period and reconcile.
- Default behaviour (current month) is unchanged and existing tests still pass.
- Deviation badge shows sensible results for months and rolling windows.
