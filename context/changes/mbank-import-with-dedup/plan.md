# mBank Import With Duplicate Resolution — Implementation Plan

## Overview

Implement S-01: the user can upload an mBank CSV export, see any transaction whose dedup hash collides with an existing one shown side-by-side (existing vs. incoming) to decide skip/keep, and see an import summary (imported / skipped-duplicate / skipped-error counts) after committing. This is the first slice to add domain data (transactions) on top of the existing auth scaffold, and the first to exercise file upload, CSV parsing, and the dedup mechanism that S-02 (manual entry) will later reuse unchanged.

## Current State Analysis

No transaction, category, import, or CSV-parsing code exists anywhere in the repo (verified in `research.md`'s Summary). The only backend code is the auth scaffold (F-01, `impl_reviewed`): `AppUser : IdentityUser<Guid>`, `AppDbContext : IdentityUserContext<AppUser, Guid>` with an explicit placeholder comment for domain `DbSet`s, cookie-based `SignInManager` sessions, and a custom-header (`X-XSRF-TOKEN`) antiforgery pattern applied manually per-endpoint via `AddEndpointFilter`. `Program.cs` registers the antiforgery *service* but never calls the antiforgery *middleware* (`app.UseAntiforgery()`) — confirmed by empirical test in research to 500 any `IFormFile`-binding endpoint regardless of token validity. No CSV library is referenced in any `.csproj`. `MyFinances.Api.csproj` targets `net10.0`, which has no built-in support for code page encodings (`Encoding.GetEncoding(1250)` throws `NotSupportedException` until `System.Text.Encoding.CodePages` is referenced and `Encoding.RegisterProvider` is called).

A real mBank sample file was analyzed byte-for-byte (see `research.md`'s "Real mBank CSV export analysis"): Windows-1250 encoding, `;` delimiter, a variable-length non-tabular preamble (letterhead, account metadata) before the real `#`-prefixed transaction header at a variable line, Polish number format (`14 501,55`), ISO dates in the transaction rows, single-quote-wrapped account numbers, and a real, non-theoretical dedup-hash collision between two distinct same-day/same-amount/same-description BLIK P2P transfers.

## Desired End State

A logged-in user can: upload an mBank CSV (or have the file rejected with a clear "couldn't recognize this as an mBank export" error and a manual bank-selection fallback), see a review screen listing every row whose dedup hash collides with an already-stored transaction (side-by-side existing vs. incoming) with a skip/keep choice per collision, commit the import, and see a summary of imported / skipped-duplicate / skipped-error counts. All persisted transactions and import batches are scoped to the logged-in user.

### Key Discoveries:

- `MyFinances/backend/AppDbContext.cs:6-11` — placeholder confirms domain `DbSet`s belong directly on `AppDbContext`, not a second context.
- `MyFinances/backend/Auth/AuthEndpoints.cs:59-72` — the manual antiforgery-filter pattern used by `/logout`; the import *commit* endpoint (JSON body, not form) should follow this same pattern, while the *parse* endpoint (multipart `IFormFile`) gets antiforgery validation automatically from `app.UseAntiforgery()` once added (research: automatic form-binding CSRF, verified to honor the existing custom `X-XSRF-TOKEN` header name).
- `MyFinances/backend/Tests/AuthEndpointsTests.cs:22-63` — reusable `WebApplicationFactory` + EF Core InMemory provider-swap pattern (with the documented Npgsql provider-marker removal gotcha) for Phase 7.
- `MyFinances/frontend/app/lib/api.ts:34-50` — `apiFetch` already omits forcing `Content-Type`, so it should support multipart `FormData` bodies without modification; only call sites need to pass a `FormData` body and skip JSON-stringifying.
- research.md's "Feasibility Verification" section — two setup blockers that must land in Phase 1 before anything else works: `System.Text.Encoding.CodePages` + `Encoding.RegisterProvider`, and `app.UseAntiforgery()` in the middleware pipeline.

## What We're NOT Doing

- Categorization of imported transactions (S-03) — imported transactions land uncategorized.
- Internal-transfer flagging (FR-009, S-03) — not modeled in this slice beyond leaving room for a future nullable field; no auto-detection logic here.
- Revolut/Erste parsers (S-07/S-08) — only `MBankCsvParser` is implemented; the `IBankStatementParser` abstraction is built to support them later without rework.
- Currency filtering for mBank — the real sample export has no currency column to filter on; FR-003's PLN-only filtering applies to Revolut/Erste, not mBank (user decision).
- An import-history browsing UI — `ImportBatch` is persisted for future auditability, but no page lists past batches; only the immediate post-commit summary is shown (user decision).
- Charts, budgets, category averages (S-04/S-05/S-06) — out of scope, downstream slices.
- Manual transaction entry (S-02) — depends on this slice's dedup hash but is a separate change.

## Implementation Approach

Two-endpoint flow: `POST /api/import/mbank/parse` accepts the uploaded file, runs it through `MBankCsvParser`, computes the dedup hash per row against existing stored transactions for the current user, and returns the full parsed row set (with a per-row `IsDuplicate` flag and, for duplicates, the existing colliding transaction) to the frontend — nothing is persisted yet. The frontend holds this payload, renders the side-by-side review screen for any flagged rows, collects skip/keep decisions, and POSTs the full row set + decisions to `POST /api/import/commit`. The commit endpoint re-parses nothing but **re-validates and re-hashes every row itself** rather than trusting client-supplied hashes or duplicate flags (the client is not a trust boundary), persists non-skipped rows as `Transaction`s plus one `ImportBatch` summarizing counts, and returns the final summary. `IBankStatementParser.CanParse`/`Parse` isolates all mBank-specific parsing quirks (encoding, preamble-skip, Polish formats) so a future `RevolutCsvParser`/`ErsteCsvParser` only needs to implement the same interface.

## Phase 1: Backend Foundation

### Overview

Fix the two setup blockers research found, add the domain schema, and establish the user-scoping convention this slice is the first to need.

### Changes Required:

#### 1. Package references

**File**: `MyFinances/backend/MyFinances.Api.csproj`

**Intent**: Add the two packages research confirmed resolve cleanly against `net10.0` from the project's `nuget.org`-only feed.

**Contract**: `PackageReference` entries for `CsvHelper` (33.1.0) and `System.Text.Encoding.CodePages`.

#### 2. Codepage provider registration

**File**: `MyFinances/backend/Program.cs`

**Intent**: Register the Windows-1250 codepage provider once at startup so `Encoding.GetEncoding(1250)` doesn't throw the first time an mBank file is parsed.

**Contract**: Call `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` before the app starts handling requests (top-level statement, alongside other startup registrations).

#### 3. Antiforgery middleware

**File**: `MyFinances/backend/Program.cs`

**Intent**: Add the missing antiforgery middleware so `IFormFile`-binding endpoints don't 500 regardless of token validity (research: confirmed empirically as a hard blocker).

**Contract**: `app.UseAntiforgery()`, positioned after `UseAuthentication()`/`UseAuthorization()` and before endpoint mapping/execution, per the framework's own ordering requirement. Verify the existing `/logout`-style manual antiforgery filter still behaves identically after this middleware is added (it validates independently via `IAntiforgery.ValidateRequestAsync`, which doesn't depend on the middleware).

#### 4. Upload size limits

**File**: `MyFinances/backend/Program.cs`

**Intent**: Cap request/multipart body size to 5 MB (user decision) instead of the much larger Kestrel/FormOptions defaults research flagged as needless attack surface for a file that's realistically a few KB to low hundreds of KB.

**Contract**: `builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 5 * 1024 * 1024)` and `builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 5 * 1024 * 1024)`.

#### 5. Domain entities

**File**: `MyFinances/backend/Transactions/Transaction.cs` (new), `MyFinances/backend/Transactions/ImportBatch.cs` (new)

**Intent**: Persist imported transactions and one row per completed import for future auditability (user decision: `Transaction` + `ImportBatch`, not `Transaction`-only).

**Contract**:
- `Transaction`: `Id (Guid)`, `UserId (Guid, FK to AppUser)`, `Bank (string)`, `Date (DateOnly)`, `Description (string)`, `Amount (decimal)`, `Hash (string)` — **not** a unique constraint, since a colliding hash is an expected, legitimate outcome once the user picks "keep" (user decision) — `ImportBatchId (Guid?, FK, nullable for S-02's later manual entries)`, `CategoryId (Guid?, nullable — S-03)`.
- `ImportBatch`: `Id (Guid)`, `UserId (Guid, FK)`, `Bank (string)`, `ImportedAtUtc (DateTime)`, `ImportedCount (int)`, `SkippedDuplicateCount (int)`, `SkippedErrorCount (int)`.
- Add both `DbSet`s to `AppDbContext`, add a composite (non-unique) index on `Transaction(UserId, Hash)` since every parse/commit does a per-user hash lookup, and generate one EF Core migration.

#### 6. User-scoping convention

**File**: `MyFinances/backend/Transactions/` (wherever the query helper lands — new)

**Intent**: This is the first slice to query per-user domain data (F-01 has no user-scoped data yet, per research's Architecture Insights). Establish one explicit convention other endpoints (and S-02/S-03) reuse: resolve the current user's `Guid` id from `ClaimsPrincipal` the same way ASP.NET Identity already does internally, and scope every domain query with `.Where(x => x.UserId == currentUserId)`.

**Contract**: A small helper (e.g. an extension method on `ClaimsPrincipal` or `HttpContext`) returning the current user's `Guid` id via `UserManager<AppUser>.GetUserId(ClaimsPrincipal)` (parsed to `Guid`) — reused by both import endpoints in Phase 3.

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds with the new packages referenced
- New migration applies cleanly: `dotnet ef database update`
- Existing test suite still passes: `dotnet test`

#### Manual Verification:

- Starting the app and calling `Encoding.GetEncoding(1250)` from a temporary debug path (or the Phase 2 parser once it lands) does not throw
- A multipart POST to a temporary throwaway `IFormFile` test endpoint with a valid antiforgery token succeeds; without a token, returns 400 (not 500)

---

## Phase 2: mBank Parser & Dedup

### Overview

Implement the bank-agnostic parser abstraction, the first concrete parser (`MBankCsvParser`), and the dedup hash — the core domain logic this slice validates.

### Changes Required:

#### 1. Parser abstraction

**File**: `MyFinances/backend/Import/IBankStatementParser.cs` (new), `MyFinances/backend/Import/NormalizedTransaction.cs` (new)

**Intent**: Isolate all bank-specific quirks behind one interface so Revolut/Erste (S-07/S-08) only need a new implementation, never changes to the dedup/endpoint code that consumes parsers generically.

**Contract**: `IBankStatementParser { bool CanParse(Stream fileStream); IReadOnlyList<NormalizedTransaction> Parse(Stream fileStream); string BankName { get; } }`. `NormalizedTransaction { DateOnly Date, string Description, decimal Amount }` — no bank-specific fields leak through this shape (per research's Architecture Insights).

#### 2. mBank parser implementation

**File**: `MyFinances/backend/Import/MBankCsvParser.cs` (new)

**Intent**: Parse the real mBank export shape research reverse-engineered: cp1250 encoding, `;` delimiter, a variable-length preamble before the real header, Polish number/date formats, single-quote-wrapped account numbers.

**Contract**: `CanParse` scans decoded lines for the header row starting with `#Data księgowania` (bank recognition) without consuming the whole stream. `Parse` skips to that line, hands the remainder to `CsvHelper.CsvReader` configured with `;` delimiter and `Encoding.GetEncoding(1250)`, stops at the first row whose `Data księgowania` field doesn't parse as a date (trailing footer), and parses `Kwota` with `CultureInfo("pl-PL")`. A row that throws during parsing (malformed date/amount) is caught, skipped, and counted — not allowed to abort the whole file (user decision: skip + report, don't abort).

```
// Non-obvious: preamble-skip needs a raw line scan before CsvReader ever sees the stream,
// since CsvReader has no concept of "skip until this header appears".
using var reader = new StreamReader(fileStream, Encoding.GetEncoding(1250));
string? line;
while ((line = reader.ReadLine()) is not null && !line.StartsWith("#Data księgowania"))
{ }
// `line` is now the header row; hand `reader` (positioned just after it) to CsvReader.
```

#### 3. Dedup hash

**File**: `MyFinances/backend/Import/DedupHash.cs` (new)

**Intent**: Compute the hash formula shape-notes pre-decided (`userId + date + amount + description + bank`), unchanged, per the user's explicit 2026-09-25 decision (Option C: never add disambiguation fields to the hash itself — collisions are resolved by always routing through the review UI, not by making the hash more unique).

**Contract**: `string ComputeHash(Guid userId, DateOnly date, decimal amount, string description, string bank)` — a pure, deterministic function (e.g. SHA-256 of a stable-delimited concatenation) called identically by both this slice's import path and S-02's manual-entry path later. Not a DB unique constraint (Phase 1, item 5).

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `dotnet test --filter MBankCsvParserTests`
- Parser correctly extracts all real transaction rows from a redacted real-format fixture (user decision: use a redacted real sample, not purely synthetic fixtures), including the two colliding BLIK P2P rows
- Parser correctly skips a deliberately malformed row without aborting the rest of the file
- Dedup hash is identical for two `NormalizedTransaction`-equivalent inputs regardless of call site

#### Manual Verification:

- N/A (pure backend logic, fully covered by automated tests)

---

## Phase 3: Import API Endpoints

### Overview

Wire the parser and dedup hash into the two-endpoint upload → review → commit flow.

### Changes Required:

#### 1. Endpoint module

**File**: `MyFinances/backend/Import/ImportEndpoints.cs` (new)

**Intent**: Follow the existing endpoint-module convention (`Auth/AuthEndpoints.cs`): one static class, one `MapImportEndpoints(this IEndpointRouteBuilder api)` extension method, registered in `Program.cs` alongside `MapAuthEndpoints()`.

**Contract**: `api.MapGroup("/import")`, inheriting `RequireAuthorization()` from the parent `/api` group (no `AllowAnonymous()` — import always requires a session).

#### 2. Parse endpoint

**File**: `MyFinances/backend/Import/ImportEndpoints.cs`

**Intent**: Accept the uploaded file, resolve the parser via `CanParse` (falling back to a manual bank-selection parameter when auto-detection fails — user decision to build this fallback now even though only `MBankCsvParser` exists), compute per-row dedup flags against the current user's existing transactions, and return the full parsed set to the frontend without persisting anything.

**Contract**: `POST /import/parse` accepts `IFormFile file` plus an optional `string? bank` form field (the manual-selection fallback — when auto-`CanParse` fails and `bank` is provided, look up that parser by name instead of erroring). Response: a list of `{ Date, Description, Amount, IsDuplicate, ExistingTransaction? }` rows. Malformed rows the parser already skipped are surfaced as a `skippedErrorCount` in the response, not silently dropped.

#### 3. Commit endpoint

**File**: `MyFinances/backend/Import/ImportEndpoints.cs`

**Intent**: Accept the full row set plus the user's skip/keep decisions and persist the result — re-validating everything server-side rather than trusting the client payload (user decision: client-side round-trip means the server is not a trust boundary on the data it re-receives).

**Contract**: `POST /import/commit` (JSON body, antiforgery via the existing manual `AddEndpointFilter` pattern from `/logout`) accepts `{ Bank, SkippedErrorCount, Rows: [{ Date, Description, Amount, Decision: Keep|Skip }] }`. `SkippedErrorCount` is the value the frontend received from `/import/parse`'s response, echoed straight back — it's purely an informational count for the summary display and never influences which rows get persisted, so it doesn't need server-side re-validation the way row data does. For each `Keep` row, the endpoint **recomputes the dedup hash and duplicate flag itself** (never trusts a client-supplied hash or `IsDuplicate` value) before inserting a `Transaction`; `Skip` rows are counted but not inserted. Persists one `ImportBatch` with `ImportedCount`/`SkippedDuplicateCount` derived from re-validating `Rows`, plus the echoed `SkippedErrorCount`, and returns it as the import summary.

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds
- Endpoint-level unit/integration smoke: parse endpoint returns 400 for an unrecognized file with no `bank` fallback provided, 200 with correct duplicate flags otherwise
- Commit endpoint persists exactly the expected `Transaction`/`ImportBatch` rows for a fixture payload including one collision resolved as "keep" and one as "skip"

#### Manual Verification:

- Uploading the real (redacted) sample file via a manual HTTP client (e.g. `curl`/Postman/Swagger UI) returns the expected parsed rows with the two BLIK collisions flagged
- Committing that payload persists the expected transaction count and summary

---

## Phase 4: Frontend Upload Page

### Overview

Build the file-upload UI, including the manual bank-selection fallback.

### Changes Required:

#### 1. Upload route

**File**: `MyFinances/frontend/app/routes/import.tsx` (new, plus route registration)

**Intent**: A file picker that posts to `/import/parse` via `apiFetch`, and on a "couldn't recognize this file" error, reveals a bank-selection dropdown (currently just "mBank") to retry the request with an explicit `bank` field.

**Contract**: Uses `FormData` (file + optional `bank` field) as the `apiFetch` body — no `Content-Type` override, per the Key Discoveries note that `apiFetch` already supports this. On success, navigates to (or renders) the Phase 5 review screen with the parsed payload held in route/component state.

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes
- N/A — no frontend test framework exists yet in this repo; upload form and bank-fallback dropdown rendering is covered by Manual Verification below instead

#### Manual Verification:

- Uploading the real redacted sample file in the browser succeeds and hands off to the review screen
- Uploading an unrelated/garbage CSV shows the recognition error and reveals the bank-selection dropdown; retrying with "mBank" explicitly selected succeeds

---

## Phase 5: Frontend Duplicate Review Screen

### Overview

Build the single-pass side-by-side review screen for FR-004.

### Changes Required:

#### 1. Review component

**File**: `MyFinances/frontend/app/routes/import.tsx` (or an extracted component)

**Intent**: List every row flagged `IsDuplicate` with the existing and incoming transaction shown side-by-side, and a skip/keep control per row; non-colliding rows are shown too (already decided as "keep" implicitly) so the user sees the full picture before committing.

**Contract**: Local component state holds the full row list plus a per-row decision; the "commit" action (Phase 6) is disabled until every colliding row has an explicit decision (no default skip/keep for collisions — the FR-004 guardrail is "never silently resolve").

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes

#### Manual Verification:

- On a re-upload of an already-imported file, both BLIK rows are shown side-by-side with their respective pre-existing (already-imported) transaction; committing is blocked until both have an explicit decision. (Dedup only fires against already-stored transactions — see the note in Manual Testing Steps — so this scenario requires the file to have been imported once already, not a fresh first upload.)
- Non-colliding rows are visible in the review list without needing a decision

---

## Phase 6: Frontend Commit & Import Summary

### Overview

Wire the commit call and display the final summary.

### Changes Required:

#### 1. Commit wiring

**File**: `MyFinances/frontend/app/routes/import.tsx`

**Intent**: POST the full row set + decisions to `/import/commit` via `apiFetch`, then render the returned summary.

**Contract**: On success, replace the review UI with a summary view showing `ImportedCount` / `SkippedDuplicateCount` / `SkippedErrorCount` from the commit response — matching FR-002's acceptance criterion exactly (no history page, per user decision).

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes
- `npm run build` succeeds

#### Manual Verification:

- Full end-to-end flow in the browser: upload real sample → resolve the two collisions (one keep, one skip) → commit → summary shows correct imported/skipped-duplicate/skipped-error counts matching the sample file's known contents

---

## Phase 7: Integration Tests

### Overview

Cover the full upload → review → commit flow with `WebApplicationFactory`-based tests, reusing the existing auth-test harness pattern.

### Changes Required:

#### 1. Import test factory & fixture

**File**: `MyFinances/backend/Tests/ImportEndpointsTests.cs` (new), `MyFinances/backend/Tests/Fixtures/mbank-sample-redacted.csv` (new)

**Intent**: Reuse the exact `WebApplicationFactory` + EF Core InMemory provider-swap pattern from `AuthEndpointsTests.cs` (including the documented Npgsql provider-marker removal gotcha), register a user via `/api/auth/register` for auth, then exercise `/import/parse` and `/import/commit` against the redacted real-format fixture.

**Contract**: Test cases cover: a fresh import with no existing transactions (all rows import, zero duplicates); a second import of the same file against a database already seeded with one of its rows (confirms the collision is flagged and routed to review, not silently skipped or silently double-counted — the PRD Guardrail this whole slice exists to prove); a malformed-row fixture (confirms skip + count, not abort).

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes, including the new `ImportEndpointsTests`
- Re-importing the same file against a seeded database correctly flags exactly the expected duplicate rows and never silently double-counts (the PRD Guardrail)

#### Manual Verification:

- N/A (fully covered by automated integration tests)

---

## Testing Strategy

### Unit Tests:

- `MBankCsvParser`: preamble-skip, cp1250 decoding, Polish number/date parsing, single-quote-wrapped account number cleanup, malformed-row skip
- `DedupHash`: identical hash for identical inputs regardless of call site; different hash for any differing field

### Integration Tests:

- Full upload → review → commit flow against the redacted real-format fixture, including the confirmed BLIK collision case and a re-import-of-the-same-file scenario proving the Guardrail

### Manual Testing Steps:

1. Register/log in, navigate to the import page
2. Upload the real redacted mBank sample for the first time (empty database); confirm parsing succeeds and the review screen lists every row, none flagged as a duplicate — dedup only checks against already-stored transactions (Phase 3), and nothing has been imported yet, so the sample's two same-day/same-amount/same-description BLIK rows are *not* expected to flag each other on this first pass (see note below)
3. Upload a non-mBank CSV; confirm the recognition-failure error and manual bank-selection fallback appear
4. Commit the first import (no collisions to resolve yet); confirm the summary counts match the file's contents
5. Re-upload the same file; confirm every previously-imported row — including both BLIK rows, each now colliding with its own already-stored counterpart — is flagged as colliding side-by-side with the matching stored transaction; resolve one collision as "keep" and one as "skip"; commit; confirm the summary counts match expectations

> Note: dedup is scoped to "collides with an already-stored transaction" (Desired End State) — it does not compare rows against each other within the same upload batch. Two genuinely distinct transactions that happen to share date/amount/description (like the sample's two BLIK transfers) will both import silently, unflagged, the first time such a file is uploaded; the collision only surfaces on a later upload once one of them is already stored. This is a deliberate scope boundary, not a gap: see Option C in `research.md`'s Open Questions, which routes collisions through review only when a hash matches something already persisted.

## Migration Notes

New EF Core migration adds `Transactions` and `ImportBatches` tables; no existing data to migrate (F-01 has no domain tables yet).

## References

- Related research: `context/changes/mbank-import-with-dedup/research.md`
- Auth scaffold conventions: `MyFinances/backend/Auth/AuthEndpoints.cs`, `MyFinances/backend/Tests/AuthEndpointsTests.cs`
- Frontend API convention: `MyFinances/frontend/app/lib/api.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Backend Foundation

#### Automated

- [x] 1.1 `dotnet build` succeeds with the new packages referenced — 30d6704
- [x] 1.2 New migration applies cleanly: `dotnet ef database update` — 30d6704
- [x] 1.3 Existing test suite still passes: `dotnet test` — 30d6704

#### Manual

- [x] 1.4 `Encoding.GetEncoding(1250)` does not throw after provider registration — 30d6704
- [x] 1.5 Multipart POST with valid antiforgery token succeeds; without token returns 400 (not 500) — 30d6704

### Phase 2: mBank Parser & Dedup

#### Automated

- [x] 2.1 Unit tests pass: `dotnet test --filter MBankCsvParserTests` — 53a139c
- [x] 2.2 Parser correctly extracts all rows from the redacted real-format fixture, including both colliding BLIK rows — 53a139c
- [x] 2.3 Parser skips a malformed row without aborting the rest of the file — 53a139c
- [x] 2.4 Dedup hash is identical for equivalent inputs regardless of call site — 53a139c

### Phase 3: Import API Endpoints

#### Automated

- [x] 3.1 `dotnet build` succeeds — 490f3ac
- [x] 3.2 Parse endpoint returns 400 for unrecognized file with no `bank` fallback; 200 with correct duplicate flags otherwise — 490f3ac
- [x] 3.3 Commit endpoint persists exactly the expected `Transaction`/`ImportBatch` rows for a fixture payload with one "keep" and one "skip" collision — 490f3ac

#### Manual

- [x] 3.4 Manual HTTP client upload of the redacted sample returns expected parsed rows with both BLIK collisions flagged — 490f3ac
- [x] 3.5 Committing that payload persists the expected transaction count and summary — 490f3ac

### Phase 4: Frontend Upload Page

#### Automated

- [x] 4.1 `npm run typecheck` passes — e099558
- [x] 4.2 N/A — no frontend test framework yet; covered by Manual below — e099558

#### Manual

- [x] 4.3 Uploading the redacted sample succeeds and hands off to the review screen — e099558
- [x] 4.4 Uploading a garbage CSV shows the recognition error and bank-selection fallback; retrying with mBank selected succeeds — e099558

### Phase 5: Frontend Duplicate Review Screen

#### Automated

- [x] 5.1 `npm run typecheck` passes — 335f5f1

#### Manual

- [x] 5.2 Both known colliding BLIK rows shown side-by-side with the pre-existing transaction; commit blocked until both have a decision — 335f5f1
- [x] 5.3 Non-colliding rows are visible without needing a decision — 335f5f1

### Phase 6: Frontend Commit & Import Summary

#### Automated

- [x] 6.1 `npm run typecheck` passes — f1bc154
- [x] 6.2 `npm run build` succeeds — f1bc154

#### Manual

- [x] 6.3 Full end-to-end browser flow: upload → resolve collisions → commit → summary shows correct counts — 229323e

### Phase 7: Integration Tests

#### Automated

- [ ] 7.1 `dotnet test` passes including new `ImportEndpointsTests`
- [ ] 7.2 Re-importing the same file against a seeded database flags exactly the expected duplicates and never silently double-counts
