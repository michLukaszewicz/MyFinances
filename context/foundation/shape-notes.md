---
project: "MyFinances"
context_type: greenfield
created: 2026-09-18
updated: 2026-09-18
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "pain category"
      decision: "data trapped somewhere — spending data exists across bank statements but isn't visible/aggregated"
    - topic: "primary persona scope"
      decision: "single named user, including the builder — own accounts across mBank/Revolut/Erste"
  frs_drafted: 13
  quality_check_status: accepted
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

An individual manages personal finances across several bank accounts (mBank, Revolut, Erste Bank Polska) and has no visibility into what they actually spend, per category, without manually tracking every transaction or committing to a budget up front. The moment they feel this: at the end of a week or month, wanting to know "how much did I really spend and where" — and finding that answer requires either manual spreadsheet work or trusting a bank's own (usually shallow) categorization.

The insight: categorization quality can come from the user's own accumulated decisions rather than either a fully manual process or a black-box auto-categorizer the user can't correct — each category choice teaches a reusable rule. Spending gets compared against a derived historical average per category, so the user gets a meaningful "is this normal for me" signal without first having to define a budget.

## User & Persona

**Primary persona**: A single named user (the builder) managing their own personal finances across multiple bank accounts (mBank, Revolut, Erste Bank Polska). Reaches for the product periodically (weekly/monthly) to import new transactions, clear the categorization queue, and check spending trends — not a always-on or real-time tool.

Explicitly single-user for MVP — no multi-tenant, no data sharing (confirmed later in Non-Goals, §6 of original PRD).

## Access Control

Login via email + password, JWT-based session. Flat user model — no roles, no admin surface. Each user sees and operates only on their own data (transactions, categories, category rules, import batches are all scoped by `userId`). No OAuth/social login for MVP (explicit non-goal, see original PRD §6).

## Success Criteria

### Primary
- User can import a real mBank CSV statement, complete categorization, and see an accurate category-breakdown chart (donut) reflecting real spending.

### Secondary
- User optionally sets a budget for at least one category and finds the actual-vs-budget signal useful alongside the average-based one.

### Guardrails
- Duplicate transactions (re-imported overlapping statements) are never silently double-counted in totals or charts — dedup-by-hash must hold.

## Functional Requirements

### Onboarding & Import
- FR-001: User can register and log in with email/password (JWT session). Priority: must-have
  > Socratic: Counter-argument considered: "single-user tool doesn't need full auth, a local profile would be simpler." Resolution: kept as written — full login auth stands.
- FR-002: User can import a CSV statement from mBank. Priority: must-have
  > Socratic: Counter-argument considered: "manual entry alone could validate the categorization flow without import complexity." Resolution: kept as written — import needed to prove value on real bulk data.
- FR-003: User can import CSV statements from Revolut, then Erste Bank Polska, added one bank at a time — each gated on the prior bank's import working end-to-end — PLN-only (other currencies filtered with a skipped-count). Priority: must-have
  > Socratic: Counter-argument considered: "adding two more parsers at once adds format-risk before core value is proven on one bank." Resolution: revised — banks now added sequentially (Revolut, then Erste), each gated on the previous one working, instead of both landing together in Etap 2.

### Duplicate handling
- FR-004: For every detected duplicate transaction during import, the system shows the user both the existing and the incoming transaction side-by-side and lets them decide whether to skip or keep it. Priority: must-have
  > Socratic: Counter-argument considered: "silently skipping duplicates could hide a real dedup bug — user wouldn't notice until double-checking." Resolution: revised — silent skip removed entirely; every detected duplicate is shown to the user for a decision (folds prior FR-005 into this FR, always-review instead of silent-skip-by-default).

### Categorization
- FR-007: User can select a category for each transaction in the categorization queue, from the full category list, with no suggestion shown in the MVP core flow. Priority: must-have
  > Socratic: Counter-argument considered: "one-at-a-time approval could be tedious for large imports; batch-approve by suggested category might be needed." Resolution: kept as written — matches the PRD's explicit "one transaction at a time" UI design.
- FR-009: System automatically marks a transaction as an internal transfer using a detection heuristic (e.g. matching transactions between the user's own known accounts); the user can change this flag by hand at any time. Flagged transactions are excluded from spend calculations and charts so income/outflow totals aren't distorted. Priority: must-have
  > Socratic: Counter-argument considered: "a manual-only flag depends on the user remembering to mark every transfer; forgetting silently skews totals." Resolution: revised — detection is automatic by default, with manual override always available.
- FR-015: User can re-categorize any transaction at any time, even after it has already been categorized. Priority: must-have
  > Socratic: Counter-argument considered: "with categorization fully manual (no rule-learning), is re-categorization support still worth the effort?" Resolution: kept as written — mistakes and changed judgment calls still happen with manual categorization, so being able to fix them without deleting/re-adding the transaction stands on its own merit.

### Transaction management
- FR-010: User can manually add, edit, and delete transactions. The dedup hash is computed identically for manually-entered and imported transactions, so a later CSV import of the same real-world transaction is still caught as a duplicate. Priority: must-have
  > Socratic: Counter-argument considered: "manual transactions bypass the import-batch/dedup model; could collide with a later import of the same real transaction." Resolution: kept as written, with the hash-parity clarification added — the existing dedup mechanism already covers this case since the hash is computed the same way regardless of source.
- FR-011: User can filter the transaction list by category, scoped to the current month (Etap 0 baseline). Priority: must-have
  > Socratic: Counter-argument considered: "is filtering necessary for MVP validation, or a UI convenience that could wait?" Resolution: revised and split — category filter for the current month moved into Etap 0 as must-have (needed to audit the chart's numbers); arbitrary date-range filtering moved to Etap 2.
- FR-016: User can filter the transaction list by an arbitrary date range (beyond the current month). Priority: nice-to-have (Etap 2)

### Insights & reporting
- FR-012: User can view a donut chart of spend share per category (Etap 0 baseline). Priority: must-have
  > Socratic: Counter-argument considered: "a single point-in-time snapshot doesn't show whether spending is unusual without the deviation signal." Resolution: kept as written — donut-only is still a meaningful first slice on its own; deviation signaling (FR-013) stays a separate, later addition in Etap 2.
- FR-013: System automatically calculates spend per category per week/month and signals deviation from the historical average, once at least 1 prior month of history exists for that category; before that, the category shows actual spend with no deviation signal. Priority: must-have
  > Socratic: Counter-argument considered: "without a minimum history threshold, early averages are based on too little data and could mislead." Resolution: resolved — minimum history threshold set to 1 month of prior data before showing a deviation signal for a category.
- FR-017: User can optionally set a monthly budget per category; when set, the system shows actual spend vs. that budget alongside the average-based signal. Categories without a budget show only the average-based signal — never a prompt requiring one. Priority: must-have (Etap 0)
  > Socratic: Counter-argument considered: "reintroducing budgets — even optional — could dilute the product's core pitch of working without any pre-set budget, making it 'yet another budget app'." Resolution: kept as written — budget is opt-in per category and additive to the average signal, which still works with zero setup; the core differentiator (no budget required) is preserved because nothing is blocked or required on the budget-free path.

## User Stories

### US-01: User imports a bank statement and sees real spend per category

- **Given** a registered/logged-in user with an mBank CSV export
- **When** they import the file, review any flagged duplicates, and work through the categorization queue (manually — no auto-suggestion in this stage) at their own pace
- **Then** they see a donut chart showing spend share per category for the current month, based on whatever transactions are categorized so far

#### Acceptance Criteria
- Import shows an import summary (imported count / skipped-duplicate count, per the user's duplicate decisions)
- A transaction left uncategorized stays in the "to be categorized" queue indefinitely — the user is never forced to categorize before finishing an import
- Uncategorized transactions are excluded from the chart until categorized (categorizing them later updates the chart)
- Transactions flagged as internal transfer are excluded from the chart
- The category filter narrows the chart/list to the current month (FR-011)
- If the user has set a budget for a category, the chart/queue shows actual spend vs. that budget; categories without a budget show no such comparison and are never prompted to set one

## Business Logic

The app computes how a user's actual spend per category deviates from their own historical average for that category, without the user ever having to set a budget or spending limit up front — and if the user optionally sets a budget for a category, actual spend is also compared against that budget.

Inputs the rule consumes: the user's categorized transactions over time, grouped by category and period (week/month), plus an optional per-category budget value the user may set. Output: for each category/period, the actual spend, a signal of whether it's above, below, or in line with that category's historical average, and — for categories with a budget set — whether spend is above, below, or in line with the budget. The user encounters both signals in the same view where they check "how much did I spend" (e.g. the category breakdown or a bar next to a reference line); a category without a budget set shows only the average-based signal, never a blocking prompt to define one.

## Non-Functional Requirements

- Financial data (transactions, statements, categories) is never shared with or visible to any third party or other user — it stays scoped to the owning user's account only.
- The app gives visible feedback within a few seconds for import and chart-rendering operations, appropriate for periodic personal use (not a real-time system).
- No specific browser/device support commitment beyond a modern desktop browser — this is a personal tool, not a public product.

## Non-Goals

- **Open Banking/PSD2 bank API integration** — CSV import only; avoids the complexity and compliance burden of direct bank API integration.
- **Multi-currency support** — PLN-only; non-PLN transactions are filtered out at import with a skipped-count.
- **Fully automatic categorization without user approval** — every category assignment requires user confirmation; no black-box auto-categorization.
- **Rule-learning / auto-suggested categorization** — categorization is fully manual; the system never learns from past decisions to suggest a category. Dropped entirely, not deferred.
- **Recurring subscription detection** — no automatic detection of recurring/subscription payments.
- **Data sharing between users** — strictly single-user data; no shared households/accounts (matches Access Control).
- **Email/push notifications** — no notification system of any kind.
- **Mobile app** — web only, no native mobile app.
- **Banks other than mBank, Revolut, and Erste Bank Polska** — no support for any other bank's CSV format.
- **Login via Google/OAuth** — email+password only (see Access Control).

## Forward: tech-stack

The user has already decided on a stack (captured here for the downstream tech-stack-selection step, not part of the PRD itself):

| Layer | Choice |
|---|---|
| Frontend | React + TypeScript + Tailwind CSS |
| Charts | Recharts |
| Backend | ASP.NET Core Web API (.NET) |
| Database | SQL Server + Entity Framework Core |
| Auth | ASP.NET Core Identity + JWT |
| Backend tests | xUnit |
| E2E tests | Playwright |
| CI/CD | GitHub Actions (build + tests on push/PR) |

## Forward: technical-roadmap

Notes carried from the original PRD, relevant to implementation planning but not PRD-schema content:

- CSV parser architecture: `IBankStatementParser` with `CanParse`/`Parse`, one implementation per bank (`MBankCsvParser`, `RevolutCsvParser`, `ErsteBankCsvParser`), bank recognition by column headers with manual-selection fallback, normalization to a common `NormalizedTransaction { Date, Description, Amount }`.
- Dedup hash: `userId + date + amount + description + bank`, computed identically for manual and imported transactions (see FR-010).
- To verify before implementation: real CSV export formats/column layouts for Revolut (app + web export), Erste (Historia → export, semicolon separator), and mBank (Płatności → Historia → Zestawienie operacji, check encoding).
- Minimum history period before a deviation-from-average signal is meaningful — to be decided during technical spec (open question, see below).

## Open Questions

None outstanding.

