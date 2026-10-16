---
project: "MyFinances"
version: 4
status: draft
created: 2026-09-18
updated: 2026-10-16
context_type: greenfield
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

## Vision & Problem Statement

An individual manages personal finances across several bank accounts (mBank, Erste Bank Polska, VeloBank) and has no visibility into what they actually spend, per category, without manually tracking every transaction or committing to a budget up front. The moment they feel this: at the end of a week or month, wanting to know "how much did I really spend and where" — and finding that answer requires either manual spreadsheet work or trusting a bank's own (usually shallow) categorization.

The insight: categorization quality can come from the user's own accumulated decisions rather than either a fully manual process or a black-box auto-categorizer the user can't correct — each category choice is a deliberate call the user makes themselves. Spending gets compared against a derived historical average per category, so the user gets a meaningful "is this normal for me" signal without first having to define a budget.

## User & Persona

**Primary persona**: A single named user (the builder) managing their own personal finances across multiple bank accounts (mBank, Erste Bank Polska, VeloBank). Reaches for the product periodically (weekly/monthly) to import new transactions, clear the categorization queue, and check spending trends — not an always-on or real-time tool.

Explicitly single-user for MVP — no multi-tenant, no data sharing (see Non-Goals below).

## Success Criteria

### Primary
- User can import a real mBank CSV statement, complete categorization, and see an accurate category-breakdown chart (donut) reflecting real spending.

### Secondary
- User picks a period (last 30/90 days, a month, a custom range) and finds the above/below/in-line signal against their own historical average, plus the per-category trend over time, useful for judging whether spending is normal.
  > Updated in v4 (2026-10-16): replaces the optional-budget criterion; budget-vs-actual (FR-017) was dropped from the MVP on 2026-10-02 (`roadmap.md` Parked), and the period selector and trend chart (S-14, S-15) were built in its place.

### Guardrails
- Duplicate transactions (re-imported overlapping statements) are never silently double-counted in totals or charts — duplicate detection must hold for every imported statement, including edited imported rows. Exception for manually entered transactions (decided 2026-10-16, see FR-010): they are not matched against later imports. Exception decided 2026-10-02 (`archive/2026-10-02-pdf-import/change.md`): the same period imported once from CSV and once from PDF on the same account is not matched by hash (descriptions differ between formats); it shows a non-blocking overlap warning instead.

## User Stories

### US-01: User imports a bank statement and sees real spend per category

- **Given** a registered/logged-in user with an mBank CSV export
- **When** they import the file, review any flagged duplicates, and work through the categorization queue (fully manual — no auto-suggestion) at their own pace
- **Then** they see a donut chart showing spend share per category for the current month, based on whatever transactions are categorized so far

#### Acceptance Criteria
- Import shows an import summary (imported count / skipped-duplicate count, per the user's duplicate decisions)
- A transaction left uncategorized stays in the "to be categorized" queue indefinitely — the user is never forced to categorize before finishing an import
- Uncategorized transactions are excluded from the chart until categorized (categorizing them later updates the chart)
- Transactions flagged as internal transfer are excluded from the chart
- The category filter narrows the chart/list to the selected period, which defaults to the current month (FR-011)
- Budgets are not part of the MVP: no category is ever prompted to set one (FR-017 dropped, see Functional Requirements)

## Functional Requirements

### Onboarding & Import
- FR-001: User can register and log in with email/password, with a session that persists across visits. Priority: must-have
  > Socratic: Counter-argument considered: "single-user tool doesn't need full auth, a local profile would be simpler." Resolution: kept as written — full login auth stands.
- FR-002: User can import a CSV statement from mBank. Priority: must-have
  > Socratic: Counter-argument considered: "manual entry alone could validate the categorization flow without import complexity." Resolution: kept as written — import needed to prove value on real bulk data.
- FR-003: User can import a CSV statement from Erste Bank Polska — PLN-only (other currencies filtered with a skipped-count). Priority: must-have
  > Updated in v4 (2026-10-16): the Revolut portion was dropped from the MVP on 2026-10-02 for lack of time and no sample statement (`roadmap.md` Parked, S-07); the sequencing gate on Revolut was waived on 2026-10-01. Erste CSV is built in four export delimiters (`;`, `,`, tab, `|`).
  > Socratic: Counter-argument considered: "adding two more parsers at once adds format-risk before core value is proven on one bank." Resolution: revised — banks now added sequentially (Revolut, then Erste), each gated on the previous one working, instead of both landing together at once.
- FR-018: User can import a PDF statement from mBank, Erste Bank Polska or VeloBank (VeloBank offers no CSV export; mBank and Erste can also be imported from CSV) through the same import, duplicate-review and categorization flow as the CSV banks — PLN-only (other currencies filtered with a skipped-count). A statement whose printed running balances do not add up is rejected with an explanation instead of being imported. Importing a period already imported from the other format (CSV or PDF) on the same account shows a non-blocking overlap warning. Priority: must-have
  > Socratic: Counter-argument considered: "a PDF is a presentation format, not a data export — a misread row would silently skew totals and duplicate detection, and the user would never notice." Resolution: revised — the statement's own running balances are checked on import and an inconsistent file is rejected rather than imported, so a misread row cannot slip into the totals.

### Duplicate handling
- FR-004: For every detected duplicate transaction during import, the system shows the user both the existing and the incoming transaction side-by-side and lets them decide whether to skip or keep it. Priority: must-have
  > Socratic: Counter-argument considered: "silently skipping duplicates could hide a real detection bug — user wouldn't notice until double-checking." Resolution: revised — silent skip removed entirely; every detected duplicate is shown to the user for a decision.

### Categorization
- FR-007: User can select a category for each transaction in the categorization queue, from the user's own category list, with no suggestion shown. Priority: must-have
  > Updated in v4 (2026-10-16): the list is per user since 2026-10-09 (see FR-019), not a single fixed list.
  > Socratic: Counter-argument considered: "one-at-a-time approval could be tedious for large imports; batch-approve by suggested category might be needed." Resolution: kept as written — matches the intended "one transaction at a time" review flow.
- FR-009: System automatically marks a transaction as an internal transfer using a detection heuristic (e.g. matching transactions between the user's own known accounts); the user can change this flag by hand at any time. Flagged transactions are excluded from spend calculations and charts so income/outflow totals aren't distorted. Priority: must-have
  > Socratic: Counter-argument considered: "a manual-only flag depends on the user remembering to mark every transfer; forgetting silently skews totals." Resolution: revised — detection is automatic by default, with manual override always available.
  > Decided 2026-10-16: when one side of a detected pair is deleted, the other transaction stays flagged as an internal transfer. Editing a transaction's amount, date or account after pairing is not yet specified.
- FR-019: User can create and delete their own categories. A new user starts with a copy of 14 default categories, which are ordinary, deletable categories afterwards. Deleting a category that is used on transactions warns the user and asks for consent; on confirmation those transactions become uncategorized and return to the categorization queue. A category's kind (expense or income) must match the sign of the transaction amount, and the server rejects a mismatch. Each category keeps a stable chart colour. While uncategorized transactions exist, the Categorize tab shows an indicator and a toast is shown outside the Categorize page. Priority: must-have
  > Added in v4 (2026-10-16) from `changes/custom-categories-and-uncategorized-indicator/change.md` (decisions of 2026-10-09). Not covered by any v3 requirement; the toast sits in tension with the Non-Goal on notifications (see Open Questions).
- FR-015: User can re-categorize any transaction at any time, even after it has already been categorized. Priority: must-have
  > Updated in v4 (2026-10-16): re-categorizing happens on the dashboard edit form, not on /categorize, since the Handled list was removed on 2026-09-30 (`archive/2026-09-30-categorize-queue-only/change.md`).
  > Socratic: Counter-argument considered: "with categorization fully manual (no rule-learning), is re-categorization support still worth the effort?" Resolution: kept as written — mistakes and changed judgment calls still happen with manual categorization, so being able to fix them without deleting/re-adding the transaction stands on its own merit.

### Accounts
- FR-020: User can add, edit and remove their own bank accounts, each with a bank (chosen from a list of Polish banks, or "Other"), a free-text account name and an account number. A user cannot have two accounts with the same bank and account number. Removing an account that has transactions asks for consent and deletes its transactions and import batches. Priority: must-have
  > Added in v4 (2026-10-16) from S-10 (`roadmap.md`) and its follow-ups `account-bank-selection` and the account-delete fix. Uniqueness by bank and number decided 2026-10-16; the code currently enforces it on the free-text name instead (`BE/AppDbContext.cs:46-48`).

### Transaction management
- FR-010: User can manually add, edit, and delete transactions. A manually-entered transaction is hashed from exactly what the user typed and is not matched against a later bank import; keeping such entries free of duplicates is the user's responsibility. An imported transaction keeps its original hash when the user edits it, so a later import of the same statement still flags it as a duplicate. Priority: must-have
  > Updated in v4 (2026-10-16), decided by the product owner: replaces the v3 promise that a manual entry is recognised when it later appears in an imported statement. Today's code recomputes the hash on every edit (`BE/Transactions/TransactionEndpoints.cs:212-222`), which contradicts the second sentence; fixing it is a separate change.
  > Socratic: Counter-argument considered: "manual transactions bypass the import/duplicate-detection model; could collide with a later import of the same real transaction." Resolution: kept as written, with a clarification added — the existing duplicate-detection mechanism already covers this case regardless of whether the transaction was entered manually or imported.
- FR-011: User can filter the transaction list by category, scoped to the selected period (default: the current month). Priority: must-have
  > Updated in v4 (2026-10-16): the fixed current-month scope was replaced by the period selector (S-14, 2026-10-02).
  > Socratic: Counter-argument considered: "is filtering necessary for MVP validation, or a UI convenience that could wait?" Resolution: revised and split — category filter for the current month moved into the core flow as must-have (needed to audit the chart's numbers); arbitrary date-range filtering split out separately (FR-016).
- FR-016: User can filter the transaction list by an arbitrary date range (beyond the current month). Priority: nice-to-have
  > Updated in v4 (2026-10-16): covered by the period selector (last 30/90 days, a month, a custom range) for the charts and the slice drilldown list (S-14); the transactions API accepts the same `from`/`to` range.

### Insights & reporting
- FR-012: User can view a donut chart of spend share per category, and a second one for income, for the selected period. Priority: must-have
  > Updated in v4 (2026-10-16): income donut and period selection added in the implementation (`roadmap.md` S-04, S-14). A per-category trend line chart by week, month or year (S-15) was added as data exploration and is not a separate requirement.
  > Socratic: Counter-argument considered: "a single point-in-time snapshot doesn't show whether spending is unusual without the deviation signal." Resolution: kept as written — donut-only is still a meaningful first slice on its own; deviation signaling (FR-013) is a separate, later addition.
- FR-013: System automatically calculates spend per category for the selected period and signals deviation from the historical average, once at least 1 prior month of history exists for that category; before that, the category shows actual spend with no deviation signal. Priority: must-have
  > Updated in v4 (2026-10-16): for a month the comparison is pace-adjusted (month-to-date against the average of prior months) with an "in line" band of ±10%; for 30/90-day and custom periods the period total is compared with the average of the preceding windows of the same length. There is no separate weekly signal (`archive/2026-09-30-category-average-deviation-signal/plan-brief.md`, `archive/2026-10-02-chart-period-selector/plan-brief.md`).
  > Socratic: Counter-argument considered: "without a minimum history threshold, early averages are based on too little data and could mislead." Resolution: resolved — minimum history threshold set to 1 month of prior data before showing a deviation signal for a category.
- FR-017 (DROPPED from the MVP on 2026-10-02, parked in `roadmap.md` as S-05): User can optionally set a monthly budget per category; when set, the system shows actual spend vs. that budget alongside the average-based signal. Categories without a budget show only the average-based signal — never a prompt requiring one. Priority: must-have (when revived)
  > Socratic: Counter-argument considered: "reintroducing budgets — even optional — could dilute the product's core pitch of working without any pre-set budget, making it 'yet another budget app'." Resolution: kept as written — budget is opt-in per category and additive to the average signal, which still works with zero setup; the core differentiator (no budget required) is preserved because nothing is blocked or required on the budget-free path.

## Non-Functional Requirements

- Financial data (transactions, statements, categories) is never shared with or visible to any third party or other user — it stays scoped to the owning user's account only.
- The app gives visible feedback within a few seconds for import and chart-rendering operations, appropriate for periodic personal use (not a real-time system).
- No specific browser/device support commitment beyond a modern desktop browser — this is a personal tool, not a public product.

## Business Logic

The app computes how a user's actual spend per category deviates from their own historical average for that category, without the user ever having to set a budget or spending limit up front. (The optional per-category budget comparison was dropped from the MVP on 2026-10-02.)

Inputs the rule consumes: the user's categorized transactions over time, excluding internal transfers, grouped by category and the selected period. Output: for each category/period, the actual spend and a signal of whether it's above, below, or in line with that category's historical average (once at least 1 prior month of history exists for that category). The user encounters the signal in the same view where they check "how much did I spend" (the category breakdown), never behind a blocking prompt to define a budget.

## Access Control

Login via email + password, with an authenticated session that persists across visits. Flat user model — no roles, no admin surface. Each user sees and operates only on their own data (transactions, categories, accounts, import batches are all scoped to the owning user). No login via Google/OAuth for MVP (see Non-Goals below).

## Non-Goals

- **Open Banking/PSD2 bank API integration** — statements are imported from files (CSV or PDF) only; avoids the complexity and compliance burden of direct bank API integration.
- **Multi-currency support** — PLN-only; non-PLN transactions are filtered out at import with a skipped-count.
- **Fully automatic categorization without user approval** — every category assignment requires user confirmation; no black-box auto-categorization.
- **Rule-learning / auto-suggested categorization** — categorization is fully manual; the system never learns from past decisions to suggest a category. Dropped entirely, not deferred.
- **Recurring subscription detection** — no automatic detection of recurring/subscription payments.
- **Data sharing between users** — strictly single-user data; no shared households/accounts.
- **Email/push notifications** — no notification system of any kind.
- **Mobile app** — web only, no native mobile app.
- **Banks other than mBank, Erste Bank Polska, and VeloBank** (Revolut parked 2026-10-02, can be revived) — no support for any other bank's statement format; mBank and Erste Bank Polska are supported through CSV and PDF statements; VeloBank through PDF statements only (it offers no CSV export).
- **Login via Google/OAuth** — email+password only.

## Open Questions

Raised by the domain distillation of 2026-10-16 (`context/domain/01-domain-distillation.md`). Answered by the product owner on 2026-10-16 and folded into the requirements above:

- FR-009: after one side of an internal-transfer pair is deleted, the other stays flagged as an internal transfer.
- FR-010: manual entries are hashed from what the user typed and are not matched against imports; an imported row keeps its original hash when edited.
- FR-019: a category kind must match the amount sign, enforced on the server.
- FR-020: account uniqueness is bank + account number.

Still open:

- Statement integrity for CSV (FR-018 covers PDF only): both the mBank and the Erste CSV exports carry a running balance column ("Saldo po operacji"; mBank also has opening/closing balance and credit/debit totals), which the parsers ignore. Whether to reject CSV files whose balances do not add up is not decided; checked against the redacted fixtures in `MyFinances/backend/Tests/Fixtures/`, to be confirmed with a fresh real export.
- FR-009: what happens to the flag when a paired transaction is edited (amount, date or account) rather than deleted.
- Non-Goals: does the in-app uncategorized toast (FR-019) count as a "notification system"?
