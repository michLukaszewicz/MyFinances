<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Manual Transaction Entry Implementation Plan

- **Plan**: context/changes/manual-transaction-entry/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2
- **Date**: 2026-09-28
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Success criteria verification

**Automated** (re-verified during implementation, both phases green):
- `dotnet build` (from `MyFinances/backend`) — PASS
- `dotnet test` (from `MyFinances/backend`) — PASS, 82/82
- `npm run typecheck` (from `MyFinances/frontend`) — PASS
- `npm run build` (from `MyFinances/frontend`) — PASS

**Manual** (Progress section): all 9 manual items (1.3-1.5, 2.3-2.8) are `[ ]` pending — not rubber-stamped, correctly left unchecked awaiting the user's own end-to-end pass. No evidence-vs-checkbox mismatch since none are marked complete.

## Findings

### F1 — TOCTOU race between duplicate-hash check and insert/update

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: MyFinances/backend/Transactions/TransactionEndpoints.cs:79-93 (POST), :146-160 (PUT)
- **Detail**: Two concurrent requests carrying the same hash could both pass the `!Force` duplicate `SELECT` before either `SaveChangesAsync` commits, both inserting/updating. `Transaction.Hash` has no unique DB constraint (by design — collisions are a legitimate "kept duplicate" outcome per S-01). This mirrors the same non-atomic check-then-write pattern already accepted in `ImportEndpoints.cs`'s commit flow, so it is pre-existing risk carried forward, not a new regression. At single-user MVP scale (one browser tab, no concurrent writers), the exposure window is negligible.
- **Fix**: No action needed now; if concurrent write scenarios become relevant later (e.g. multi-tab usage), consider it alongside the same gap in the import commit flow rather than patching this endpoint in isolation.
- **Decision**: FIXED — wrapped the duplicate-hash check + write in POST/PUT in a `Serializable` DB transaction via a new `BeginTransactionIfSupportedAsync` helper (`TransactionEndpoints.cs`). The EF Core InMemory provider used by the test suite doesn't support transactions (`Database.BeginTransactionAsync` throws `InvalidOperationException`), so the helper catches that and falls back to no transaction — production (Npgsql) gets real atomicity, tests are unaffected (82/82 still green after the fix).

### F2 — `parseErrorBody` naming diverges from `extractErrorMessage` precedent

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/frontend/app/routes/home.tsx:85-105
- **Detail**: `settings.tsx`/`categorize.tsx`'s `extractErrorMessage` returns a plain `string`. `home.tsx` introduces `parseErrorBody`, returning `{ message, existingTransaction? }` — a necessarily richer shape since this page also needs to surface the 409 duplicate-transaction snapshot, which the two precedent pages never encounter. The divergence is functionally justified, not an inconsistency introduced by oversight.
- **Fix**: No action needed — the different name accurately signals the different (richer) return contract.
- **Decision**: SKIPPED

## Notes

Both review sub-agents independently confirmed:
- All 3 planned changes (`TransactionContracts.cs`, `TransactionEndpoints.cs`, `home.tsx`) MATCH the plan's stated intent/contract exactly, including the explicit "no shared antiforgery helper" and "no shared dialog component" constraints.
- All "What We're NOT Doing" scope guardrails held (no schema/migration change, no `IsInternalTransfer` control, no `ImportEndpoints.cs`/`DedupHash.cs` touch, hard delete confirmed, no new route/page, no extracted shared component).
- Changed-file list matches the plan's file list exactly — no extra/unplanned files, `Program.cs` and `routes.ts` untouched (correctly, since no new route registration was needed).
- The dedup hash is always recomputed server-side from request fields; `TransactionWriteRequest` carries no client-supplied `Hash`, consistent with the import flow's "never trust a client-supplied hash" rule.
