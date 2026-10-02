---
date: 2026-10-02T11:01:04+02:00
researcher: Claude (Sonnet 5.5) for Michal Lukaszewicz
git_commit: d11921cc67fa0a92046ce172d47f23149ced4d45
branch: main
repository: Kurs 10xDev (MyFinances)
topic: "Ground test-plan Phase 1 (Risks #1 and #4): import dedup and CSV-vs-PDF overlap"
tags: [research, import, dedup, ImportEndpoints, DedupHash, mixed-format-overlap, testing]
status: complete
last_updated: 2026-10-02
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Import integrity and dedup (Risk #1, Risk #4)

**Date**: 2026-10-02T11:01:04+02:00
**Git Commit**: d11921c (main; working tree has only untracked/skill changes, no backend edits)
**Scope inspected**: `MyFinances/backend/Import/{ImportEndpoints,DedupHash,ImportContracts,MBankCsvParser,MBankPdfParser}.cs`, `Import/Pdf/PdfStatementReader.cs` (JoinCell), `Transactions/{Transaction,ImportBatch,TransactionEndpoints}.cs`, `Dashboard/DashboardEndpoints.cs`, `Migrations/20261002063707_AddImportBatchSourceFormat.cs`, `frontend/app/routes/import.tsx`, `Tests/ImportEndpointsTests.cs`, `Tests/DedupHashTests.cs`, `Tests/Support/MBankSampleData.cs`, `Tests/Fixtures/mbank-sample-redacted.csv`. Erste and VeloBank parsers were only grepped, not read in full.

## Research Question

For Risk #1 (re-import double-counts) and Risk #4 (CSV-vs-PDF same period not recognised): ground the real failure path in code, verify/correct the response guidance in test-plan §2, locate existing tests, name the cheapest useful test layer (and whether InMemory suffices), flag speculative risks and misleading hot-spot evidence.

## Summary

1. **Risk #1 is mostly protected by design, but "skip a duplicate" is a client decision, not a server guarantee.** `/import/parse` flags rows whose hash exists in the user's stored transactions; `/import/commit` inserts every row the client marks `Keep` and never re-checks it (`ImportEndpoints.cs:151-200`). Totals stay unchanged only if the client sends `Skip`. The server cannot be made to "leave totals unchanged" on a second import without that decision, so the testable guarantee is: every duplicate is *flagged* on parse, and `Skip` rows are not inserted on commit.
2. **The dedup key is a boolean per hash, not a multiset.** With N stored rows and M parsed rows sharing one hash, all M rows are flagged duplicate (`ImportEndpoints.cs:92-109`). For M > N (partial overlap with repeated identical transactions) "Skip all duplicates" drops M−N real transactions; "Keep" on them risks N double counts. Not covered by any existing test. This directly hits the "equal dedup key means same transaction" challenge.
3. **Editing a transaction rewrites its hash** (`TransactionEndpoints.cs:214-222`). After an edit of date, description, amount or account, a re-import of the original statement row no longer matches and is not flagged. This is a concrete, untested double-count path not named in the test plan.
4. **Risk #4 is a warning-only guarantee, and the guidance wording must be corrected.** CSV and PDF rows for the same transaction produce different hashes (different description source), so the overlap path is a separate date-range count (`ImportEndpoints.cs:115-129`). "Does not inflate totals" cannot be asserted: nothing blocks or removes the rows; the UI text says duplicates "will not be detected" (`import.tsx:270-278`). The provable behavior is `MixedFormatOverlapCount > 0` for a real PDF/CSV pair, and that the stored data is what the user chose.
5. **No existing test uses a CSV and a PDF that describe the same transactions.** Overlap tests seed dummy rows ("Seeded {guid}", −1.00) (`ImportEndpointsTests.cs:1097-1140`); the mBank CSV fixture covers 2026-08-01..05 while the mBank PDF fixtures cover 2026-09-10..10-01 (`MBankSampleData.cs:13-18`). This is the exact anti-pattern named in the plan.
6. **Cheapest layer: integration through `WebApplicationFactory` + EF InMemory is sufficient for both risks.** Neither guarantee depends on a DB constraint or transaction (hash is deliberately not unique, `Transaction.cs:6-7`, `DedupHash.cs:6-8`). InMemory would only be insufficient for concurrent double-commit, which has no constraint to test.

## Detailed Findings

### Risk #1: where the failure path lives

**Dedup key.** `DedupHash.ComputeHash` = SHA-256 over `userId|yyyy-MM-dd|amount F2 invariant|description|accountId` (`DedupHash.cs:11-24`). Description is compared as an exact string: case, inner whitespace and any trailing characters matter. Bank, import batch and source format are not in the key. Manual entries use the same function (`TransactionEndpoints.cs:116`), so a manual row also flags a later import row (the parse query has no batch filter, `ImportEndpoints.cs:92-96`).

**Parse flags duplicates.**
```csharp
var existingByHash = (await db.Transactions
        .Where(t => t.UserId == userId && hashes.Contains(t.Hash))
        .ToListAsync())
    .GroupBy(t => t.Hash)
    .ToDictionary(g => g.Key, g => g.First());
...
existing is not null,   // IsDuplicate
```
(`ImportEndpoints.cs:92-96, 107`). Flag = "any stored row with this hash exists". Multiplicity is lost, and the UI shows a single representative stored row.

**Commit trusts the client's decision.**
```csharp
var keepRows = request.Rows.Where(r => r.Decision == RowDecision.Keep).ToList();
...
// A "Keep" decision means insert regardless of duplicate status
```
(`ImportEndpoints.cs:151, 186-187`). Condition: any commit request with `Keep` rows inserts them, with or without a prior parse. Consequences inspected on this path only:
- `SkippedDuplicateCount` counts `Skip` rows whose hash exists (`:174`); a `Skip` on a non-duplicate row is silently dropped and not counted anywhere.
- No idempotency: posting the same `Keep` payload twice inserts twice (sequential, no check at `:151-200`). The frontend sets a `committing` flag; I did not verify that it disables the button, so double-submit from the UI is unverified.

**Frontend gate.** Duplicate rows need an explicit decision before Continue (`allDuplicatesDecided`, `import.tsx:162-167`); non-duplicates default to `Keep` (`:181`). "Skip all duplicates" commits immediately (`:208-215`). So "surfaced to the user for a decision" is enforced in the UI, not the API. An API-level test can only prove the flag (`IsDuplicate`/`ExistingTransaction`), not that a decision was made.

**Totals.** Dashboard sums only rows with a category, not internal transfers, in the period (`DashboardEndpoints.cs:63-71`). A freshly imported, uncategorized duplicate does not move the charts until categorized. "Totals unchanged" is therefore best asserted as stored-row count / sum of amounts per account in the DB, plus (optionally) `/dashboard/category-spend` after categorizing.

**Correction to guidance: "skipping a duplicate means sums are right".** Holds only when the stored rows and the skipped rows are the same transactions one-to-one. It fails when:
- M parsed rows share a hash with N < M stored rows (partial overlap with repeated identical rows, e.g. two identical coffee purchases on one day, only one stored): all M flagged, Skip-all loses M−N rows.
- A stored row was edited (hash rewritten, `TransactionEndpoints.cs:214-222`): the parsed original is not flagged, Keep default inserts it again.

**Correction to guidance: "equal dedup key means same transaction".** Confirmed as the weak spot, in the other direction too: two genuinely distinct same-day, same-amount, same-description transactions are one key. The code accepts this by design ("collisions are resolved by routing through the review UI", `DedupHash.cs:6-8`).

**Partial overlap vs full overlap.** Existing tests cover full re-import only (every row flagged; `Parse_*AfterCommit_FlagsEveryCommittedRowAsDuplicate` for Erste CSV :439, VeloBank PDF :680, mBank PDF :845, Erste PDF :971). No test imports a file whose rows are partly new and partly stored and checks that exactly the overlapping rows are flagged. Not-flagged-when-different cases exist only for a different account (:256).

### Risk #4: where the failure path lives

**Why dedup cannot catch it.** CSV description is column 3 (`Tytuł`) only (`MBankCsvParser.cs:165`; fixture row `"NA JEDZENIE"`). PDF description is the whole description cell, all its text lines joined with spaces (`MBankPdfParser.cs:229`, `PdfStatementReader.cs:128`), which in the synthetic data starts with the operation type line (e.g. `BLIK P2P-WYCHODZĄCY` + name, `MBankSampleData.cs:42`). Different strings → different hash → never flagged as duplicates. The code comment states the same (`ImportEndpoints.cs:115-116`). The Erste CSV description is field 2 (`ErsteCsvParser.cs:145`); I did not read the Erste PDF description construction, so the Erste CSV/PDF description relation is unverified.

**The overlap warning.**
```csharp
mixedFormatOverlapCount = await db.Transactions.CountAsync(t =>
    t.UserId == userId && t.AccountId == account.Id
    && t.ImportBatch != null
    && t.ImportBatch.SourceFormat != parser.Format
    && t.Date >= minDate && t.Date <= maxDate);
```
(`ImportEndpoints.cs:122-128`). Properties of this inspected path:
- Warning only; response field `MixedFormatOverlapCount`; the UI banner is non-blocking (`import.tsx:270-279`).
- Range = min/max date of the *new* file's parsed rows; counts all other-format rows in that range, related or not. A different-account, manual (null batch), same-format, or other-user row is not counted (covered by seeded tests :1233-1288).
- Zero parsed rows → 0 (`:118`; test :1291).
- `SourceFormat` on the batch is whatever the client sends on commit; missing → `Csv` (`ImportContracts.cs:31`, test :1344). The server does not verify it against the uploaded file, and legacy batches were backfilled as `Csv` except VeloBank → `Pdf` (`20261002063707_AddImportBatchSourceFormat.cs:20-28`). A PDF batch committed by a client that omits the field would be recorded as Csv and would not warn on a later Csv import (inferred from the code; not exercised by a test).

**Correction to guidance: "does not inflate totals".** The system does not prevent inflation; it warns. The testable statement is: after committing a real PDF batch and parsing a real CSV of the same period on the same account, `MixedFormatOverlapCount` is > 0 and (to document, not endorse) no row is flagged `IsDuplicate`. A test that asserts totals stay unchanged would require product behavior that does not exist, so either the plan accepts "warn only", or a product decision is needed (see Open Questions).

**Correction to guidance: "CSV-vs-CSV dedup works, so CSV-vs-PDF does too".** Confirmed false by construction (see above); the existing suite does not demonstrate it either way with real parsed rows.

### Existing tests

| Behavior | Test (file:line, `ImportEndpointsTests.cs`) | Gap |
|---|---|---|
| Full re-import flagged, Erste CSV incl. 3 identical rows | `Parse_ErsteFileAfterCommit_FlagsEveryCommittedRowAsDuplicate` :439 | Only the all-duplicates case |
| Full re-import flagged, VeloBank PDF / mBank PDF / Erste PDF | :680, :845, :971 | Same |
| Two stored rows sharing a hash do not crash parse | :221 | Regression for a thrown exception, not for counts |
| Different account not flagged | :256 | |
| Commit persists Keep, counts skipped duplicate (hand-built payload) | :290 | One duplicate, one new; no totals check, no re-parse after |
| Overlap count: in range, boundaries, outside, same format, manual, other account, other user, zero rows | :1192-1308 | Seeded dummy rows, not real parsed CSV/PDF pair |
| Overlap after real commit with `Pdf` | `Commit_Then_ParseOtherFormat_WarnsAboutTheCommittedBatch` :1359 | Committed row is a made-up "Some row" |
| Real mBank CSV into account with seeded PDF batch warns | :1068 | Seeded batch |
| Hash function sensitivity per input | `DedupHashTests.cs` (6 facts) | Does not test description/whitespace normalization (there is none) |

Baseline: `dotnet test --filter "ImportEndpointsTests|DedupHashTests"` → 69 passed, 0 failed (run 2026-10-02). No test covers: partial overlap, M>N repeated rows, edit-then-reimport, double commit, Skip on a non-duplicate, a real paired CSV+PDF, Erste CSV delimiter variants against each other.

### Test layer and InMemory

Both risks are decided by application code and plain queries; hash is not a unique constraint, and commit uses no explicit transaction. InMemory is sufficient for: partial/full overlap, repeated-hash multiplicity, edit-then-reimport, Skip/Keep persistence, paired CSV/PDF warning. InMemory would hide, but no code path here depends on, DB constraints or `SaveChanges` atomicity; commit adds one batch plus N transactions in a single `SaveChangesAsync` (`ImportEndpoints.cs:179-202`), whose atomicity under Postgres is not exercised (not a Risk #1/#4 scenario). The factory already swaps Npgsql for InMemory (`AuthEndpointsTests.cs:41-61`).

Fixtures for a real pair: `MBankPdfBuilder.Build(header, openingBalance, rows, layout)` already builds a PDF from independent row records (used at `ImportEndpointsTests.cs:883`). A matching CSV must be authored from the same row records (not by calling the parser on the PDF). The existing CSV fixture is a single-encoding redacted file, so a paired test would need a small CSV writer in `Tests/Support` or a hand-authored CSV beside a hand-built PDF whose rows match its dates/amounts.

### Hot-spot evidence check

The plan cites 30 commits/30d for `MyFinances/backend/Import` and 27 for `Transactions`. `git log --since=30.days -- Import` on this checkout returns 16 commits and `-- Transactions` 17. The plan's counts could come from a different counting basis (it states "83 commits" overall); I did not reproduce them. Churn in Import is real and correlates with the format-aware pipeline commits (`333b83c`, `8b20218`), which touched `ImportEndpoints.cs`, but it is likelihood evidence only. The `Transactions` churn is mostly unrelated to import integrity except the edit-rehash path above.

## Code References

- `Import/DedupHash.cs:11-24` - hash formula; description compared exactly
- `Import/ImportEndpoints.cs:84-109` - parse: hashes, existing lookup, `IsDuplicate`
- `Import/ImportEndpoints.cs:115-129` - mixed-format overlap count
- `Import/ImportEndpoints.cs:151-202` - commit: Keep inserts unconditionally, Skip counted only if hash exists
- `Import/ImportContracts.cs:17,28,31` - response/request contracts, `SourceFormat` default Csv
- `Import/MBankCsvParser.cs:165` vs `Import/MBankPdfParser.cs:229` - CSV vs PDF description source
- `Transactions/TransactionEndpoints.cs:116,193,214-222` - manual create hash; edit rewrites hash
- `Transactions/Transaction.cs:6-7` - hash intentionally non-unique
- `Dashboard/DashboardEndpoints.cs:63-71` - totals: categorized, non-transfer rows only
- `frontend/app/routes/import.tsx:162-215,270-279` - decision gate and warning banner
- `Tests/ImportEndpointsTests.cs:439,680,845,971,1068,1192-1370` - existing coverage

## Architecture Insights

- Dedup is advisory: parse reports, the client decides, commit obeys. Any "no double count" test must therefore drive both endpoints the way the client does (parse → build decisions from `IsDuplicate` → commit → assert stored rows), not call commit with hand-built payloads only.
- Cross-format identity is deliberately out of scope for the hash; the only mitigation is the warning. Tests should pin that contract rather than imply stronger behavior.
- Test conventions to reuse: `AuthApiFactory` + `TestClientHelpers.CreateAuthenticatedClientAsync`, antiforgery token helper, `CreateAccountAsync`, `SeedTransactionsAsync` (`ImportEndpointsTests.cs:26-93,1097`). The file is already 1388 lines; new tests likely belong in a new file reusing these helpers (they are `private static` today, so extraction is needed).

## Historical Context (from prior changes)

Not inspected: `context/changes/**` and `context/archive/**` were not searched for earlier dedup decisions. Code comments cite shape-notes pre-decision of the hash formula (`DedupHash.cs:6-8`) and a prior crash fix (`f1bc154`, regression test at :221). Treated as unverified beyond the comments.

## Related Research

None for this change. Phase 2 (parser correctness) will share the paired-fixture concern.

## Open Questions

1. **Product decision for Risk #4:** is "warn only" the accepted behavior, or should the plan push for a rule that blocks or auto-flags cross-format overlap? The test plan's "does not inflate totals" is unattainable under current behavior. Recommend rewording to "warns, and the rows the user keeps are exactly what is stored".
2. **M > N repeated rows:** is losing or double-keeping the Nth repeated identical transaction an accepted limitation or a bug to fix? Tests can document current behavior, but expected values here should come from the statement semantics (two coffees = two rows), so the test would fail today if it asserts correctness. Needs a decision before `/10x-plan` marks it as a red test or a documented-limitation test.
3. **Edit-then-reimport:** same question; likely a real gap, small scope in Phase 1 if accepted.
4. Erste CSV vs PDF description relationship and cross-delimiter hash equality were not verified.
5. Whether the import page disables Continue while committing (double submit) was not verified.
6. The 30/27 commit counts in the plan were not reproduced (16/17 here).
