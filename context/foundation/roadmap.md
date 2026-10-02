---
project: "MyFinances"
version: 1
status: draft
created: 2026-09-22
updated: 2026-10-02
prd_version: 3
main_goal: market-feedback
top_blocker: time
milestone_id: mvp-spend-insight-loop
milestone_seq: 1
milestone_status: done
---

# Roadmap: MyFinances

> Derived from `context/foundation/prd.md` (v3) + `context/foundation/shape-notes.md`'s `## Forward: technical-roadmap` notes + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-1: Full MVP spend-insight loop (mBank, Erste, VeloBank)** — Status: done

- **Intent:** Deliver the entire MVP scope in `prd.md` v3 — the full import → dedup → categorize → chart → budget/average-deviation loop, across all four banks (VeloBank via PDF statements, the others via CSV) — as one outcome-scoped milestone. The PRD carries no staged "Etap" split (that framing lived only in the earlier shape-notes draft), so all must-have FRs belong to this single milestone. S-12 (mBank and Erste PDF import) is an optional follow-up to S-11 and sits outside this milestone's done criterion.
- **Source materials:** `context/foundation/prd.md` (v3), supplemented by `context/foundation/shape-notes.md`'s `## Forward: technical-roadmap` notes (parser architecture, dedup-hash shape, CSV formats to verify).
- **Done when:** every F-NN and S-NN below is `done`, except the optional S-12.

## Vision recap

An individual manages personal finances across several bank accounts (mBank, Revolut, Erste Bank Polska, VeloBank) and has no visibility into what they actually spend, per category, without manual spreadsheet work or trusting a bank's own shallow categorization. The product's core bet — its **wedge**, the one trait that, if removed, makes it just another expense tracker — is that categorization quality comes from the user's own deliberate, correctable decisions rather than a black-box auto-categorizer, and spend gets compared against the user's own historical average so "is this normal for me" works without ever requiring a budget to be set up front.

## North star

**S-01: User imports an mBank CSV statement and resolves any detected duplicates** — the riskiest unknown in this MVP is whether real bank export data can be reliably parsed and deduplicated; everything downstream (categorization, charts, budgets) is only as trustworthy as this step, so it's validated first and placed as early as its prerequisites allow.

> A reader-facing gloss: the **north star** here is the smallest end-to-end slice whose successful delivery proves the riskiest part of the core hypothesis — placed early because every later slice only matters if this one holds up.

## At a glance

| ID    | Change ID                       | Outcome (user can …)                                                              | Prerequisites | PRD refs                    | Status   |
| ----- | -------------------------------- | ----------------------------------------------------------------------------------- | -------------- | ---------------------------- | -------- |
| F-01  | minimal-auth-scaffold            | (foundation) register, log in, and stay logged in via a persistent session          | —              | FR-001, Access Control        | done |
| S-01  | mbank-import-with-dedup          | import an mBank CSV, resolve flagged duplicates, and see an import summary          | F-01           | FR-002, FR-004, US-01, Guardrail (dedup) | done |
| S-02  | manual-transaction-entry         | manually add, edit, and delete transactions without creating import duplicates      | S-01           | FR-010                        | done |
| S-03  | categorization-queue             | categorize queued transactions, with internal transfers auto-flagged (overridable)  | S-01, S-10     | FR-007, FR-009, FR-015, US-01 | done |
| S-04  | category-spend-donut-chart       | see a donut chart of category spend for the current month, filterable by category   | S-03           | FR-011, FR-012, US-01         | done |
| S-06  | category-average-deviation-signal| see a category's spend flagged as above/below/in line with its historical average   | S-04           | FR-013                        | done |
| S-08  | erste-import                     | import an Erste Bank Polska CSV statement through the same loop                     | S-01           | FR-003                        | done |
| S-09  | transaction-history-view         | see a chronological list of their imported/manually-entered transactions on the dashboard (date, description, amount, category) | S-01           | FR-011 (partial)              | done |
| S-10  | account-management                | add/edit/remove their own bank accounts (account number + bank name) via a settings page, and pick from them when manually entering a transaction | F-01           | FR-009, FR-010                | done |
| S-11  | pdf-statement-import              | import a VeloBank PDF statement through the same import/dedup/categorize/chart loop | S-01           | FR-018                        | done |
| S-12  | pdf-import                        | (optional) import an mBank or Erste Bank Polska PDF statement as a second format next to the CSV | S-11 | FR-018                        | done |
| S-14  | chart-period-selector             | pick the period (last 30/90 days, a month, a custom range) shown by both donut charts, the slice drilldown list and the deviation badge | S-04, S-06 | FR-011, FR-012, FR-013, FR-016 | done |
| S-15  | category-trend-line-chart         | see a line chart of per-category spend/income over time, bucketed by week, month or year | S-04          | FR-011 (data exploration)     | done |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                          | Chain                                          | Note                                                                 |
| ------ | ------------------------------- | ----------------------------------------------- | --------------------------------------------------------------------- |
| A      | Core spend-insight loop         | `F-01` → `S-01` → `S-03` → `S-04` → `S-06` | The main validation path for `main_goal: market-feedback` — auth, import, categorize, chart, then the average-deviation signal. |
| B      | Manual transaction entry        | `S-02`                                          | Joins Stream A at `S-01` — independent of categorization/chart work, can run in parallel. |
| C      | Bank coverage expansion         | `S-07` → `S-08` → `S-11` (→ `S-12` optional) | Joins Stream A at `S-01` — Revolut/Erste import, then VeloBank PDF import (`S-11`, which replaces Revolut as the next bank to add — no Revolut sample available). `S-08` no longer waits for `S-07` — the PRD FR-003 sequencing gate was waived by the user on 2026-10-01. `S-12` adds the PDF format for mBank and Erste and is optional. |
| D      | Transaction visibility           | `S-09`                                          | Joins Stream A at `S-01` — closes the "no history view" gap flagged during S-01 manual testing (2026-09-25); independent of categorization/chart work, can run in parallel. |
| F      | Data exploration                | `S-14`, `S-15`                                  | Joins Stream A at `S-04`/`S-06` — added 2026-10-02 in place of the dropped S-05 (budget-vs-actual); the two slices are independent (the line chart has its own range control) and can run in parallel. |
| E      | Account management              | `S-10`                                          | Joins Stream A at `S-03` — S-03's internal-transfer heuristic (FR-009) consumes S-10's account list; only needs F-01, so it can be built in parallel with S-01/S-02/S-07/S-09. |

## Baseline

What's already in place in the codebase as of `2026-09-22` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — React Router scaffold present, default welcome page (`MyFinances/frontend/app/welcome/welcome.tsx`), no domain UI yet.
- **Backend / API:** partial — ASP.NET Core minimal API scaffolded (`MyFinances/backend/Program.cs`), still serving the default `WeatherForecast` endpoint under `/api`.
- **Data:** partial — EF Core + Npgsql wired to Neon Postgres (`MyFinances/backend/AppDbContext.cs`), context is an explicit placeholder with a comment noting domain models arrive with real feature work.
- **Auth:** absent — no `Microsoft.AspNetCore.Identity`/JWT packages in `MyFinances.Api.csproj`, no auth code in `Program.cs`.
- **Deploy / infra:** present — `MyFinances/backend/Dockerfile` + `render.yaml` wired, first deployment to Render (with Neon Postgres) already recorded in git history.
- **Observability:** absent — no logging/monitoring beyond ASP.NET Core defaults; no PRD NFR currently requires it.

## Foundations

### F-01: Minimal auth scaffold

- **Outcome:** (foundation) user can register and log in with email/password; the session persists across visits; every API endpoint requires auth and scopes data to the logged-in user.
- **Change ID:** minimal-auth-scaffold
- **PRD refs:** FR-001, Access Control (flat user model, data scoped per user), NFR (financial data never visible to another user)
- **Unlocks:** S-01, S-02, S-03, S-04, S-06, S-08 — every vertical slice persists or reads user-scoped data, so none can be built safely before this lands.
- **Prerequisites:** — (Baseline: backend/frontend scaffolds present; auth is the one absent layer blocking everything else.)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced first because retrofitting user-scoping onto transactions/categories built without it would mean redoing data-access code across every later slice.
- **Status:** done

## Slices

### S-01: mBank import with duplicate resolution

- **Outcome:** user can import an mBank CSV export, see any detected duplicate shown side-by-side (existing vs. incoming) to decide skip/keep, and see an import summary (imported count / skipped-duplicate count).
- **Change ID:** mbank-import-with-dedup
- **PRD refs:** FR-002, FR-004, US-01 (Given/When), Guardrail (duplicates never silently double-counted)
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Exact mBank CSV export format (columns, encoding, separator) from "Płatności → Historia → Zestawienie operacji" — Owner: user. Block: no (verify during `/10x-plan`'s spec step, doesn't block sequencing).
- **Risk:** This is the north star — highest-risk assumption (can real bank data be parsed and deduped reliably) validated first, before anything else depends on transaction data.
- **Status:** done

### S-02: Manual transaction entry

- **Outcome:** user can manually add, edit, and delete transactions; a manually-entered transaction is recognized as the same one if it later appears in an imported statement, so it's never duplicated.
- **Change ID:** manual-transaction-entry
- **PRD refs:** FR-010
- **Prerequisites:** S-01 (reuses the dedup mechanism established there)
- **Parallel with:** S-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low risk — extends the dedup mechanism from S-01 to a second entry path rather than building new detection logic.
- **Status:** done

### S-03: Categorization queue

- **Outcome:** user can select a category for each transaction in the queue (no auto-suggestion); internal transfers are auto-flagged by default (overridable by hand); the user can re-categorize any transaction at any time.
- **Change ID:** categorization-queue
- **PRD refs:** FR-007, FR-009, FR-015, US-01 (When/Then)
- **Prerequisites:** S-01, S-10 (needs the user's own known-accounts list from S-10 to identify internal transfers)
- **Parallel with:** S-02
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Internal-transfer auto-detection heuristic needs the user's known accounts to be identifiable across banks — worth confirming the heuristic's accuracy against real data early, since it directly affects chart totals (S-04).
- **Status:** done
- **Follow-up (2026-09-30, `categorize-queue-only`):** `/categorize` now lists only uncategorized transactions ("Yay, all done!" when empty); the "Handled" list was removed. Category re-editing lives on the dashboard; the internal-transfer flag of handled transactions is toggled from the dashboard edit form (issue #23). See `context/archive/2026-09-30-categorize-queue-only/`.

### S-04: Category spend donut chart

- **Outcome:** user sees a donut chart of spend share per category for the current month (uncategorized and internal-transfer transactions excluded), filterable by category.
- **Change ID:** category-spend-donut-chart
- **PRD refs:** FR-011, FR-012, US-01 (Then, Acceptance Criteria)
- **Prerequisites:** S-03
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** — (S-09, transaction-history-view, has landed and resolved the static empty-state gap this used to track.)
- **Risk:** This is the slice the PRD's Primary Success Criterion is built around — correctness here depends entirely on S-03's categorization and internal-transfer flagging being right first.
- **Status:** done

### S-06: Category average-deviation signal

- **Outcome:** user sees, per category, whether current spend is above, below, or in line with that category's historical average — once at least 1 prior month of history exists; before that, the category shows actual spend with no deviation signal.
- **Change ID:** category-average-deviation-signal
- **PRD refs:** FR-013 (core Business Logic differentiator)
- **Prerequisites:** S-04
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Can only be verified end-to-end once real usage spans more than 1 month; plan and implement it, but full verification is naturally delayed relative to the other slices.
- **Status:** done

### S-08: Erste Bank Polska import

- **Outcome:** user can import an Erste Bank Polska CSV statement through the same loop, completing PLN-only coverage of all three target banks.
- **Change ID:** erste-import
- **PRD refs:** FR-003 (Erste portion)
- **Prerequisites:** S-01 (the PRD FR-003 gate on Revolut working first was waived by the user on 2026-10-01)
- **Parallel with:** S-06
- **Blockers:** —
- **Unknowns:**
  - Exact Erste CSV export format ("Historia → export", semicolon separator per shape-notes) — Owner: user. Block: no.
- **Risk:** Third and final parser — lowest risk of the three bank slices since the abstraction is validated twice already by this point.
- **Status:** done

### S-09: Transaction history view

- **Outcome:** user sees a chronological list of their imported and manually-entered transactions on the dashboard (date, description, amount, category), replacing the current static "you haven't imported anything yet" placeholder once data exists.
- **Change ID:** transaction-history-view
- **PRD refs:** FR-011 (partial — the list itself; the category/current-month filter portion of FR-011 stays with S-04, since it depends on categorization)
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low risk — read-only listing over data S-01 already persists; no new write paths or dedup logic involved.
- **Status:** done

### S-10: Account management

- **Outcome:** user can add, edit, and remove their own bank accounts (account number + bank name) via a settings page, and pick from their own accounts when manually entering a transaction, instead of a generic bank-name dropdown.
- **Change ID:** account-management
- **PRD refs:** FR-009 (the "user's own known accounts" concept internal-transfer detection needs), FR-010 (manual-entry integration)
- **Prerequisites:** F-01 (needs auth/user-scoping; does not touch transaction data, so no dependency on S-01)
- **Parallel with:** S-01, S-02, S-09
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Deferred out of S-02's `/10x-plan` scope (S-02 shipped with a minimal bank-name dropdown instead); sequenced ahead of S-03 because S-03's internal-transfer heuristic (FR-009) needs a stable list of the user's own accounts to match transfers against. S-02 can optionally be revisited later to consume this account list instead of its original dropdown, but that's not required for S-10 to land.
- **Status:** done

### S-11: VeloBank PDF import

- **Outcome:** user can import a VeloBank "Historia rachunku" PDF statement (the bank offers no CSV export) through the same import → dedup → categorize → chart loop; pending payments are included, dated by transaction date, and a statement whose printed running balances do not add up is rejected with an explanation instead of being imported.
- **Change ID:** pdf-statement-import
- **PRD refs:** FR-018
- **Prerequisites:** S-01 (reuses the import and dedup pipeline; adds a format-aware parser selection)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Whether a pending payment and its later booked row carry the same description and amount (a card hold may settle for a different amount) — if not, a payment imported while pending would be counted twice. **Open follow-up (post-merge):** once the bank has booked the two payments that were pending on 2026-10-01, export again and import; both should be flagged as duplicates. If either is not, reopen the pending-row decision — Owner: user. Block: no.
- **Risk:** First bank imported only through PDF — row boundaries come from the table geometry rather than a delimiter, so a misread row would silently skew totals; mitigated by checking the statement's own running balances and rejecting an inconsistent file. Revolut (S-07) was dropped from the MVP, so VeloBank is the last bank added.
- **Status:** done

### S-12: mBank and Erste Bank Polska PDF import (optional)

- **Outcome:** user can import an mBank or Erste Bank Polska PDF statement as a second input format next to the existing CSV import.
- **Change ID:** pdf-import
- **PRD refs:** FR-018
- **Prerequisites:** S-11 (reuses the format-aware import pipeline and the PDF fixture approach)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Cross-format dedup — decided approach: the import batch records its source format (CSV or PDF), and importing a period already imported from the other format on the same account shows a non-blocking overlap warning instead of attempting cross-format hash matching (descriptions, and therefore dedup hashes, differ between the two formats) — Owner: user. Block: no.
- **Risk:** Optional and outside M-1's done criterion; the risk is double-counting transactions already imported from CSV. The Erste PDF is lossy versus its CSV (no booking date, counterparty-only description), so the same transaction hashes differently from the CSV import — the overlap warning is the mitigation, not exact matching.
- **Status:** done

### S-14: Chart period selector

- **Outcome:** user can choose the period shown by the spend and income donut charts — last 30 days, last 90 days, a selected month, or a custom date range — with the slice-click transaction list and the average-deviation badge following the same period.
- **Change ID:** chart-period-selector
- **PRD refs:** FR-011, FR-012, FR-013; also covers the parked FR-016 (arbitrary date-range filter) for the charts
- **Prerequisites:** S-04, S-06
- **Parallel with:** S-15
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Replaces the `currentMonth` flag on `/api/transactions` with `from`/`to` and generalises the deviation calculator beyond the current month; the backend and UI phases should land in one PR.
- **Status:** done

### S-15: Category trend line chart

- **Outcome:** user sees a line chart on the dashboard with time (week, month or year) on the X axis and the summed spend (or income) of each selected category on the Y axis, one line per category.
- **Change ID:** category-trend-line-chart
- **PRD refs:** FR-011 (data exploration; not a separate PRD requirement)
- **Prerequisites:** S-04
- **Parallel with:** S-14
- **Blockers:** —
- **Unknowns:** —
- **Risk:** New aggregation endpoint (ISO weeks, clipped edge buckets, 120-bucket cap); the dashboard layout may get crowded next to two donuts and a selector.
- **Status:** done

## Backlog Handoff

| Roadmap ID | Change ID                        | Suggested issue title                                          | Ready for `/10x-plan` | Notes |
| ---------- | ---------------------------------- | ---------------------------------------------------------------- | ---------------------- | ----- |
| F-01       | minimal-auth-scaffold              | Add email/password auth with persistent session                  | yes                    | — |
| S-01       | mbank-import-with-dedup            | Import mBank CSV with duplicate review and summary                | no                     | Depends on F-01 landing first |
| S-02       | manual-transaction-entry           | Manual transaction add/edit/delete with dedup-hash parity         | no                     | Depends on S-01 |
| S-03       | categorization-queue               | Categorization queue with internal-transfer auto-flag              | no                     | Depends on S-01, S-10 |
| S-04       | category-spend-donut-chart         | Donut chart of category spend, current-month filter                | no                     | Depends on S-03 |
| S-06       | category-average-deviation-signal  | Historical-average deviation signal per category                   | no                     | Depends on S-04 |
| S-08       | erste-import                       | Import Erste Bank Polska CSV through the existing loop             | no                     | Depends on S-01 (FR-003 gate on Revolut waived 2026-10-01) |
| S-09       | transaction-history-view           | Show transaction history list on the dashboard                     | no                     | Depends on S-01 |
| S-10       | account-management                 | Add/edit/remove user's own bank accounts, use in manual entry       | yes                    | Depends on F-01 only; unblocks S-03's internal-transfer detection |
| S-11       | pdf-statement-import               | Import a VeloBank PDF statement through the existing loop           | yes                    | Depends on S-01 (done); already planned |
| S-12       | pdf-import                         | Import mBank and Erste Bank Polska PDF statements (optional)        | no                     | Depends on S-11; optional, outside M-1's done criterion |
| S-14       | chart-period-selector              | Period selector for the spend/income donut charts                   | yes                    | Depends on S-04, S-06 (done); planned |
| S-15       | category-trend-line-chart          | Per-category spend/income line chart by week/month/year             | yes                    | Depends on S-04 (done); planned |

## Open Roadmap Questions

No cross-cutting open questions at this time — PRD's own `## Open Questions` section is empty ("None outstanding"). The remaining unknowns (exact CSV export formats per bank) are scoped to their individual slices above (S-01, S-08) and don't block sequencing.

## Parked

- **S-07 / Revolut CSV import (FR-003, Revolut portion)** — Why parked: dropped from the MVP on 2026-10-02 for lack of time (and no Revolut sample available). Not a PRD Non-Goal — PRD v3 FR-003 still lists Revolut and should be updated if this is permanent; can be revived later.

- **S-05 / FR-017 (optional per-category budget vs. actual)** — Why parked: dropped from the MVP on 2026-10-02 for lack of time; effort goes to richer data exploration (period selector on the charts, category trend line chart) instead. Not a PRD Non-Goal — can be revived later.
- **FR-016 (arbitrary date-range transaction filter)** — Why parked: priority nice-to-have in PRD; the current-month filter (FR-011, folded into S-04) already covers the "audit the chart's numbers" need the PRD calls out.
- **Open Banking/PSD2 bank API integration** — Why parked: PRD Non-Goal; CSV import only, avoids compliance burden.
- **Multi-currency support** — Why parked: PRD Non-Goal; PLN-only, non-PLN transactions filtered at import with a skipped-count.
- **Fully automatic categorization without user approval** — Why parked: PRD Non-Goal; every category assignment requires user confirmation.
- **Rule-learning / auto-suggested categorization** — Why parked: PRD Non-Goal, dropped entirely (not deferred) — categorization is fully manual.
- **Recurring subscription detection** — Why parked: PRD Non-Goal.
- **Data sharing between users** — Why parked: PRD Non-Goal; strictly single-user data.
- **Email/push notifications** — Why parked: PRD Non-Goal; no notification system.
- **Mobile app** — Why parked: PRD Non-Goal; web only.
- **Banks other than mBank, Revolut, Erste, VeloBank** — Why parked: PRD Non-Goal; no other bank statement formats supported (VeloBank: PDF only).
- **Login via Google/OAuth** — Why parked: PRD Non-Goal; email+password only.

## Milestone History

- **M-1: Full MVP spend-insight loop (mBank, Erste, VeloBank)** (`mvp-spend-insight-loop`) — closed 2026-10-02. All foundations and slices done; S-05 (budget-vs-actual) and S-07 (Revolut import) were dropped from the MVP on 2026-10-02.

## Done

(Empty on first generation. `/10x-archive` appends an entry here — and flips that item's `Status` to `done` — when a change whose `Change ID` matches the item is archived.)

- **S-02: user can manually add, edit, and delete transactions; a manually-entered transaction is recognized as the same one if it later appears in an imported statement, so it's never duplicated.** — Archived 2026-09-28 → `context/archive/2026-09-25-manual-transaction-entry/`. Lesson: —.
- **F-01: (foundation) user can register and log in with email/password; the session persists across visits; every API endpoint requires auth and scopes data to the logged-in user.** — Archived 2026-09-28 → `context/archive/2026-09-22-minimal-auth-scaffold/`. Lesson: —.
- **S-01: user can import an mBank CSV export, see any detected duplicate shown side-by-side (existing vs. incoming) to decide skip/keep, and see an import summary (imported count / skipped-duplicate count).** — Archived 2026-09-28 → `context/archive/2026-09-25-mbank-import-with-dedup/`. Lesson: —.
- **S-04: user sees a donut chart of spend share per category for the current month (uncategorized and internal-transfer transactions excluded), filterable by category.** — Archived 2026-09-30 → `context/archive/2026-09-28-category-spend-donut-chart/`. Lesson: —.
- **S-03: user can select a category for each transaction in the queue (no auto-suggestion); internal transfers are auto-flagged by default (overridable by hand); the user can re-categorize any transaction at any time.** — Archived 2026-09-28 → `context/archive/2026-09-25-categorization-queue/`. Lesson: —.
- **categorize-queue-only (S-03 follow-up): the categorize page lists only uncategorized transactions and shows a "Yay, all done!" placeholder when none are left; the Handled list was removed.** — Archived 2026-09-30 → `context/archive/2026-09-30-categorize-queue-only/`. Lesson: —.
- **S-09: user sees a chronological list of their imported and manually-entered transactions on the dashboard (date, description, amount, category), replacing the current static "you haven't imported anything yet" placeholder once data exists.** — Archived 2026-09-28 → `context/archive/2026-09-25-transaction-history-view/`. Lesson: —.
- **S-10: user can add, edit, and remove their own bank accounts (account number + bank name) via a settings page, and pick from their own accounts when manually entering a transaction, instead of a generic bank-name dropdown.** — Archived 2026-09-28 → `context/archive/2026-09-25-account-management/`. Lesson: —.
- **S-06: user sees, per category, whether current spend is above, below, or in line with that category's historical average — once at least 1 prior month of history exists; before that, the category shows actual spend with no deviation signal.** — Archived 2026-09-30 → `context/archive/2026-09-30-category-average-deviation-signal/`. Lesson: —.
- **S-08: user can import an Erste Bank Polska CSV statement through the same loop, completing PLN-only coverage of all three target banks.** — Archived 2026-10-01 → `context/archive/2026-10-01-erste-import/`. Lesson: —.
- **S-14: user can choose the period shown by the spend and income donut charts — last 30 days, last 90 days, a selected month, or a custom date range — with the slice-click transaction list and the average-deviation badge following the same period.** — Archived 2026-10-02 → `context/archive/2026-10-02-chart-period-selector/`. Lesson: —.
- **S-15: user sees a line chart on the dashboard with time (week, month or year) on the X axis and the summed spend (or income) of each selected category on the Y axis, one line per category.** — Archived 2026-10-02 → `context/archive/2026-10-02-category-trend-line-chart/`. Lesson: —.
- **S-11: user can import a VeloBank PDF statement through the same import/dedup/categorize/chart loop.** — Archived 2026-10-02 → `context/archive/2026-10-02-pdf-statement-import/`. Lesson: —.
- **S-12: user can import an mBank or Erste Bank Polska PDF statement as a second input format next to the CSV import.** — Archived 2026-10-02 → `context/archive/2026-10-02-pdf-import/`. Lesson: —.
- **import-account-linking (S-01 follow-up): imported transactions are linked to one of the user's accounts instead of a free-text bank string.** — Archived 2026-10-02 → `context/archive/2026-10-02-import-account-linking/`. Lesson: —.
- **account-bank-selection (S-10 follow-up): accounts get a Bank dropdown (Polish banks + Other) that drives the import bank-mismatch check.** — Archived 2026-10-02 → `context/archive/2026-10-02-account-bank-selection/`. Lesson: —.
- **homepage-redesign: placeholder home page replaced with a real MyFinances landing page.** — Archived 2026-10-02 → `context/archive/2026-10-02-homepage-redesign/`. Lesson: —.
- **homepage-visual-refresh: homepage gets the logo and animation.** — Archived 2026-10-02 → `context/archive/2026-10-02-homepage-visual-refresh/`. Lesson: —.
