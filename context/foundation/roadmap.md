---
project: "MyFinances"
version: 1
status: draft
created: 2026-09-22
updated: 2026-09-25
prd_version: 3
main_goal: market-feedback
top_blocker: time
milestone_id: mvp-spend-insight-loop
milestone_seq: 1
milestone_status: open
---

# Roadmap: MyFinances

> Derived from `context/foundation/prd.md` (v3) + `context/foundation/shape-notes.md`'s `## Forward: technical-roadmap` notes + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-1: Full MVP spend-insight loop (mBank, Revolut, Erste)** — Status: open

- **Intent:** Deliver the entire MVP scope in `prd.md` v3 — the full import → dedup → categorize → chart → budget/average-deviation loop, across all three banks — as one outcome-scoped milestone. The PRD carries no staged "Etap" split (that framing lived only in the earlier shape-notes draft), so all must-have FRs belong to this single milestone.
- **Source materials:** `context/foundation/prd.md` (v3), supplemented by `context/foundation/shape-notes.md`'s `## Forward: technical-roadmap` notes (parser architecture, dedup-hash shape, CSV formats to verify).
- **Done when:** every F-NN and S-NN below is `done`.

## Vision recap

An individual manages personal finances across several bank accounts (mBank, Revolut, Erste Bank Polska) and has no visibility into what they actually spend, per category, without manual spreadsheet work or trusting a bank's own shallow categorization. The product's core bet — its **wedge**, the one trait that, if removed, makes it just another expense tracker — is that categorization quality comes from the user's own deliberate, correctable decisions rather than a black-box auto-categorizer, and spend gets compared against the user's own historical average so "is this normal for me" works without ever requiring a budget to be set up front.

## North star

**S-01: User imports an mBank CSV statement and resolves any detected duplicates** — the riskiest unknown in this MVP is whether real bank export data can be reliably parsed and deduplicated; everything downstream (categorization, charts, budgets) is only as trustworthy as this step, so it's validated first and placed as early as its prerequisites allow.

> A reader-facing gloss: the **north star** here is the smallest end-to-end slice whose successful delivery proves the riskiest part of the core hypothesis — placed early because every later slice only matters if this one holds up.

## At a glance

| ID    | Change ID                       | Outcome (user can …)                                                              | Prerequisites | PRD refs                    | Status   |
| ----- | -------------------------------- | ----------------------------------------------------------------------------------- | -------------- | ---------------------------- | -------- |
| F-01  | minimal-auth-scaffold            | (foundation) register, log in, and stay logged in via a persistent session          | —              | FR-001, Access Control        | in-progress |
| S-01  | mbank-import-with-dedup          | import an mBank CSV, resolve flagged duplicates, and see an import summary          | F-01           | FR-002, FR-004, US-01, Guardrail (dedup) | in-progress |
| S-02  | manual-transaction-entry         | manually add, edit, and delete transactions without creating import duplicates      | S-01           | FR-010                        | proposed |
| S-03  | categorization-queue             | categorize queued transactions, with internal transfers auto-flagged (overridable)  | S-01, S-10     | FR-007, FR-009, FR-015, US-01 | proposed |
| S-04  | category-spend-donut-chart       | see a donut chart of category spend for the current month, filterable by category   | S-03           | FR-011, FR-012, US-01         | proposed |
| S-05  | category-budget-vs-actual        | optionally set a per-category budget and see actual-vs-budget alongside the chart   | S-04           | FR-017                        | proposed |
| S-06  | category-average-deviation-signal| see a category's spend flagged as above/below/in line with its historical average   | S-04           | FR-013                        | proposed |
| S-07  | revolut-import                   | import a Revolut CSV statement through the same import/dedup/categorize/chart loop  | S-01           | FR-003                        | proposed |
| S-08  | erste-import                     | import an Erste Bank Polska CSV statement through the same loop                     | S-07           | FR-003                        | proposed |
| S-09  | transaction-history-view         | see a chronological list of their imported/manually-entered transactions on the dashboard (date, description, amount, category) | S-01           | FR-011 (partial)              | proposed |
| S-10  | account-management                | add/edit/remove their own bank accounts (account number + bank name) via a settings page, and pick from them when manually entering a transaction | F-01           | FR-009, FR-010                | in-progress |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                          | Chain                                          | Note                                                                 |
| ------ | ------------------------------- | ----------------------------------------------- | --------------------------------------------------------------------- |
| A      | Core spend-insight loop         | `F-01` → `S-01` → `S-03` → `S-04` → `S-05` → `S-06` | The main validation path for `main_goal: market-feedback` — auth, import, categorize, chart, then the two comparison signals. |
| B      | Manual transaction entry        | `S-02`                                          | Joins Stream A at `S-01` — independent of categorization/chart work, can run in parallel. |
| C      | Bank coverage expansion         | `S-07` → `S-08`                                 | Joins Stream A at `S-01` — Revolut/Erste import, sequentially gated per PRD FR-003 (Erste only after Revolut works end-to-end). |
| D      | Transaction visibility           | `S-09`                                          | Joins Stream A at `S-01` — closes the "no history view" gap flagged during S-01 manual testing (2026-09-25); independent of categorization/chart work, can run in parallel. |
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
- **Unlocks:** S-01, S-02, S-03, S-04, S-05, S-06, S-07, S-08 — every vertical slice persists or reads user-scoped data, so none can be built safely before this lands.
- **Prerequisites:** — (Baseline: backend/frontend scaffolds present; auth is the one absent layer blocking everything else.)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced first because retrofitting user-scoping onto transactions/categories built without it would mean redoing data-access code across every later slice.
- **Status:** in-progress

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
- **Status:** in-progress

### S-02: Manual transaction entry

- **Outcome:** user can manually add, edit, and delete transactions; a manually-entered transaction is recognized as the same one if it later appears in an imported statement, so it's never duplicated.
- **Change ID:** manual-transaction-entry
- **PRD refs:** FR-010
- **Prerequisites:** S-01 (reuses the dedup mechanism established there)
- **Parallel with:** S-03, S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low risk — extends the dedup mechanism from S-01 to a second entry path rather than building new detection logic.
- **Status:** proposed

### S-03: Categorization queue

- **Outcome:** user can select a category for each transaction in the queue (no auto-suggestion); internal transfers are auto-flagged by default (overridable by hand); the user can re-categorize any transaction at any time.
- **Change ID:** categorization-queue
- **PRD refs:** FR-007, FR-009, FR-015, US-01 (When/Then)
- **Prerequisites:** S-01, S-10 (needs the user's own known-accounts list from S-10 to identify internal transfers)
- **Parallel with:** S-02, S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Internal-transfer auto-detection heuristic needs the user's known accounts to be identifiable across banks — worth confirming the heuristic's accuracy against real data early, since it directly affects chart totals (S-04).
- **Status:** proposed

### S-04: Category spend donut chart

- **Outcome:** user sees a donut chart of spend share per category for the current month (uncategorized and internal-transfer transactions excluded), filterable by category.
- **Change ID:** category-spend-donut-chart
- **PRD refs:** FR-011, FR-012, US-01 (Then, Acceptance Criteria)
- **Prerequisites:** S-03
- **Parallel with:** S-07
- **Blockers:** —
- **Unknowns:**
  - S-01's home page currently always shows a static "you haven't imported any transactions yet" empty state regardless of whether the user actually has data (no transaction-count/listing query exists yet) — flagged during S-01 manual testing 2026-09-25. This gap is now tracked as S-09 (transaction-history-view); until S-09 lands, the empty-state copy is a known simplification, not a fixed requirement. Owner: implementer. Block: no.
- **Risk:** This is the slice the PRD's Primary Success Criterion is built around — correctness here depends entirely on S-03's categorization and internal-transfer flagging being right first.
- **Status:** proposed

### S-05: Category budget vs. actual

- **Outcome:** user can optionally set a monthly budget per category; when set, actual spend is shown against that budget alongside the average-based signal; categories without a budget show no such comparison and are never prompted to set one.
- **Change ID:** category-budget-vs-actual
- **PRD refs:** FR-017 (Secondary Success Criterion)
- **Prerequisites:** S-04
- **Parallel with:** S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Purely additive to the chart view — low risk of disrupting the budget-free path, since PRD is explicit that unset-budget categories must never be prompted.
- **Status:** proposed

### S-06: Category average-deviation signal

- **Outcome:** user sees, per category, whether current spend is above, below, or in line with that category's historical average — once at least 1 prior month of history exists; before that, the category shows actual spend with no deviation signal.
- **Change ID:** category-average-deviation-signal
- **PRD refs:** FR-013 (core Business Logic differentiator)
- **Prerequisites:** S-04
- **Parallel with:** S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Can only be verified end-to-end once real usage spans more than 1 month; plan and implement it, but full verification is naturally delayed relative to the other slices.
- **Status:** proposed

### S-07: Revolut import

- **Outcome:** user can import a Revolut CSV statement through the same import → dedup → categorize → chart loop already proven for mBank.
- **Change ID:** revolut-import
- **PRD refs:** FR-003 (Revolut portion)
- **Prerequisites:** S-01 (PRD: gated on mBank import working end-to-end)
- **Parallel with:** S-02, S-03, S-04
- **Blockers:** —
- **Unknowns:**
  - Exact Revolut CSV export format (app vs. web export may differ) — Owner: user. Block: no.
- **Risk:** Second implementation of the `IBankStatementParser` pattern (per shape-notes) — validates that the parser abstraction generalizes beyond mBank before a third bank is added.
- **Status:** proposed

### S-08: Erste Bank Polska import

- **Outcome:** user can import an Erste Bank Polska CSV statement through the same loop, completing PLN-only coverage of all three target banks.
- **Change ID:** erste-import
- **PRD refs:** FR-003 (Erste portion)
- **Prerequisites:** S-07 (PRD: gated on Revolut import working end-to-end)
- **Parallel with:** S-05, S-06
- **Blockers:** —
- **Unknowns:**
  - Exact Erste CSV export format ("Historia → export", semicolon separator per shape-notes) — Owner: user. Block: no.
- **Risk:** Third and final parser — lowest risk of the three bank slices since the abstraction is validated twice already by this point.
- **Status:** proposed

### S-09: Transaction history view

- **Outcome:** user sees a chronological list of their imported and manually-entered transactions on the dashboard (date, description, amount, category), replacing the current static "you haven't imported anything yet" placeholder once data exists.
- **Change ID:** transaction-history-view
- **PRD refs:** FR-011 (partial — the list itself; the category/current-month filter portion of FR-011 stays with S-04, since it depends on categorization)
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-07
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low risk — read-only listing over data S-01 already persists; no new write paths or dedup logic involved.
- **Status:** planning

### S-10: Account management

- **Outcome:** user can add, edit, and remove their own bank accounts (account number + bank name) via a settings page, and pick from their own accounts when manually entering a transaction, instead of a generic bank-name dropdown.
- **Change ID:** account-management
- **PRD refs:** FR-009 (the "user's own known accounts" concept internal-transfer detection needs), FR-010 (manual-entry integration)
- **Prerequisites:** F-01 (needs auth/user-scoping; does not touch transaction data, so no dependency on S-01)
- **Parallel with:** S-01, S-02, S-07, S-09
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Deferred out of S-02's `/10x-plan` scope (S-02 shipped with a minimal bank-name dropdown instead); sequenced ahead of S-03 because S-03's internal-transfer heuristic (FR-009) needs a stable list of the user's own accounts to match transfers against. S-02 can optionally be revisited later to consume this account list instead of its original dropdown, but that's not required for S-10 to land.
- **Status:** in-progress

## Backlog Handoff

| Roadmap ID | Change ID                        | Suggested issue title                                          | Ready for `/10x-plan` | Notes |
| ---------- | ---------------------------------- | ---------------------------------------------------------------- | ---------------------- | ----- |
| F-01       | minimal-auth-scaffold              | Add email/password auth with persistent session                  | yes                    | — |
| S-01       | mbank-import-with-dedup            | Import mBank CSV with duplicate review and summary                | no                     | Depends on F-01 landing first |
| S-02       | manual-transaction-entry           | Manual transaction add/edit/delete with dedup-hash parity         | no                     | Depends on S-01 |
| S-03       | categorization-queue               | Categorization queue with internal-transfer auto-flag              | no                     | Depends on S-01, S-10 |
| S-04       | category-spend-donut-chart         | Donut chart of category spend, current-month filter                | no                     | Depends on S-03 |
| S-05       | category-budget-vs-actual          | Optional per-category budget vs. actual                            | no                     | Depends on S-04 |
| S-06       | category-average-deviation-signal  | Historical-average deviation signal per category                   | no                     | Depends on S-04 |
| S-07       | revolut-import                     | Import Revolut CSV through the existing loop                       | no                     | Depends on S-01 |
| S-08       | erste-import                       | Import Erste Bank Polska CSV through the existing loop             | no                     | Depends on S-07 |
| S-09       | transaction-history-view           | Show transaction history list on the dashboard                     | no                     | Depends on S-01 |
| S-10       | account-management                 | Add/edit/remove user's own bank accounts, use in manual entry       | yes                    | Depends on F-01 only; unblocks S-03's internal-transfer detection |

## Open Roadmap Questions

No cross-cutting open questions at this time — PRD's own `## Open Questions` section is empty ("None outstanding"). The remaining unknowns (exact CSV export formats per bank) are scoped to their individual slices above (S-01, S-07, S-08) and don't block sequencing.

## Parked

- **FR-016 (arbitrary date-range transaction filter)** — Why parked: priority nice-to-have in PRD; the current-month filter (FR-011, folded into S-04) already covers the "audit the chart's numbers" need the PRD calls out.
- **Open Banking/PSD2 bank API integration** — Why parked: PRD Non-Goal; CSV import only, avoids compliance burden.
- **Multi-currency support** — Why parked: PRD Non-Goal; PLN-only, non-PLN transactions filtered at import with a skipped-count.
- **Fully automatic categorization without user approval** — Why parked: PRD Non-Goal; every category assignment requires user confirmation.
- **Rule-learning / auto-suggested categorization** — Why parked: PRD Non-Goal, dropped entirely (not deferred) — categorization is fully manual.
- **Recurring subscription detection** — Why parked: PRD Non-Goal.
- **Data sharing between users** — Why parked: PRD Non-Goal; strictly single-user data.
- **Email/push notifications** — Why parked: PRD Non-Goal; no notification system.
- **Mobile app** — Why parked: PRD Non-Goal; web only.
- **Banks other than mBank, Revolut, Erste** — Why parked: PRD Non-Goal; no other bank CSV formats supported.
- **Login via Google/OAuth** — Why parked: PRD Non-Goal; email+password only.

## Milestone History

(Empty — this is the first milestone.)

## Done

(Empty on first generation. `/10x-archive` appends an entry here — and flips that item's `Status` to `done` — when a change whose `Change ID` matches the item is archived.)
