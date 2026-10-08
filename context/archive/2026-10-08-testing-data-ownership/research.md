---
date: 2026-10-08T12:00:00+02:00
researcher: Claude (Sonnet 5.5)
git_commit: 8f70871
branch: testing/data-ownership
repository: Kurs 10xDev (MyFinances)
topic: "Rollout Phase 3 (Data ownership, Risk #3): can one user read or change another user's transactions, accounts or imports, and what do existing tests already prove?"
tags: [research, codebase, ownership, authorization, endpoints, test-plan]
status: complete
last_updated: 2026-10-08
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Data ownership (Risk #3)

**Git Commit**: 8f70871 · **Branch**: testing/data-ownership · paths relative to `MyFinances/backend` unless noted.

## Research Question

Risk #3 of `context/foundation/test-plan.md`: user B reads or modifies user A's transactions, accounts or imports. Ground it in code: where is the owner scope applied, which endpoints take a resource id, what do existing tests prove, and what would a convincing test look like. Verify, not accept, the response guidance ("logged in means authorized"; "every query already filters by owner").

## Summary

1. **On the inspected paths, every per-user query is scoped by `UserId` in the endpoint itself.** No shared query filter was found (no `HasQueryFilter` inspected in `AppDbContext`); each of the 7 endpoint files resolves `principal.GetUserId(userManager)` (`Transactions/ClaimsPrincipalExtensions.cs:11`) and writes `.Where(x => x.UserId == userId)` by hand. So "every query already filters by owner" is true today by convention only; one forgotten `Where` in a future endpoint is exactly the failure, and nothing structural prevents it.
2. **All `/api` routes sit under `app.MapGroup("/api").RequireAuthorization()`** (`Program.cs:107`); only `register`, `login` and `antiforgery-token` opt out (`Auth/AuthEndpoints.cs`). "Logged in" is enforced centrally, but says nothing about *whose* data.
3. **Resource-id endpoints (the IDOR surface)**: `PUT/DELETE /api/accounts/{id}`, `PUT/DELETE /api/transactions/{id}`, `PUT /api/categorization/transactions/{id}`, plus account ids carried in bodies/forms: `POST /api/transactions`, `PUT /api/transactions/{id}` body, `POST /api/import/parse` (form) and `POST /api/import/commit` (body). Each resolves the resource with `Id == id && UserId == userId` (or `AnyAsync` on the account) and answers 404 (path ids, import) or 400 "Unknown account." (body ids on transactions). Import batches have no endpoint of their own: they are created only inside `/commit` under the caller's `UserId` (`Import/ImportEndpoints.cs:170`).
4. **List/aggregate endpoints** scope by `UserId`: `GET /api/transactions`, `/api/accounts/`, `/api/categorization/queue`, `/handled`, `/api/dashboard/category-spend`, `/category-income`, `/category-trend`. `TransferDetectionService.DetectAsync(userId, db)` loads only the caller's rows (`Categorization/TransferDetectionService.cs:22`), so a cross-user pair cannot be flagged. The dedup hash includes `userId` and the duplicate lookups filter by `UserId`, so another user's identical row is neither flagged nor shown as an "existing" snapshot. `Category` rows are deliberately global (seeded, not user-owned; `Categorization/Category.cs`), so `GET /api/categorization/categories` is not an ownership surface.
5. **Existing ownership tests are real but thin.** Per-endpoint "other user" tests exist for accounts PUT/DELETE (`Tests/Accounts/AccountEndpointsTests.cs:285,319`), transactions PUT/DELETE/POST (`Tests/Transactions/TransactionEndpointsTests.cs:718,875,895,1004`), categorize PUT + queue/handled (`Tests/Categorization/CategorizationEndpointsTests.cs:221`), import parse/commit (`Tests/Import/ImportEndpointsTests.cs:114,404`), list/dashboard isolation (`TransactionEndpointsTests.cs:197`, `DashboardEndpointsTests.cs:206,373,629`, `CategoryTrendEndpointsTests.cs:230`) and transfer detection (`TransferDetectionServiceTests.cs:63`). Weaknesses targeted by this phase:
   - **The "other user" is a synthetic `Guid` row inserted directly into the DB, never a second authenticated user.** Only `Auth:AllowedEmail` may register (`Auth/AuthEndpoints.cs:16-19`, `Tests/Accounts/AccountEndpointsTests.cs:20-22`). No test runs two real sessions side by side, so a bug in how the user id is resolved from the cookie would be invisible.
   - **In the denial tests inspected (`Put_ForTransactionNotOwnedByCaller_ReturnsNotFound`, `Delete_ForTransactionNotOwnedByCaller_ReturnsNotFound`, `Update_/Delete_ForAnotherUsersAccountId_ReturnsNotFound`) only the status code is asserted**; none re-reads the owner's rows afterwards (the plan's anti-pattern "checking only the status code").
   - **Nothing fails when a *new* endpoint is added without an ownership test.**
6. **Registration is single-user but login is not**: `/api/auth/login` accepts any existing Identity user (`Auth/AuthEndpoints.cs:42`), so a second real user can be created in a test through `UserManager<AppUser>` from the factory's service provider and logged in over HTTP on a separate client. `WebApplicationFactory` clients keep cookies per client, so two clients are two sessions. (Inference; confirmed by the first test run in plan Phase 1.)

## Code References

- `Program.cs:107-115` - `/api` group with `RequireAuthorization()` and endpoint mapping.
- `Transactions/ClaimsPrincipalExtensions.cs:11` - the single user-id resolution convention.
- `Transactions/TransactionEndpoints.cs:50,104,126,175,181,203,245` - owner scope on list, account check, dedup lookups, PUT/DELETE.
- `Transactions/AccountEndpoints.cs:26,49,80,93,116` - owner scope on list/create/update/delete.
- `Categorization/CategorizationEndpoints.cs:42,62,81` - owner scope on queue, handled, categorize.
- `Import/ImportEndpoints.cs:29,93,123,145,163,170,192` - owner scope on parse/commit, hash lookups, batch and transaction creation.
- `Dashboard/DashboardEndpoints.cs:66,100`, `Dashboard/CategoryTrendEndpoints.cs:75` - owner scope on aggregates.
- `Tests/Auth/AuthEndpointsTests.cs:23-80` - `AuthApiFactory` (InMemory DB per instance, `AllowedEmail`).
- `Tests/Support/TestClientHelpers.cs`, `Tests/Support/ImportTestHelpers.cs` - authenticated client, antiforgery token, import helpers.

## Architecture Insights

- Ownership is a per-query convention, not a mechanism. The cheapest durable protection is (a) behavior tests with two real sessions and (b) a structural guard that enumerates the mapped `/api` endpoints and fails when one is neither covered by an ownership test nor classified as global/public.
- EF Core InMemory is sufficient: the risk is a missing `Where`, which InMemory reproduces faithfully; Postgres constraints are irrelevant to ownership.

## Historical Context (from prior changes)

- `context/archive/2026-10-02-testing-import-integrity-dedup/` - Phase 1 helpers (`ImportTestHelpers`, `IAsyncLifetime` setup with `AuthApiFactory`) to reuse.
- `context/foundation/lessons.md` - every test uses Arrange/Act/Assert comments and a shared per-class setup.
- `context/archive/2026-09-25-account-management/` - origin of the "other user via synthetic Guid" helper.

## Open Questions

- Whether `WebApplicationFactory` cookie handling lets two clients hold two sessions against one in-memory host (expected yes; confirmed in plan Phase 1).
- Whether a route-inventory guard can read `EndpointDataSource` from `factory.Services` (expected yes; confirmed in plan Phase 3).
