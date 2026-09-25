# Transaction History View — Plan Brief

> Full plan: `context/changes/transaction-history-view/plan.md`

## What & Why

Replace the dashboard's static "you haven't imported any transactions yet" placeholder with a real, paginated list of the user's transactions (date, description, amount, category). This closes the gap flagged during S-01 manual testing: the home page currently shows that placeholder regardless of whether the user actually has data.

## Starting Point

`home.tsx`'s authenticated branch always renders a static empty-state message — no transaction-fetching exists on the dashboard today. The backend's `Transaction` entity (from S-01) already has everything needed (date, description, signed amount, nullable category), but no listing endpoint exists yet, and no pagination precedent exists anywhere in the codebase.

## Desired End State

A logged-in user sees their transactions listed newest-first on `/`, with amounts colored by sign (red for expenses, green for income) and uncategorized rows labeled "Uncategorized". A "Load more" button reveals additional pages of 20. A user with zero transactions still sees the original empty-state placeholder, unchanged. Returning from `/import` shows new transactions automatically via React Router's normal loader re-run.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Sort order | Newest first | Matches dashboard-feed convention — most relevant info first. |
| Pagination strategy | "Load more" button, 20/page | Simplest implementation with no existing precedent to build from; avoids unnecessary infinite-scroll complexity for an MVP. |
| Uncategorized display | Show "Uncategorized" label | Sets up the category column now so S-03 just fills it in later, no layout rework. |
| Amount styling | Color-coded by sign (red/green) | User's explicit choice, enabled by the fact that mBank amounts already carry their real sign — no extra sign-inference logic needed. |
| Bank/source column | Omitted | Matches the roadmap outcome's literal 4 fields (date, description, amount, category); revisit once multiple banks are live (S-07/S-08). |
| Dashboard refresh after import | Rely on React Router's natural loader re-run | Zero added complexity — this is the default behavior, not something to build. |
| Placement | Inline on the dashboard (`home.tsx`) | Matches the roadmap outcome text ("...on the dashboard") — no new route. |

## Scope

**In scope:**
- New `GET /api/transactions?skip=&take=` endpoint, user-scoped, paginated, tested.
- Dashboard rendering of the transaction list with load-more, sign-colored amounts, "Uncategorized" labels.
- Preserving the existing zero-transaction empty state.

**Out of scope:**
- Category names/filtering (S-03), bank/source column, date-range filtering (parked FR-016 / S-04's current-month filter), manual transaction entry (S-02), infinite scroll or numbered pagination, a dedicated `/transactions` route.

## Architecture / Approach

Backend first: a new endpoint following `ImportEndpoints.cs`'s exact structural conventions (grouped under `/api`, `GetUserId`-scoped, plain-record DTOs), ordered newest-first with a deterministic `Id` tiebreak, returning a `HasMore` flag. Frontend second: the first page loads via `home.tsx`'s `clientLoader` (so it's naturally fresh on every navigation to `/`); "Load more" clicks fetch and append subsequent pages via `apiFetch` with local component state, reusing `import.tsx`'s row-styling and error-handling conventions.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend transaction list endpoint | `GET /api/transactions`, DTOs, integration tests | Low — read-only query over existing data, no dedup/write-path involved |
| 2. Frontend dashboard integration | List rendering, load-more, empty-state preserved | Medium — first pagination UI in the codebase, no precedent to copy |

**Prerequisites:** S-01 (`mbank-import-with-dedup`) implemented — done.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- Assumes every current transaction has `ImportBatchId != null` (manual entry, S-02, doesn't exist yet) — the list simply renders whatever's in `Transactions`, so this isn't a blocking assumption, just a note that "imported vs. manual" isn't distinguishable in the UI today.
- No `CreatedAt` field exists on `Transaction`, so pagination's tiebreak (`Id` descending) is stable but not chronologically meaningful for same-day transactions — acceptable since it only affects ordering *within* a single day, not correctness.

## Success Criteria (Summary)

- A user with transactions sees them listed newest-first with correct amount coloring and category labeling.
- A user with zero transactions sees no regression — same empty-state message as today.
- Pagination ("Load more") works correctly across page boundaries with no duplicate or skipped rows.
