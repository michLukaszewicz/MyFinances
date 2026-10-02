# Import Integrity and Dedup Test Rollout (Phase 1) Implementation Plan

## Overview

Add backend integration tests that prove, through the real `/api/import/parse` and `/api/import/commit` endpoints, that re-imports are flagged and do not change stored data when the user skips them (Risk #1), and that a PDF imported after a CSV of the same period on the same account raises the overlap warning (Risk #4). No product code changes.

## Current State Analysis

- Dedup is advisory: parse flags rows whose hash exists for the user; commit inserts every `Keep` row and never re-checks (`MyFinances/backend/Import/ImportEndpoints.cs:92-109, 151-200`).
- The hash is `userId|date|amount F2|description|accountId` (`Import/DedupHash.cs:11-24`), non-unique by design (`Transactions/Transaction.cs:6-7`). Manual entries use the same hash (`Transactions/TransactionEndpoints.cs:116`).
- Cross-format overlap is a date-range count of other-format rows on the same account (`ImportEndpoints.cs:115-129`). CSV and PDF descriptions differ (`Import/MBankCsvParser.cs:165` vs `Import/MBankPdfParser.cs:229`), so hashes never match across formats.
- Existing tests cover full re-import only and overlap with seeded dummy rows (`Tests/ImportEndpointsTests.cs:439, 680, 845, 971, 1192-1370`). No test uses a CSV and PDF describing the same transactions; the mBank CSV fixture covers 2026-08-01..05, the PDF fixtures 2026-09-10..10-01.
- Full evidence: `context/changes/testing-import-integrity-dedup/research.md`.

## Desired End State

`dotnet test` from `MyFinances/backend` includes two new test classes. They fail if (a) a flagged-and-skipped re-import changes stored rows, (b) partial overlap flags anything other than the overlapping rows, (c) a real paired CSV+PDF stops raising `MixedFormatOverlapCount > 0`, or (d) cross-format rows start being flagged or silently dropped without the contract being updated. `context/foundation/test-plan.md` §6.2 describes the pattern.

### Key Discoveries:

- "Totals unchanged" is asserted on stored rows and per-account amount sum in the DB; dashboard charts only count categorized, non-transfer rows (`Dashboard/DashboardEndpoints.cs:63-71`).
- `MBankPdfBuilder.Build(header, openingBalance, rows, layout)` builds a PDF from independent row records (used at `Tests/ImportEndpointsTests.cs:883`); the CSV counterpart must be authored from the same records, not from parser output.
- mBank CSV decoding tries strict UTF-8 then Windows-1252 re-encode, else cp1250 (`Import/MBankCsvParser.cs:41-52`); a test CSV writer should emit cp1250 bytes (or ASCII-only text) so Polish characters do not go through the remojibake path.
- `Tests/Import/ImportEndpointsTests.cs` helpers are `private static`; `TestClientHelpers` and `AuthApiFactory` are reusable (`Tests/TestClientHelpers.cs`, `Tests/AuthEndpointsTests.cs:23`).

## What We're NOT Doing

- No product code changes (cross-format matching, multiset-aware duplicate flags, idempotent commit).
- Edit-then-reimport (PUT rewrites the hash, `Transactions/TransactionEndpoints.cs:214-222`): left out by decision; recorded in the research as a known gap.
- No red tests for known limitations; limitations are pinned as characterization tests.
- No Erste or VeloBank cross-format pairs (Erste CSV/PDF description relationship unverified; VeloBank has no CSV).
- No frontend tests, no concurrency/double-submit tests, no Postgres-backed tests (InMemory is sufficient; research.md "Test layer and InMemory").
- No changes to `Tests/Import/ImportEndpointsTests.cs`.

## Implementation Approach

Drive the endpoints the way the client does: parse, derive decisions from `IsDuplicate`, commit, then assert persisted state. Expected values come from independent sources (a known row list authored in the test, the existing fixture's documented contents), never from running the code under test first. Use the existing InMemory `AuthApiFactory`. Two new test classes keep the 1388-line existing file unchanged; a small shared helper class avoids copy-pasting boilerplate.

## Phase 1: Shared import test support

### Overview

Provide the helpers both new test classes need.

### Changes Required:

#### 1. Import test helpers

**File**: `MyFinances/backend/Tests/Support/ImportTestHelpers.cs`

**Intent**: Offer reusable upload, commit and DB-inspection helpers so new tests do not duplicate the private helpers in `ImportEndpointsTests.cs`.

**Contract**: `internal static class ImportTestHelpers` in namespace `MyFinances.Api.Tests.Support` (or `MyFinances.Api.Tests` to match `TestClientHelpers`), exposing: antiforgery token fetch; create account via `/api/accounts/`; multipart upload to `/api/import/parse` for CSV bytes or PDF bytes returning `ImportParseResponse`; commit a `ImportParseResponse` where each row's decision is `Skip` if `IsDuplicate` else `Keep` (and an overload taking an explicit decision per row), sending `SourceFormat` from the parse response; read the user's stored transactions for an account (rows and amount sum); get the registered user's id. Mirrors the request shapes at `Tests/ImportEndpointsTests.cs:26-93, 1310-1324`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build` from `MyFinances/backend`
- Existing import tests still pass: `dotnet test --filter "FullyQualifiedName~ImportEndpointsTests"` from `MyFinances/backend`

#### Manual Verification:

- None (support code only; exercised by Phase 2 and 3)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding to the next phase.

---

## Phase 2: Risk #1 — re-import and partial-overlap integrity

### Overview

Prove re-imports are flagged and, when skipped, leave stored data unchanged; probe the weak points of the dedup key.

### Changes Required:

#### 1. Dedup integrity tests

**File**: `MyFinances/backend/Tests/Import/ImportDedupIntegrityTests.cs`

**Intent**: Cover the behaviors the research found untested, using `AuthApiFactory` with one authenticated user.

**Contract**: One `public class ImportDedupIntegrityTests`; each test a `[Fact]` using a fresh factory. Scenarios (behavior, regression caught):
- Full re-import: import the mBank CSV fixture, commit with Keep; re-upload; every row has `IsDuplicate` with `ExistingTransaction` populated; commit with decisions derived from flags; stored row count and account amount sum equal the first import's (caught: a changed key or a commit path that inserts skipped rows).
- Partial overlap: first import commits a subset of the fixture's four known rows (authored by the test, e.g. the 08-01 pair and 08-02 row); full file then flags exactly those rows (by date, amount, description) and not the others; skipping flagged and keeping the rest yields stored rows equal to the full set (caught: partial overlap treated like full overlap, or non-overlapping rows flagged).
- Skip on a non-duplicate row is not stored and is not counted in `SkippedDuplicateCount` (pins the commit contract at `ImportEndpoints.cs:156-177`).
- Keep on a flagged duplicate stores a second row (pins "user decision wins"; stored count rises by one).
- Manual entry (POST `/api/transactions`, `ImportBatchId` null) with the same date/amount/description/account flags the matching import row.
- Same data under another account of the same user is not flagged (already covered at `ImportEndpointsTests.cs:256`; include only if cheap, otherwise skip).
- Characterization of the repeated-rows limitation: the fixture's two identical BLIK rows (-500.00, 2026-08-01, "NA JEDZENIE"); with exactly one such row stored, parsing the file flags both identical rows. Test name and a comment state this is current behavior and a known limitation, not an endorsement (research.md "Risk #1", Open Question 2).
- Expected counts and rows are written from the authored row list, not read back from a first parse.

### Success Criteria:

#### Automated Verification:

- New class passes: `dotnet test --filter "FullyQualifiedName~ImportDedupIntegrityTests"` from `MyFinances/backend`
- Whole suite passes: `dotnet test` from `MyFinances/backend`

#### Manual Verification:

- Temporarily changing `DedupHash.ComputeHash` (e.g. drop `accountId` or trim description) makes at least one new test fail; revert afterwards

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding to the next phase.

---

## Phase 3: Risk #4 — CSV-vs-PDF overlap with a real paired fixture, and cookbook

### Overview

Prove the overlap warning fires for a PDF and a CSV that describe the same transactions, and pin the warn-only contract.

### Changes Required:

#### 1. Paired fixture builder

**File**: `MyFinances/backend/Tests/Support/MBankPairedStatement.cs`

**Intent**: One independent list of transactions (date, amount, CSV title, PDF description lines) that yields both an mBank PDF and an mBank CSV, so both describe the same period on the same account.

**Contract**: Static class exposing the authored row list (about 5 rows within a single month, none from the existing fixtures), `BuildPdf()` via `MBankPdfBuilder.Build` with a running-balance chain from an opening balance, and `BuildCsv()` writing the mBank CSV layout (preamble, `#Data księgowania` header, `;` delimiter, Polish decimal commas, `yyyy-MM-dd` dates, quoted title column) encoded as cp1250 or ASCII-only. Descriptions differ between formats exactly as real exports do (CSV title vs PDF operation-type line plus title). Format reference: `Tests/Fixtures/mbank-sample-redacted.csv`.

#### 2. Cross-format overlap tests

**File**: `MyFinances/backend/Tests/Import/ImportCrossFormatOverlapTests.cs`

**Intent**: Exercise the full parse, commit, parse sequence with a real pair in both orders.

**Contract**: `public class ImportCrossFormatOverlapTests`; scenarios:
- Fixture sanity: parsing the paired CSV and PDF through the endpoint yields the same multiset of (date, amount) as the authored row list (guards against fixtures that do not describe the same transactions).
- PDF committed, then CSV parsed: `MixedFormatOverlapCount` equals the number of committed rows whose dates fall in the CSV's date range (computed from the authored list), and no CSV row is `IsDuplicate`.
- CSV committed, then PDF parsed: same assertions with the roles swapped.
- Control: re-parsing the same format after commit flags every row (`IsDuplicate`) and `MixedFormatOverlapCount` is 0, so the contrast with cross-format is visible.
- Warn-only contract: committing both with Keep stores rows from both imports (count = CSV rows + PDF rows) and is documented in the test as current behavior, with the double counting visible in the stored sum.
- Partial period: PDF covers the full authored list, CSV only a subset; count reflects only PDF rows inside the CSV's min/max date.

#### 3. Cookbook

**File**: `context/foundation/test-plan.md`

**Intent**: Fill §6.2 "Adding an import / dedup integration test" with the pattern shipped (parse → derive decisions → commit → assert persisted state; independent expected values; paired-fixture rule), and append a §6.4 note for this phase.

**Contract**: Replace the `TBD` bullet under §6.2; keep the reference-test line; keep headings unchanged.

### Success Criteria:

#### Automated Verification:

- New class passes: `dotnet test --filter "FullyQualifiedName~ImportCrossFormatOverlapTests"` from `MyFinances/backend`
- Whole suite passes: `dotnet test` from `MyFinances/backend`

#### Manual Verification:

- Temporarily making the overlap query ignore `SourceFormat` (or return 0) makes the cross-format tests fail; revert afterwards
- §6.2 in `context/foundation/test-plan.md` reads correctly and references the new test files

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human.

---

## Testing Strategy

### Unit Tests:

- None added; `DedupHashTests.cs` already covers hash sensitivity.

### Integration Tests:

- Parse/commit sequences against the InMemory `AuthApiFactory` (Phases 2 and 3).

### Manual Testing Steps:

1. Run mutation checks described in Phase 2 and 3 Manual Verification.
2. Run the full suite once after both phases.

## Performance Considerations

None; each test builds a fresh factory like the existing import tests.

## Migration Notes

None.

## References

- Related research: `context/changes/testing-import-integrity-dedup/research.md`
- Test plan: `context/foundation/test-plan.md` (§2 Risks #1, #4; §6.2)
- Existing patterns: `MyFinances/backend/Tests/ImportEndpointsTests.cs:26-93, 1097-1150, 1310-1324`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared import test support

#### Automated

- [x] 1.1 Solution builds: `dotnet build` from `MyFinances/backend` — 74e3966
- [x] 1.2 Existing import tests still pass: `dotnet test --filter "FullyQualifiedName~ImportEndpointsTests"` from `MyFinances/backend` — 74e3966

### Phase 2: Risk #1 — re-import and partial-overlap integrity

#### Automated

- [x] 2.1 New class passes: `dotnet test --filter "FullyQualifiedName~ImportDedupIntegrityTests"` from `MyFinances/backend` — b625cbf
- [x] 2.2 Whole suite passes: `dotnet test` from `MyFinances/backend` — b625cbf

#### Manual

- [x] 2.3 Temporarily changing `DedupHash.ComputeHash` makes at least one new test fail; revert afterwards — b625cbf

### Phase 3: Risk #4 — CSV-vs-PDF overlap with a real paired fixture, and cookbook

#### Automated

- [x] 3.1 New class passes: `dotnet test --filter "FullyQualifiedName~ImportCrossFormatOverlapTests"` from `MyFinances/backend` — ca21cf2
- [x] 3.2 Whole suite passes: `dotnet test` from `MyFinances/backend` — ca21cf2

#### Manual

- [x] 3.3 Temporarily making the overlap query ignore `SourceFormat` makes the cross-format tests fail; revert afterwards — ca21cf2
- [x] 3.4 §6.2 in `context/foundation/test-plan.md` reads correctly and references the new test files — ca21cf2

## Amendments (during Phase 2)

- Test conventions for new tests: Arrange/Act/Assert comments; shared setup in `IAsyncLifetime` (xUnit's equivalent of `TestInitialize`), so a test's Arrange holds only what differs from the standard setup; neutral descriptions ("Test description N"), no real-looking transfer titles.
- Existing test classes were moved into folders (`Auth/`, `Accounts/`, `Transactions/`, `Categorization/`, `Dashboard/`, `Import/`, `Import/Parsers/`, `Support/`) with the namespace left as `MyFinances.Api.Tests`. Existing tests were not otherwise rewritten.
- `Tests/Support/MBankCsvBuilder.cs` (cp1250 mBank CSV from authored rows) replaces reading `Fixtures/mbank-sample-redacted.csv` in new tests; Phase 3's paired fixture reuses it instead of a CSV writer inside `MBankPairedStatement`.
