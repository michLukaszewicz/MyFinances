# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-10-02

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the
   team is worried about X, and the failure would surface somewhere in
   <area>" carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `MyFinances/backend`,
`MyFinances/frontend/app` (excluding fixtures, migrations, bin/obj, docs,
archive). 83 commits in the last 30 days.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | Re-importing an overlapping statement of the same format silently double-counts spend in totals and charts | High | High | interview Q1, Q3; PRD Guardrail (duplicates never silently double-counted); PRD FR-004; hot-spot dir `MyFinances/backend/Import` (30 commits/30d) |
| 2 | A CSV or PDF parser misreads a row (amount, date, locale-dependent date format) and totals diverge from the statement without any warning | High | High | interview Q1, Q2 (parser refactor changed date format, import threw), Q4; PRD FR-018 (running-balance check); hot-spot dir `MyFinances/backend/Import` (30 commits/30d) |
| 3 | One user reads or modifies another user's transactions, accounts or imports | High | Medium | interview Q1, Q4; PRD Non-Functional Requirements and Access Control (data scoped to owning user); hot-spot dir `MyFinances/backend/Transactions` (27 commits/30d) |
| 4 | The same period imported from CSV and from PDF on the same account is not recognized as overlapping and is double-counted | High | Medium | interview Q4; PRD FR-018 (non-blocking overlap warning across formats); PRD Guardrail |

Risks 3 covers the abuse / security lens (authorization and ownership, not
only authentication).

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | A second import of the same transactions leaves totals unchanged, and every detected duplicate is surfaced to the user for a decision | "Skipping a duplicate means sums are right"; "equal dedup key means same transaction"; partial overlap behaves like full overlap | How the dedup key is built and what goes into it; persisted state compared against; behavior on partial overlap; what existing tests already cover | integration (API + persistence) | Expected counts copied from what the code returns today; only the all-duplicates case |
| #2 | A fixture row yields exactly the amount and date printed in the source, regardless of the machine's culture; a statement with inconsistent balances is rejected, not imported | "Passes on the fixture so it parses correctly"; "the balance check catches every misread"; date format is stable across cultures | Date and number parsing paths; culture handling; delimiter variants (Erste); balance-check semantics; fixture provenance | unit with independent fixtures; include a run under a different culture | Expected values produced by the parser itself; fixtures generated from parser output; asserting only row count |
| #3 | User B gets not-found or forbidden and no state change for every read, edit, delete and categorize attempt on user A's transactions, accounts and imports | "Logged in means authorized"; "every query already filters by owner" | Every endpoint that takes a resource id; where the owner scope is applied; shared query/composition points | integration with two users | Happy path with a single user; checking only the status code, not that data is unchanged |
| #4 | Importing a PDF after a CSV of the same period on the same account produces the overlap warning and does not inflate totals | "CSV-vs-CSV dedup works, so CSV-vs-PDF does too" | Whether the dedup key is independent of source format; how overlap is computed across formats; fixtures that represent one real period in both formats | integration | CSV and PDF fixtures that do not describe the same underlying transactions |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|----------------|------------|--------|---------------|
| 1 | Import integrity and dedup | Re-imports and CSV-vs-PDF overlaps never double-count and always surface duplicates | #1, #4 | integration | complete | context/changes/testing-import-integrity-dedup/ |
| 2 | Parser correctness | Parsed amounts and dates equal an independent source, independent of culture | #2 | unit with fixtures | researched | context/changes/testing-parser-correctness/ |
| 3 | Data ownership | A user can never read or change another user's data | #3 | integration (two users) | not started | — |
| 4 | Quality-gates wiring | Lock the floor: run the suite automatically in the agent loop and in CI (none exists today) | cross-cutting | gates (hook, CI) | not started | — |

**Status vocabulary** (fixed — parser literals): `not started`,
`change opened`, `researched`, `planned`, `implementing`, `complete`.

## 4. Stack

The classic test base for this project.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| unit + integration (backend) | xUnit | 2.9.2 | 22 test files in `MyFinances/backend/Tests`; run with `dotnet test` from `MyFinances/backend` |
| integration host | Microsoft.AspNetCore.Mvc.Testing (`WebApplicationFactory`) | 10.0.3 | endpoint-level tests |
| persistence in tests | EF Core InMemory | 10.0.3 | does not reproduce Postgres constraints or transactions; research decides per risk whether a real database is required |
| frontend unit / e2e | none | n/a | no framework; frontend deliberately left out of this rollout (see §7) |
| CI | none | n/a | no workflows exist; addressed by Phase 4 |

Test-base profile: `meaningful` for backend, absent for frontend.

**Stack grounding tools (current session):**
- Docs: Context7 — available; not queried at planning time, to be used per phase for xUnit / ASP.NET Core testing APIs; checked: 2026-10-02
- Search: Exa.ai — not available in current session; checked: 2026-10-02
- Runtime/browser: built-in Claude browser — available; not used (no browser-level risk in scope); checked: 2026-10-02
- Provider/platform: GitHub via `gh` CLI — relevant to the CI gate in Phase 4; checked: 2026-10-02

## 5. Quality Gates

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| backend build | local + CI | required after §3 Phase 4 | compile errors |
| frontend typecheck | local + CI | required after §3 Phase 4 | type drift |
| backend unit + integration (`dotnet test`) | local + CI | required after §3 Phase 1 (CI after Phase 4) | logic regressions in import, parsing, ownership |
| post-edit / end-of-turn hook | local (agent loop) | recommended after §3 Phase 4 | regressions at edit time |

## 6. Cookbook Patterns

How to add new tests in this project. Sub-sections fill in as phases ship.

### 6.1 Adding a parser unit test

- TBD — see §3 Phase 2 (independent-oracle fixtures, culture-independence pattern).
- **Reference test (existing)**: `MyFinances/backend/Tests/MBankCsvParserTests.cs`.
- **Run locally**: `dotnet test` from `MyFinances/backend`.

### 6.2 Adding an import / dedup integration test

- **Flow**: drive the real endpoints end to end. Parse the file (`ImportTestHelpers.ParseAsync` / `ParsePdfAsync`), derive the per-row decisions from `IsDuplicate` (or a fixed decision), commit (`CommitAsync`, `CommitAllAsync`, `CommitSkippingDuplicatesAsync`), then assert the persisted state read straight from the database (`GetStoredAsync`), not only the HTTP responses.
- **Expected values are authored, never read back**: write the rows the test expects as literals (`MBankCsvRow`, `MBankPairedTransaction`) and build the file from them (`MBankCsvBuilder`, `MBankPdfBuilder`). Do not generate a fixture from parser output and do not take the oracle from a first parse.
- **Paired-fixture rule**: a CSV and a PDF that must describe the same transactions are both built from one authored row list (`MBankPairedStatement`); the CSV carries the title only, the PDF an operation-type line plus the title. Keep a sanity test that both files parse to the same (date, amount) multiset as the authored list.
- **Structure**: `public class ... : IAsyncLifetime`; `InitializeAsync` creates a fresh `AuthApiFactory`, an authenticated client and an empty mBank account, so a test's Arrange holds only what differs. Use neutral invented text ("Test description N").
- **Pin known limitations as characterization tests**: name them `..._KnownLimitation` or comment them as current behavior, not endorsement.
- **Reference tests (new)**: `MyFinances/backend/Tests/Import/ImportDedupIntegrityTests.cs`, `MyFinances/backend/Tests/Import/ImportCrossFormatOverlapTests.cs`; shared helper `MyFinances/backend/Tests/Support/ImportTestHelpers.cs`.
- **Reference test (existing)**: `MyFinances/backend/Tests/Import/ImportEndpointsTests.cs`.
- **Run locally**: `dotnet test --filter "FullyQualifiedName~Import"` from `MyFinances/backend`.

### 6.3 Adding an ownership (two-user) test for an endpoint

- TBD — see §3 Phase 3 (cross-user denial pattern).

### 6.4 Per-rollout-phase notes

(Appended by `/10x-implement` after each phase.)

- **Phase 1 (import integrity and dedup)** — shipped two test classes, `ImportDedupIntegrityTests` (re-import, partial overlap, skip/keep contract, manual-entry match) and `ImportCrossFormatOverlapTests` (CSV-vs-PDF overlap warning in both orders, same-format control, partial period, warn-only commit), plus the helpers `ImportTestHelpers`, `MBankCsvBuilder` and `MBankPairedStatement`. Known limitations pinned as current behavior: the cross-format overlap is warn-only (keeping both imports doubles the stored sum); repeated identical rows are flagged together (the dedup key has no occurrence counter); edit-then-reimport is out of scope. Fixture note: the paired PDF spans two pages on purpose, because the mBank PDF parser reads no rows from a statement that fits on one page.

## 7. What We Deliberately Don't Test

- **Login and registration screens** — scaffold, single user. Re-evaluate if multi-user access is introduced. (Source: interview Q5.)
- **Visual snapshots of the homepage** — break on every redesign and catch nothing. Re-evaluate if the UI stabilizes. (Source: interview Q5.)
- **Bank account settings screen** — low impact, rarely changed. (Source: interview Q5.)
- **Frontend chart and period logic (numbers shown vs API)** — considered during brief review and judged unlikely to fail; no frontend test framework is added. Re-evaluate if chart or period bugs reach real use. (Source: brief review, 2026-10-02.)
- **Manual-transaction write path (silent failure, overwrite, duplicate)** — raised in interview Q1 but removed from the risk map during brief review; only the dedup behavior shared with import is covered (Phase 1). Re-evaluate if a write-path bug appears. (Source: brief review, 2026-10-02.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-10-02
- Stack versions last verified: 2026-10-02
- AI-native tool references last verified: 2026-10-02 (none recommended)

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
