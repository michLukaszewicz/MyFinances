# mBank Import With Duplicate Resolution — Plan Brief

> Full plan: `context/changes/mbank-import-with-dedup/plan.md`
> Research: `context/changes/mbank-import-with-dedup/research.md`

## What & Why

Build S-01, the MVP's north-star slice: a user uploads an mBank CSV export, any transaction that collides with an already-stored one (by dedup hash) is shown side-by-side for an explicit skip/keep decision, and the user sees an import summary when done. This is the riskiest untested assumption in the whole product — can real bank data be parsed and deduped reliably — validated first because every later slice (categorization, charts, budgets) only matters if this holds up.

## Starting Point

No transaction/import/CSV code exists yet — only the auth scaffold (F-01, cookie sessions, `AppDbContext` with an explicit placeholder for domain tables). Research reverse-engineered the real mBank export format from a user-supplied sample (Windows-1250 encoding, `;`-delimited, variable preamble before the real header row, Polish number/date formats) and found two setup blockers that must be fixed before anything else works: no codepage provider registered for cp1250, and the antiforgery *middleware* missing from `Program.cs` (only the service is registered), which 500s any file-upload endpoint today.

## Desired End State

A logged-in user opens an import page, uploads their mBank statement, resolves any flagged duplicates on a clear side-by-side screen, and gets a plain summary of what imported vs. what was skipped as a duplicate or an unparseable row. Re-uploading the same statement later correctly re-flags everything already imported — nothing is ever silently double-counted.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Data model | `Transaction` + `ImportBatch` tables | Import summary (FR-002) is queryable from a persisted batch, not just recomputed in-memory | Plan |
| Review flow | Single-pass review before commit | One clear screen, one final commit — no partial-import state to reason about | Plan |
| Collision resolution ("keep") | Insert as a new, separate `Transaction` row; hash is not a unique constraint | Matches the user's already-confirmed policy: collisions are legitimate, never auto-resolved in the hash formula | Research + Plan |
| Malformed rows | Skip row, count separately, continue the rest of the file | One bad row shouldn't block importing the other 74 | Plan |
| Staging between parse and commit | Client-side, full payload round-tripped (not server-side session) | Simpler — no server-side expiry/cleanup story needed | Plan |
| Server trust boundary | Commit endpoint re-hashes/re-validates every row itself | The client payload from "staging client-side" is not trustworthy input | Plan |
| Manual bank-selection fallback | Built now, even with only one parser | Fallback UX proven end-to-end before Revolut/Erste add real choices | Plan |
| Upload size limit | 5 MB (Kestrel + FormOptions) | ~2000x the real sample size (13 KB), tiny compared to framework defaults (~28.6 MB / 128 MB) | Plan |
| Parser test fixture | Redacted real-format sample, not synthetic | Tests the actual quirks research found; a prior redaction attempt already once accidentally erased a real quirk | Plan |
| mBank currency filtering | None | The real sample export has no currency column to filter on; FR-003's PLN-only rule is scoped to Revolut/Erste | Plan |
| Import history UI | None — summary shown once, no browsing page | Matches FR-002's acceptance criterion exactly; browsing is scope creep beyond this slice's PRD refs | Plan |
| Dedup hash formula | `userId + date + amount + description + bank`, unchanged | Preserves FR-010 manual/imported hash parity; confirmed to collide on real data (two distinct BLIK transfers) but resolved via always-review, not a formula change | Research |

## Scope

**In scope:**
- `IBankStatementParser` abstraction + `MBankCsvParser` (cp1250, preamble-skip, Polish formats)
- Dedup hash computation, reusable by S-02 later
- Two-endpoint upload → review → commit API with server-side re-validation
- Frontend upload page (+ manual bank-selection fallback), side-by-side review screen, commit + summary display
- Integration tests proving the PRD Guardrail (no silent double-counting) against a redacted real-format fixture

**Out of scope:**
- Categorization (S-03), internal-transfer flagging (FR-009), charts/budgets (S-04–S-06)
- Revolut/Erste parsers (S-07/S-08) — abstraction supports them, implementations don't exist yet
- Currency filtering for mBank, import-history browsing UI
- Manual transaction entry (S-02) — depends on this slice's hash but is a separate change

## Architecture / Approach

`POST /import/parse` (multipart upload) parses the file via the chosen `IBankStatementParser`, flags dedup collisions against the user's existing transactions, and returns the full row set to the frontend unpersisted. The frontend renders the review screen, collects skip/keep decisions, and `POST /import/commit` (JSON) re-validates and re-hashes every row server-side before persisting `Transaction`s + one `ImportBatch`. All bank-specific parsing quirks stay behind `IBankStatementParser`; the dedup/endpoint code only ever sees the bank-agnostic `NormalizedTransaction` shape.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend Foundation | Packages, codepage provider, antiforgery middleware fix, `Transaction`/`ImportBatch` schema, user-scoping convention, size limits | Missing either setup blocker silently breaks every later phase |
| 2. mBank Parser & Dedup | `IBankStatementParser`, `MBankCsvParser`, dedup hash — unit-tested against a real-format fixture | Preamble-skip/encoding/Polish-format edge cases not fully covered by the fixture |
| 3. Import API Endpoints | `/import/parse` + `/import/commit`, server-side re-validation | Trusting client-supplied hash/duplicate flags instead of re-deriving them |
| 4. Frontend Upload Page | File picker + manual bank-selection fallback | `apiFetch` multipart handling not exercised before |
| 5. Frontend Duplicate Review Screen | Side-by-side skip/keep UI | Allowing commit before every collision has an explicit decision |
| 6. Frontend Commit & Summary | Commit wiring + summary display | Summary counts not matching actual persisted rows |
| 7. Integration Tests | End-to-end + re-import-same-file test proving the Guardrail | Re-import scenario is the one test that actually proves "never silently double-counted" |

**Prerequisites:** F-01 (minimal-auth-scaffold), already `impl_reviewed`.
**Estimated effort:** ~5-7 sessions across 7 phases (solo, after-hours).

## Open Risks & Assumptions

- The redacted test fixture must preserve every real quirk (cp1250 bytes, quoted fields, preamble shape) — a prior redaction pass already once accidentally stripped a quirk; care is needed when committing the fixture.
- The real sample is one account/one month; an mBank export with a different account type or a wider date range could reveal preamble variations not covered by this fixture — first real user import after ship is the actual validation.
- 5 MB size limit is a judgment call, not derived from an mBank-documented maximum export size.

## Success Criteria (Summary)

- User can upload a real mBank CSV, see accurate duplicate flags for genuinely repeated transactions, and never see a transaction silently double-counted on re-import
- Import summary counts (imported / skipped-duplicate / skipped-error) match the actual persisted state after commit
- A malformed row or an unrecognized file produces a clear, recoverable error path — never a 500 or a silently-aborted import
