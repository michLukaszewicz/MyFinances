---
date: 2026-09-25T05:38:10Z
researcher: Claude Sonnet 5
git_commit: cd6c22b8ac49af1e3dcd931408cc0216f0b469e9
branch: main
repository: MyFinances (Kurs 10xDev)
topic: "mBank CSV import with duplicate resolution (S-01 / FR-002, FR-004)"
tags: [research, codebase, backend, import, dedup, ef-core, identity]
status: partial
last_updated: 2026-09-25
last_updated_by: Claude Sonnet 5
last_updated_note: "Empirically verified feasibility of CsvHelper/Windows-1250/IFormFile-antiforgery decisions against this project's actual .csproj/NuGet.Config/Program.cs; found two previously-unflagged setup blockers (System.Text.Encoding.CodePages + Encoding.RegisterProvider needed for cp1250; app.UseAntiforgery() middleware missing from Program.cs for IFormFile CSRF to work at all)"
---

# Research: mBank import with duplicate resolution

## Research Question

What does the codebase already have in place, and what conventions/prior decisions
should `/10x-plan` build on, to implement S-01 (mBank CSV import, side-by-side
duplicate review, import summary) per `context/foundation/prd.md` FR-002/FR-004 and
the Guardrail on duplicate detection?

## Summary

There is **no existing transaction, import, category, or CSV-parsing code anywhere in
the repository** (verified by grepping `MyFinances/backend` for `category`/`transaction`/
`import`/`duplicate`/`mbank`/`csv`, excluding build output — [MyFinances/backend](MyFinances/backend)).
This slice starts from a clean domain layer. The only backend code that exists is the
auth scaffold (F-01, already `impl_reviewed`): `AppUser`/`AppDbContext` (Identity-based,
`Guid` keys, Postgres via Npgsql) and cookie-session auth endpoints under `/api/auth/*`.
No CSV library (e.g. CsvHelper) is referenced in any `.csproj` in the repo.

Two prior architectural decisions for this exact slice are recorded in
`context/foundation/shape-notes.md`'s `## Forward: technical-roadmap` section (carried
into `context/foundation/roadmap.md`'s S-01 entry) and are NOT yet implemented:
1. A `IBankStatementParser` abstraction (`CanParse`/`Parse`, one implementation per
   bank, header-based bank recognition with manual-selection fallback, normalizing to
   a common `NormalizedTransaction { Date, Description, Amount }`).
2. A dedup hash computed from `userId + date + amount + description + bank`, intended
   to be identical for manually-entered and imported transactions (this parity matters
   for FR-010/S-02 later, but the hash shape should be chosen now so S-02 doesn't need
   to change it).

The auth scaffold (F-01) establishes concrete, reusable conventions this slice should
follow: minimal-API endpoint modules grouped under `/api` via `MapGroup`, EF Core with
`AppDbContext : IdentityUserContext<AppUser, Guid>` (domain `DbSet`s are meant to be
added here per the comment at [AppDbContext.cs:6-8](MyFinances/backend/AppDbContext.cs#L6-L8)),
`WebApplicationFactory<Program>` + EF Core InMemory provider for integration tests, and
`apiFetch`/`ApiError` on the frontend for typed API calls with CSRF-token handling on
mutating requests.

**Gap**: the exact mBank CSV export format (columns, separator, encoding) is an
explicitly open unknown in the roadmap (S-01's `Unknowns`), not resolved by this
codebase research — it requires either a real sample file from the user or external
research, both out of this skill's scope.

## Detailed Findings

### Existing backend structure (auth scaffold, F-01)

- [MyFinances/backend/AppDbContext.cs](MyFinances/backend/AppDbContext.cs) — `AppDbContext : IdentityUserContext<AppUser, Guid>(options)`. Comment explicitly states: "Domain models (transactions, categories, ...) are added with the real feature implementation" (line 7) — this is the intended extension point; there is no separate "domain" DbContext.
- [MyFinances/backend/AppUser.cs](MyFinances/backend/AppUser.cs) — `AppUser : IdentityUser<Guid>` with `CreatedAtUtc`. Any per-user entity (transactions, import batches) should FK to `Guid` `AppUser.Id`.
- [MyFinances/backend/Program.cs](MyFinances/backend/Program.cs) — single `var api = app.MapGroup("/api").RequireAuthorization();` (line 84), and endpoint modules register themselves via an `IEndpointRouteBuilder` extension method (`api.MapAuthEndpoints()`, line 86). A new `MapImportEndpoints()`/`MapTransactionEndpoints()` following the same pattern is the established convention. All endpoints under `/api` require auth by default (`RequireAuthorization()` at the group level); anonymous endpoints opt out explicitly with `.AllowAnonymous()` (see [AuthEndpoints.cs:40](MyFinances/backend/Auth/AuthEndpoints.cs#L40)).
- [MyFinances/backend/Auth/AuthEndpoints.cs](MyFinances/backend/Auth/AuthEndpoints.cs) — endpoint module pattern: `static class` with one `Map*Endpoints(this IEndpointRouteBuilder api)` method, nested `MapGroup` per sub-area (e.g. `/auth`), request DTOs as `record`s at the bottom of the file. Mutating endpoints that aren't login/register (e.g. `/logout`) require the antiforgery header via an `AddEndpointFilter` — a CSV-import POST endpoint (mutating, already-authenticated) should follow the same antiforgery pattern as `/logout`, not the anonymous pattern used by `/register`/`/login`.
- No `Category`, `Transaction`, `Import`/`ImportBatch`, or CSV-parsing type exists anywhere under `MyFinances/backend` (confirmed by grep across `.cs` files, excluding `obj`/`bin`).
- No EF Core migration beyond `20260923083801_InitialIdentitySchema` exists ([MyFinances/backend/Migrations/](MyFinances/backend/Migrations)) — domain tables (transactions, import batches) will be a new migration on top of this.

### Package/dependency baseline

- [MyFinances/backend/MyFinances.Api.csproj](MyFinances/backend/MyFinances.Api.csproj) currently references only `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Swashbuckle.AspNetCore` — no CSV parsing library (e.g. CsvHelper) is present in this file or any other `.csproj` in the repository (grep for `csvhelper`, case-insensitive, across all `.csproj` files returned no matches). A CSV library choice is unresolved and belongs to `/10x-plan` (with external research backing, per this project's research-chain convention), not to this internal-research pass.
- [MyFinances/backend/Tests/MyFinances.Api.Tests.csproj](MyFinances/backend/Tests/MyFinances.Api.Tests.csproj) — xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Microsoft.EntityFrameworkCore.InMemory`, referencing the API project directly. The same harness (`WebApplicationFactory<Program>` + a per-test-run InMemory database) is reusable for import-endpoint tests.

### Test harness pattern (reusable for import endpoints)

- [MyFinances/backend/Tests/AuthEndpointsTests.cs](MyFinances/backend/Tests/AuthEndpointsTests.cs) defines `AuthApiFactory : WebApplicationFactory<Program>` (lines 22-63) that swaps the Npgsql-backed `AppDbContext` for a uniquely-named EF Core InMemory database per test-factory instance, and injects config (`Auth:AllowedEmail`) via `ConfigureAppConfiguration`. The comment at lines 40-46 documents a specific EF Core gotcha (removing only `DbContextOptions<AppDbContext>` isn't enough — provider-marker services registered by `UseNpgsql`'s "external service provider" mode must also be stripped, or EF Core throws "multiple database providers registered"). Any new integration-test factory for import endpoints should reuse this exact removal logic rather than rediscovering the gotcha.
- Tests authenticate by registering a user through `/api/auth/register` first (e.g. [AuthEndpointsTests.cs:82-93](MyFinances/backend/Tests/AuthEndpointsTests.cs#L82-L93)), then reuse the resulting cookie-bearing `HttpClient` — the same approach applies to testing an authenticated import endpoint.

### Frontend API/session conventions

- [MyFinances/frontend/app/lib/api.ts](MyFinances/frontend/app/lib/api.ts) — `apiFetch<T>(path, init)` is the sole API entry point: it fetches a fresh antiforgery token before every mutating request (POST/PUT/PATCH/DELETE, line 3 `MUTATING_METHODS`) and sets `credentials: "include"`. A CSV-upload endpoint is a mutating (POST) request, so it will go through this same path — note `apiFetch` currently JSON-serializes/parses bodies implicitly via `fetch`'s `init.body` passthrough and `response.json()`; a `multipart/form-data` file upload will need `init.headers` to NOT set `Content-Type` (browser sets the multipart boundary) while still merging in the CSRF header set at line 39 — `apiFetch`'s `Headers` merge (`new Headers(init?.headers)`, line 36) should already support this since it doesn't force a `Content-Type`.
- No transaction/import-related routes or components exist under [MyFinances/frontend/app/routes/](MyFinances/frontend/app/routes) yet (only `home`, `login`, `register`).

### Prior architectural decisions for this exact slice (not yet implemented)

From `context/foundation/shape-notes.md` lines 147-154 (`## Forward: technical-roadmap`), carried forward into `context/foundation/roadmap.md`'s S-01 entry:

- **Parser abstraction**: `IBankStatementParser` with `CanParse`/`Parse` methods, one implementation per bank (`MBankCsvParser` first; `RevolutCsvParser`, `ErsteBankCsvParser` come later per S-07/S-08), bank recognition by CSV column headers with a manual-selection fallback when recognition is ambiguous, normalizing all banks to a common `NormalizedTransaction { Date, Description, Amount }` shape.
- **Dedup hash**: computed from `userId + date + amount + description + bank`. The shape-notes text ties this explicitly to FR-010 (manual transactions must hash identically to imported ones) — this slice (S-01) is what first establishes the hash, and S-02 (manual entry) depends on reusing it unchanged, so the hash formula chosen here has a real downstream consumer already on the roadmap.
- **To verify before implementation** (explicit, unresolved unknowns per shape-notes line 153 and roadmap's S-01 `Unknowns`): the real mBank CSV export column layout/separator/encoding from "Płatności → Historia → Zestawienie operacji". This is out of scope for codebase research — it needs either a real sample file supplied by the user, or external research into mBank's export format documentation.

### Historical stack drift — confirmed intentional (user, 2026-09-25)

`shape-notes.md` (lines 39-41, 132-145) records the *original* intended stack as JWT-based auth and SQL Server + EF Core. The *actual* implemented F-01 auth (already `impl_reviewed`, see [AuthEndpointsTests.cs](MyFinances/backend/Tests/AuthEndpointsTests.cs)) uses cookie-based `SignInManager`/Identity sessions with antiforgery tokens, not JWT, and the actual database is Postgres via Npgsql (confirmed in `MyFinances.Api.csproj` and `AppDbContext.cs`), not SQL Server.

The user confirmed both deviations were deliberate, not drift-by-omission:
- **Postgres over SQL Server**: Neon (the chosen Postgres host, see `render.yaml`/deploy history) offers a free tier; SQL Server has no equivalent free-hosting option for this solo/after-hours MVP.
- **Cookie session over JWT**: chosen to keep auth as simple as possible for a single-user MVP — no token refresh/storage handling on the frontend, `SignInManager`'s built-in cookie lifecycle covers "session persists across visits" (FR-001) directly.

This supersedes shape-notes.md's stack description going forward; `context/foundation/tech-stack.md` (the actual hand-off) already reflects Postgres, and this research document now records the *why*. `/10x-plan` for this slice should follow the actual implemented auth/session pattern (cookie + antiforgery) and Postgres, not the JWT/SQL-Server description still sitting in shape-notes.md.

## Code References

- `MyFinances/backend/AppDbContext.cs:6-11` - Identity-only context; explicit placeholder comment for domain models
- `MyFinances/backend/AppUser.cs:1-8` - `Guid`-keyed user, FK target for any per-user domain entity
- `MyFinances/backend/Program.cs:84-86` - `/api` group with `RequireAuthorization()`, endpoint-module registration pattern
- `MyFinances/backend/Auth/AuthEndpoints.cs:12-14,59-72` - endpoint-module shape; antiforgery-filter pattern for authenticated mutating endpoints
- `MyFinances/backend/Tests/AuthEndpointsTests.cs:22-63` - reusable `WebApplicationFactory` + EF Core InMemory swap pattern, with the Npgsql provider-marker removal gotcha documented inline
- `MyFinances/backend/MyFinances.Api.csproj:12-18` - current package set; no CSV library present
- `MyFinances/frontend/app/lib/api.ts:34-50` - `apiFetch`, CSRF-token-per-mutation, credential-included fetch wrapper

## Architecture Insights

- Endpoint modules are one static class per feature area under a feature folder (`Auth/AuthEndpoints.cs`), each exposing a single `Map*Endpoints` extension method — a `MyFinances/backend/Import/ImportEndpoints.cs` (or similar) following this exact shape is the path of least surprise.
- `AppDbContext` is the single EF Core context for the whole app (Identity + domain combined) by design — the placeholder comment at `AppDbContext.cs:7` confirms domain `DbSet`s are meant to be added directly to it, not split into a second context.
- All non-anonymous endpoints inherit `RequireAuthorization()` from the `/api` group; user-scoping (per PRD Access Control: "each user sees and operates only on their own data") isn't yet demonstrated by any query in this repo (F-01 has no user-scoped data yet) — this slice will be the first to need a `Where(x => x.UserId == currentUserId)`-style scoping convention, so `/10x-plan` should decide that convention explicitly since no precedent exists to reuse.

## Historical Context (from prior changes)

- `context/foundation/shape-notes.md:147-154` — records the `IBankStatementParser` abstraction and the dedup-hash formula (`userId + date + amount + description + bank`) as pre-decided technical direction for this slice, carried from the original (pre-PRD-v3) planning pass. Not implemented anywhere in the current codebase (verified by the grep in Summary) — these are design intentions, not built patterns to imitate.
- `context/foundation/roadmap.md` (S-01 section) — restates the same unknowns (exact mBank CSV format) as still open, and confirms S-01's only prerequisite is F-01 (now `impl_reviewed`), so this slice is unblocked to plan.
- `context/changes/minimal-auth-scaffold/plan.md` and `context/changes/minimal-auth-scaffold/reviews/` — the F-01 plan/review artifacts establish the auth/session/testing conventions summarized above; no mention of transactions, imports, or CSV in either (confirmed by grep).

## Related Research

None — this is the first `/10x-research` invocation found under `context/changes/**` or `context/archive/**` (no other `research.md` exists in the repository as of this run).

### Real mBank CSV export analysis (user-supplied sample, 2026-09-25)

Analyzed a real mBank export ("Elektroniczne zestawienie operacji", EKONTO account, Aug 2026) byte-for-byte and confirmed the following, resolving the format-related part of the Gap noted above. Two versions of the sample were supplied: an initially-provided `76477052_260801_260831_test.csv` (user-edited/redacted) and, after the user asked to double-check, the true original `76477052_260801_260831.csv`. Both were verified byte-for-byte to be Windows-1250 — **the encoding finding below is unaffected by the user's edit**. However the edited copy also differed from the original in a few structural details (unrelated to encoding); those are called out below and this section now reflects the **original** file, which supersedes the initial pass.

- **Encoding is Windows-1250 (cp1250), not UTF-8.** Confirmed at the byte level: `ś` = `0x9C`, `ć` = `0xE6`, decoding correctly under `Encoding.GetEncoding(1250)`. A naive `Encoding.UTF8` `StreamReader` (the default one might reach for) will silently corrupt every Polish diacritic in descriptions/titles rather than throw — this must be an explicit, bank-specific configuration in `MBankCsvParser`, not a hardcoded app-wide default, since Revolut/Erste exports (S-07/S-08) will very plausibly be UTF-8.
- **Delimiter is `;`**, consistent with the shape-notes assumption.
- **The file is NOT a plain tabular CSV — it's a report with a variable-length preamble.** Structure observed:
  1. Lines 1–37: bank letterhead, client name, statement period, account type/currency/number, interest/credit-limit metadata, a turnover summary (Uznania/Obciążenia/Łącznie), and an opening balance (`#Saldo początkowe`) — all `;`-separated but not transaction rows, several using `"..."`-quoted multi-tab fields.
  2. Line 38: the **real transaction header row**, each column name prefixed with `#`: `#Data księgowania;#Data operacji;#Opis operacji;#Tytuł;#Nadawca/Odbiorca;#Numer konta;#Kwota;#Saldo po operacji`.
  3. Lines 39–113: one transaction per row, 8 data columns matching the header plus one trailing empty field (every row, including the header, ends with an extra `;` — a 9th, always-empty column).
  4. Trailing rows: blank lines, a closing-balance row (`#Saldo końcowe`), and a legal-disclaimer footer sentence — none of these match the transaction column shape.
  - **Implication**: `CsvReader.GetRecords<T>()` cannot be pointed at the raw stream from byte 0. `MBankCsvParser` needs a pre-pass that scans lines for the header row (e.g. first line starting with `#Data księgowania`) before handing the remaining stream to CsvHelper, and needs a stop condition for the trailing footer (e.g. stop at first row whose `Data księgowania` field doesn't parse as a date). This preamble-skip is exactly the kind of per-bank quirk the `IBankStatementParser`/`CanParse` abstraction is meant to isolate — confirms the interface design from shape-notes is the right shape, not just a nice-to-have.
- **`Tytuł` and `Nadawca/Odbiorca` are standard double-quoted CSV fields** (e.g. `"NA JEDZENIE"`, `"  "`) — CsvHelper's default quote-handling parses these correctly with no extra cleanup. (The user-edited copy had stripped these quotes, which would have wrongly suggested no special handling was needed — corrected here from the original file.)
- **Numbers are Polish-formatted**: space as thousands separator, comma as decimal separator (e.g. `14 501,55`, `-1 697,98`). The transaction `Kwota` column itself has no currency suffix (unlike some metadata-block amounts, which append ` PLN`), but still needs `CultureInfo("pl-PL")` (or an equivalent `NumberStyles`/`NumberFormatInfo` override) to parse correctly — `decimal.Parse("−1 697,98", CultureInfo.InvariantCulture)` would throw or misparse.
- **Transaction dates (`Data księgowania`/`Data operacji`) are ISO `yyyy-MM-dd`** (e.g. `2026-08-01`) in the original file — note this differs from the statement-period line (`#Za okres:` → `01.08.2026;31.08.2026`, `dd.MM.yyyy`) elsewhere in the *same* file, so date-format parsing must be scoped per-field/per-section, not assumed uniform across the whole document. (Corrected from the initial pass, which read `dd.MM.yyyy` off the user-edited copy — that copy had reformatted the transaction dates; the original is ISO.) `Data księgowania` and `Data operacji` are identical on every row in this sample; the actual card-transaction date (when it differs, e.g. weekend purchases posted days later) is only available embedded as free text inside `Tytuł` (`...DATA TRANSAKCJI: 2026-08-05`), not as a separate structured column.
- **`Numer konta` is wrapped in single quotes** even when empty (`''`) — needs `.Trim('\'')`, not part of CsvHelper's own quoting (these are literal single-quote characters inside a `;`-delimited field, unrelated to CSV double-quote escaping).
- **Concrete, real evidence of the false-duplicate collision risk already flagged in Open Questions below**: rows 39 and 40 are two separate, legitimate BLIK P2P transfers — identical date (`2026-08-01`), identical amount (`-500,00`), identical description (`BLIK P2P-WYCHODZĄCY` / `Tytuł` `"NA JEDZENIE"`), zero other distinguishing structured field (`Nadawca/Odbiorca` and `Numer konta` are both blank for BLIK P2P). The shape-notes dedup-hash formula (`userId + date + amount + description + bank`) **would collide on these two real rows** and either silently drop one as a false duplicate or require row-order/sequence-number tie-breaking that isn't in the formula today. This is no longer a theoretical risk — `/10x-plan` must decide how the hash (or the duplicate-review UI) handles same-day/same-amount/same-description repeats before this ships.
- **Relevance to future extensibility (PDF, other banks)**: everything CsvHelper-specific here (encoding, delimiter, preamble-skip, quoted-field cleanup, Polish number/date formats) is real evidence that these quirks belong entirely *inside* `MBankCsvParser`, behind the already-planned `IBankStatementParser.CanParse`/`Parse` interface — none of it should leak into `NormalizedTransaction` or the import-endpoint/dedup code that consumes parsers generically. A future `PdfStatementParser` (or a Revolut/Erste CSV parser with different quirks) only needs to implement the same interface and produce the same `NormalizedTransaction { Date, Description, Amount }` shape; CsvHelper itself stays an implementation detail of the CSV-based parsers and never becomes a dependency of the parser abstraction, the dedup logic, or the import endpoint.

## Feasibility Verification (empirical, 2026-09-25)

Following the user's request to check whether everything recorded above actually works in this project, three concrete claims were tested empirically (not just read from docs) against this repo's actual `.csproj`/`NuGet.Config`/`Program.cs`, using scratch throwaway projects outside the repo (no repo files were modified for this check):

- **CsvHelper resolves cleanly against this project's exact target framework.** [MyFinances/backend/NuGet.Config](MyFinances/backend/NuGet.Config) restricts package restore to `nuget.org` only (`<clear />` + a single source) — confirmed this doesn't block CsvHelper: `dotnet restore` against `net10.0` with `PackageReference Include="CsvHelper" Version="33.1.0"` succeeds from `nuget.org` with no conflicts. CsvHelper 33.1.0's `.nuspec` has no `net10.0`-specific asset group, but ships `net8.0`/`net9.0`/`.NETStandard2.0`/`.NETStandard2.1` groups — NuGet's framework-compatibility resolution picks a compatible asset automatically, verified by the successful restore rather than assumed from the nuspec alone.

- **New, previously-unflagged blocker found: `Encoding.GetEncoding(1250)` throws by default on this project's target framework.** Modern .NET (5+, including this project's net10.0) only ships Unicode/ASCII/Latin1 encodings out of the box — code pages like Windows-1250 require the separate `System.Text.Encoding.CodePages` package plus an explicit `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` call (typically once at startup). Verified directly: a throwaway net10.0 console app calling `Encoding.GetEncoding(1250)` with no registration threw `NotSupportedException: No data is available for encoding 1250`; adding the `System.Text.Encoding.CodePages` package (resolves cleanly the same way as CsvHelper) and calling `RegisterProvider` first fixed it (`GetEncoding(1250)` then correctly returns "Central European (Windows)"). **This means the encoding plan recorded above in "Real mBank CSV export analysis" is incomplete as written** — `/10x-plan` needs to add `System.Text.Encoding.CodePages` to `MyFinances.Api.csproj` and register the provider (e.g. in `Program.cs` or a static initializer used by `MBankCsvParser`), or `Encoding.GetEncoding(1250)` will throw at runtime the first time an mBank file is imported.

- **New, previously-unflagged blocker found: the automatic antiforgery protection for `IFormFile`/form binding (cited from Context7 docs above) requires `app.UseAntiforgery()` middleware, which [Program.cs](MyFinances/backend/Program.cs) does not currently call anywhere.** [Program.cs](MyFinances/backend/Program.cs) registers the antiforgery *service* (`AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN")`, line 66) but never adds the antiforgery *middleware* to the pipeline — the current app only validates tokens by explicitly calling `antiforgery.ValidateRequestAsync(...)` inside a manual `AddEndpointFilter`, as seen on `/logout` ([AuthEndpoints.cs:59-72](MyFinances/backend/Auth/AuthEndpoints.cs#L59-L72)). Verified with a throwaway minimal-API app mirroring this project's exact `AddAntiforgery` config and an `IFormFile`-binding endpoint: **without `app.UseAntiforgery()`, every request to the endpoint throws an unhandled `InvalidOperationException` ("contains anti-forgery metadata, but a middleware was not found") and returns 500**, regardless of whether a valid token was sent. After adding `app.UseAntiforgery()` to the pipeline (positioned per the framework's own requirement — after `UseAuthentication`/`UseAuthorization`, before endpoint execution), the same custom `X-XSRF-TOKEN` header this project already uses on the frontend was verified end-to-end: no cookie/header → 400; cookie + correct header → 200; cookie without header → 400. **Conclusion**: the automatic form-binding CSRF protection is real and does honor the project's existing custom header name (so the frontend's existing `apiFetch` token-fetch-and-header pattern does not need to change for the import endpoint), but `/10x-plan` must explicitly add `app.UseAntiforgery()` to `Program.cs`'s middleware pipeline — it is not implied by the existing `AddAntiforgery`/manual-filter setup, and omitting it turns every multipart import request into an unhandled 500 rather than a clean 400.

**Net effect on the plan**: none of the decisions already recorded above (CsvHelper, Windows-1250, `IFormFile` binding, hash-collision Option C) need to change — all three are still realizable in this codebase — but two concrete, previously-invisible setup steps must be added to `/10x-plan`'s scope: (1) reference `System.Text.Encoding.CodePages` and call `Encoding.RegisterProvider` before parsing mBank files, and (2) add `app.UseAntiforgery()` to `Program.cs` before the import endpoint can accept authenticated multipart uploads at all.

## Open Questions

- ~~Exact mBank CSV export column layout, delimiter, and text encoding~~ — **Resolved (real sample file, 2026-09-25)**: see "Real mBank CSV export analysis" above. Windows-1250 encoding, `;` delimiter, variable-length non-tabular preamble before the real `#`-prefixed header row at line 38, Polish number/date formats, single-quote-wrapped `Numer konta`.
- **Confirmed with real data (2026-09-25), not just theoretical**: the dedup-hash fields from shape-notes (`userId + date + amount + description + bank`) **do collide** on real mBank data — two genuinely distinct BLIK P2P transfers in the sample file share identical date, amount, and description with no other distinguishing field. The export has no timestamp, only a date, so no field in the data can distinguish these two rows from each other — any resolution has to be a policy choice, not a data fix.

  **Decision (user, 2026-09-25): Option C — do not resolve the collision in the hash formula at all.** Keep the hash as `userId + date + amount + description + bank` unchanged (preserves FR-010 manual/imported hash parity exactly, zero added domain concept). When a hash collides with an existing transaction, always route it through FR-004's side-by-side duplicate review UI for the user to decide ("these are two different transactions" vs. "this is a real duplicate, skip it") — never auto-skip and never auto-accept a colliding hash. Two alternatives were considered and rejected:
  - *Occurrence/sequence-count in the hash* (rejected): would silently accept a third identical entry as "new" (false negative — a real duplicate slips through unnoticed) and would make the FR-004 review UI never trigger for this case at all, defeating its purpose.
  - *Include `Saldo po operacji` (running balance) in the hash* (rejected): would technically distinguish the two sample rows (`6 789,70` vs `6 289,70`), but breaks FR-010 — a manually-entered transaction has no running-balance value, and the app doesn't model account balance anywhere else, so this would require adding a whole new domain concept just to compute a hash field that only CSV imports can ever populate.
  - Trade-off accepted: this can surface the same-looking-transaction case to the user more often than strictly necessary (e.g. recurring same-amount purchases at the same merchant on the same day) — judged acceptable since it's rare in practice (1 case in the 75-row sample file) and FR-004's review UI exists specifically for this kind of ambiguity.
- ~~Which CSV parsing library to use~~ — **Resolved (external research, Context7, 2026-09-25)**: **CsvHelper** (`/joshclose/csvhelper`), confirmed available in Context7 with 790 code snippets and High source reputation. Chosen as first pick over hand-rolled parsing or other alternatives (alternatives not evaluated — CsvHelper's Context7 coverage was sufficient to proceed). Not yet added to `MyFinances.Api.csproj`. Usage patterns relevant to `MBankCsvParser`, pulled from Context7 (`/joshclose/csvhelper`):
  - **Custom delimiter**: set via `CsvConfiguration.Delimiter` (e.g. `;` for mBank, since Polish bank exports commonly use semicolon) and pass the config into `CsvReader`'s constructor.
  - **Custom encoding**: pass an `Encoding` into the `StreamReader` that wraps the uploaded file stream, before handing it to `CsvReader` — confirmed by real-sample analysis below that mBank specifically requires `Encoding.GetEncoding(1250)` (Windows-1250), not `Encoding.UTF8`.
  - **Bank recognition via headers (`CanParse`)**: `csv.Read()` + `csv.ReadHeader()` exposes the header row without requiring a full class map — `MBankCsvParser.CanParse` can call this and inspect `csv.HeaderRecord` (or attempt `GetField<T>("ExpectedColumn")` and catch a missing-field failure) to decide whether the uploaded file matches mBank's column layout, before falling back to manual bank selection.
  - **Row-to-`NormalizedTransaction` mapping**: either `ClassMap<T>` with `Map(m => m.Prop).Name("ColumnHeader")` registered via `csv.Context.RegisterClassMap<T>()`, or manual `csv.GetField<T>("ColumnName")` per row inside a `while (csv.Read())` loop — the manual approach is closer to the `IBankStatementParser.Parse` design already implied by shape-notes (parser is bank-specific and normalizes to a shared shape, rather than deserializing directly into `NormalizedTransaction` per bank).
  - **Malformed/missing-column handling**: CsvHelper throws (a `MissingFieldException`-shaped failure, exact type version-dependent) when a mapped/requested field isn't found in a row — useful both for `CanParse`'s header-mismatch detection and for surfacing per-row import errors, rather than silently skipping bad rows.
- ~~Multipart file-upload handling on the ASP.NET Core minimal-API side~~ — **Resolved (external research, Context7, 2026-09-25)**: `/dotnet/aspnetcore.docs`. Key findings for the import endpoint:
  - **Binding**: minimal API supports `IFormFile`/`IFormFileCollection` directly as an endpoint parameter (net7.0+, so available on this project's net10.0) — `app.MapPost("/import", (IFormFile file) => ...)`, no `[FromForm]` attribute needed for a single file; the parameter name must match the form field name sent by the client. Binding the whole request body directly to `IFormFile` without `multipart/form-data` is *not* supported — the frontend must send `FormData`, matching what was already flagged as a requirement for `apiFetch` in the Detailed Findings above.
  - **CSRF/antiforgery is automatic for form binding**: minimal-API form parameters (including `IFormFile`) get the same built-in antiforgery validation as `[FromForm]` — cross-origin form submissions are rejected with 400 before the handler runs. This means the import endpoint does **not** need the same manual `AddEndpointFilter` antiforgery pattern used for JSON mutating endpoints like `/logout` ([AuthEndpoints.cs:40](MyFinances/backend/Auth/AuthEndpoints.cs#L40)) — form binding has its own built-in protection; `/10x-plan` should confirm which mechanism actually fires for this endpoint rather than assuming the JSON-endpoint pattern applies unchanged.
  - **Size limits — two separate settings, both default to values likely too small for nothing/too large for this MVP's needs and must be set explicitly**: Kestrel's `KestrelServerLimits.MaxRequestBodySize` (default 30,000,000 bytes / ~28.6 MB) via `builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = ...)`, and `FormOptions.MultipartBodyLengthLimit` (default 134,217,728 bytes / 128 MB) via `builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = ...)`. A single bank CSV statement is realistically well under 1 MB (the sample file is ~13 KB), so `/10x-plan` should pick a deliberately small explicit limit (e.g. a few MB) rather than leaving either default — both larger defaults are needless attack surface for a file that's never supposed to be that big, and leaving Kestrel's ~28.6 MB default is still 2000x the sample size.
  - **Validation/error handling**: exceeding `MultipartBodyLengthLimit` throws `InvalidDataException` when the form is parsed — needs an explicit catch/handler to turn this into a user-facing import error rather than a raw 500.
  - Not yet investigated: `IFormFile.OpenReadStream()` vs. copying to a buffer first, and how `WebApplicationFactory`-based integration tests (the existing harness pattern from `AuthEndpointsTests.cs`) construct a multipart request — left for `/10x-plan`/`/10x-implement`, not critical enough to block planning.
