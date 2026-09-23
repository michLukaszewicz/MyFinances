# Minimal Auth Scaffold Implementation Plan

## Overview

Add email/password authentication to MyFinances so every API endpoint is scoped to a logged-in user and the session persists across visits, per roadmap item F-01 and PRD FR-001 / Access Control. This is the foundation slice — no downstream feature (import, categorization, charts) can safely persist or read user-scoped data before this lands.

## Current State Analysis

The repo is a freshly-scaffolded split stack with zero auth code:

- [Program.cs](../../../MyFinances/backend/Program.cs) has no authentication/authorization services registered; the only API endpoint (`/api/weatherforecast`) is unauthenticated.
- [MyFinances.Api.csproj](../../../MyFinances/backend/MyFinances.Api.csproj) has no `Microsoft.AspNetCore.Identity.EntityFrameworkCore` package reference.
- [AppDbContext.cs](../../../MyFinances/backend/AppDbContext.cs) is an explicit empty placeholder (`DbContext` with no `DbSet`s).
- Frontend has one route ([routes.ts](../../../MyFinances/frontend/app/routes.ts) → `routes/home.tsx`), no `/login` or `/register`, and [api.ts](../../../MyFinances/frontend/app/lib/api.ts)'s `apiFetch` is a bare `fetch` wrapper with no auth-aware behavior.
- Production serves the SPA and API from the same origin (`UseStaticFiles` + `MapFallbackToFile` in Program.cs); dev proxies `/api/*` from Vite to `http://localhost:5007`. Both cases are effectively same-origin from the browser's perspective, which is what makes cookie-based auth viable without CORS/credential gymnastics.
- No test project exists in either stack yet.

### Key Discoveries:

- `/api/weatherforecast` ([Program.cs:53-65](../../../MyFinances/backend/Program.cs#L53-L65)) is the only existing non-auth endpoint — it becomes the first protected endpoint once the `/api` group requires authorization, and doubles as the test target for "every endpoint requires auth."
- `apiFetch` ([api.ts:3-9](../../../MyFinances/frontend/app/lib/api.ts#L3-L9)) uses a bare `fetch()`, whose default `credentials: "same-origin"` already forwards cookies on same-origin requests — no change needed there. It does need a hook to attach an antiforgery header on mutating (`POST`/`PUT`/`DELETE`) requests.
- `AppDbContext` already has `Npgsql`/EF Core wired to Neon Postgres (`builder.Services.AddDbContext<AppDbContext>` in [Program.cs:19-20](../../../MyFinances/backend/Program.cs#L19-L20)) — the Identity schema rides on the same context and connection string.

## Desired End State

A user can register (email restricted to a single configured allow-listed address), log in, and stay logged in via an HttpOnly cookie that survives a browser restart for up to 30 days of inactivity. Every `/api/*` endpoint requires that cookie except register/login; a logged-out request to any of them (including the existing weatherforecast endpoint) returns 401. The SPA redirects unauthenticated visitors to `/login`, shows a logout control once authenticated, and issues an antiforgery header on mutating requests.

Verification: `dotnet test` passes in a new `MyFinances.Api.Tests` project; manually, a fresh browser session hitting `/` redirects to `/login`, registering with the allow-listed email succeeds, registering with any other email is rejected, logging in sets a persistent cookie, the weatherforecast table renders only when logged in, and logout clears the session.

## What We're NOT Doing

- No password reset / forgot-password flow (not in F-01's scope; PRD doesn't require it for MVP).
- No email verification or email-sending of any kind.
- No account lockout / login rate-limiting (decided: not needed at single-user scale for MVP).
- No roles or multi-user administration surface — flat single-user model per PRD Access Control.
- No OAuth/Google login (explicit PRD non-goal).
- No frontend test tooling (Vitest/RTL) — this change adds backend tests only; frontend auth UI is manually verified.
- No changes to deployment config (`Dockerfile`/`render.yaml`) beyond documenting the one new required env var — this change ships the code; wiring the Render env var at deploy time is an operational step, not a plan phase.

## Implementation Approach

ASP.NET Core Identity (`IdentityUserContext<AppUser, Guid>`, no roles) backs the user store, with cookie authentication (`IdentityConstants.ApplicationScheme`) as the sign-in mechanism instead of Identity's default bearer-token minimal API endpoints — cookies fit the single-origin SPA deployment cleanly and avoid token-storage/XSS tradeoffs. Registration is gated by a single allow-listed email read from configuration, since this app runs on a public URL but is meant for exactly one person. CSRF is handled with ASP.NET Core's built-in antiforgery services (double-submit cookie + header), since a signed-in cookie is sent automatically by the browser on every request. The `/api` route group flips to `RequireAuthorization()` by default, with `/api/auth/register` and `/api/auth/login` explicitly marked `AllowAnonymous()`.

## Phase 1: Identity data model & migration

### Overview

Wire ASP.NET Core Identity into the existing `AppDbContext` and generate the schema migration, without yet exposing any endpoints.

### Changes Required:

#### 1. Identity package reference

**File**: `MyFinances/backend/MyFinances.Api.csproj`

**Intent**: Add the EF Core Identity store package so `AppDbContext` can host Identity's user tables.

**Contract**: Add `<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.*" />` (match the existing `Npgsql.EntityFrameworkCore.PostgreSQL` major/minor version, `10.0.x`) to the existing `<ItemGroup>`. `Microsoft.AspNetCore.Identity` itself and cookie/antiforgery authentication are already part of the shared `Microsoft.NET.Sdk.Web` framework reference — no separate package needed for those.

#### 2. AppUser entity

**File**: `MyFinances/backend/AppUser.cs` (new)

**Intent**: Define the Identity user type. Per the agreed minimal shape, this is `IdentityUser<Guid>` with no additional fields (`Id`, `Email`, `PasswordHash`, `CreatedAt`-equivalent are already covered by the base type — Identity doesn't have a `CreatedAt` column out of the box, so add one field: `CreatedAtUtc`).

**Contract**: `public class AppUser : IdentityUser<Guid> { public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow; }`

#### 3. AppDbContext → IdentityUserContext

**File**: `MyFinances/backend/AppDbContext.cs`

**Intent**: Replace the placeholder `DbContext` base with the roles-less Identity context so `UserManager`/`SignInManager` can operate against it. No roles table is created (flat user model — no roles decision).

**Contract**: `public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<AppUser, Guid>(options)`. Remove the "placeholder" comment; Identity's own tables (`AspNetUsers`, claims, logins, tokens — no `AspNetRoles`) are the only schema this migration introduces.

#### 4. Service registration

**File**: `MyFinances/backend/Program.cs`

**Intent**: Register Identity core services (user manager, password hasher, sign-in manager) bound to `AppUser`/`AppDbContext`, and register cookie authentication under `IdentityConstants.ApplicationScheme` as the app's auth scheme, with a 30-day sliding expiration. Register antiforgery services. This phase only adds service registration — `app.UseAuthentication()`/`app.UseAuthorization()` and endpoint-level `RequireAuthorization()` land in Phase 2 alongside the endpoints that need them.

**Contract**: Add, before `var app = builder.Build();`:
- `builder.Services.AddIdentityCore<AppUser>(options => { options.Password.RequireDigit = false; options.Password.RequireLowercase = false; options.Password.RequireUppercase = false; options.Password.RequireNonAlphanumeric = false; options.Password.RequiredLength = 12; }).AddSignInManager().AddEntityFrameworkStores<AppDbContext>();` — password rules reflect the agreed "minimum length only" decision.
- `builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, options => { options.ExpireTimeSpan = TimeSpan.FromDays(30); options.SlidingExpiration = true; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict; options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }; options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }; });` — the `OnRedirectToLogin`/`OnRedirectToAccessDenied` overrides are required because Identity's cookie handler defaults to a 302 redirect to a login *page*, which is wrong for a JSON API; without this override, an unauthenticated API call returns a confusing redirect instead of a clean 401.
- `builder.Services.AddAuthorization();`
- `builder.Services.AddAntiforgery(options => { options.HeaderName = "X-XSRF-TOKEN"; });`

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds from `MyFinances/backend`
- `dotnet ef migrations add InitialIdentitySchema` generates a migration touching only `AspNetUsers`/claims/logins/tokens tables (no `AspNetRoles`)
- `dotnet ef database update` applies cleanly against the configured Postgres connection

#### Manual Verification:

- Inspect the generated migration file to confirm no unexpected tables/columns beyond Identity's standard user-only schema plus `CreatedAtUtc`

---

## Phase 2: Auth endpoints & global enforcement

### Overview

Expose `/api/auth/register`, `/api/auth/login`, `/api/auth/logout`, `/api/auth/me`, and flip the `/api` group to require authentication by default — making the existing `/api/weatherforecast` endpoint the first protected route.

### Changes Required:

#### 1. Auth configuration value

**File**: `MyFinances/backend/appsettings.json`, `MyFinances/backend/appsettings.Development.json`

**Intent**: Add the single allow-listed registration email as configuration, per the "allow-listed email" registration decision. Development can hold a real value directly since it's not committed with production secrets; production reads the same key from a Render environment variable (`Auth__AllowedEmail`), documented but not wired into `render.yaml` in this change (see "What We're NOT Doing").

**Contract**: Add a top-level `"Auth": { "AllowedEmail": "" }` key (empty in `appsettings.json`, the user's real email in `appsettings.Development.json`, git-ignored or set via user-secrets if preferred over committing it).

#### 2. Auth endpoints

**File**: `MyFinances/backend/Program.cs` (or extracted to `MyFinances/backend/AuthEndpoints.cs` if `Program.cs` gets unwieldy — implementer's call)

**Intent**: Four endpoints under a `/api/auth` group:
- `POST /api/auth/register` — body `{ email, password }`. Rejects (400 ProblemDetails) if `email` doesn't case-insensitively match `Auth:AllowedEmail`, or if `UserManager.CreateAsync` fails (duplicate email, password too short). On success, signs the user in via `SignInManager.SignInAsync` (issues the cookie) and returns 200 with the user's email.
- `POST /api/auth/login` — body `{ email, password }`. Uses `SignInManager.PasswordSignInAsync` (not `CheckPasswordSignInAsync` — the former issues the cookie directly). Returns 401 ProblemDetails on failure (generic "invalid credentials", not "no such user" — avoid user enumeration), 200 with user email on success.
- `POST /api/auth/logout` — `SignInManager.SignOutAsync()`, requires auth, returns 200.
- `GET /api/auth/me` — requires auth, returns `{ email }` for the current `ClaimsPrincipal`. This is the endpoint the frontend loader calls to check session state.

**Contract**: `register` and `login` are `AllowAnonymous()`; `logout` and `me` inherit the group's `RequireAuthorization()`. All four return `ProblemDetails`-shaped errors (RFC 9457, matching the agreed error-shape decision) via `Results.Problem(...)`/`Results.ValidationProblem(...)` — not a custom `{ error }` shape.

#### 3. Global `/api` authorization + antiforgery pipeline

**File**: `MyFinances/backend/Program.cs`

**Intent**: Make every `/api/*` endpoint require authentication by default (the register/login endpoints opt out explicitly), and validate the antiforgery token on authenticated mutating requests.

**Contract**: 
- Change `var api = app.MapGroup("/api");` to `var api = app.MapGroup("/api").RequireAuthorization();`, so `/api/weatherforecast` (already mapped under this group) becomes protected with no changes to its own handler.
- Add `app.UseAuthentication();` and `app.UseAuthorization();` between `app.UseHttpsRedirection();` and the `var api = ...` line (order matters: authentication before authorization, both before endpoint mapping).
- Add a `GET /api/auth/antiforgery-token` endpoint (`AllowAnonymous`) that calls `IAntiforgery.GetAndStoreTokens(HttpContext)` and returns the request token in the response body — the frontend calls this once per session bootstrap to seed the readable half of the double-submit cookie pair.
- Add antiforgery validation as an endpoint filter (`IAntiforgery.ValidateRequestAsync`) applied to `logout` specifically (the only mutating, authenticated endpoint this phase introduces) — `register`/`login` are pre-session and don't yet have a cookie to protect; future write endpoints (S-01 onward) apply the same filter pattern.

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds
- A request to `/api/weatherforecast` with no cookie returns 401
- A request to `/api/auth/register` with a non-allow-listed email returns 400
- A request to `/api/auth/register` with the allow-listed email and a 12+ character password returns 200 and sets a `Set-Cookie` header
- A request to `/api/weatherforecast` with the resulting cookie returns 200
- A request to `/api/auth/login` with wrong credentials returns 401; with correct credentials returns 200 and a cookie
- A request to `/api/auth/logout` without the antiforgery header returns 400/403; with it, returns 200 and the session cookie no longer authenticates subsequent requests

#### Manual Verification:

- Exercise the four endpoints via Swagger UI (`/swagger` in dev) or `curl -c`/`-b` to confirm cookie issuance and 401 behavior end-to-end against the real Neon-backed dev database

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Frontend auth UI & route guarding

### Overview

Add `/login` and `/register` pages, guard the home route behind a session check, add a logout control, and teach `apiFetch` about the antiforgery header.

### Changes Required:

#### 1. Routes

**File**: `MyFinances/frontend/app/routes.ts`

**Intent**: Register two new routes alongside the existing `index`.

**Contract**: `[index("routes/home.tsx"), route("login", "routes/login.tsx"), route("register", "routes/register.tsx")] satisfies RouteConfig` (add `route` to the existing `@react-router/dev/routes` import).

#### 2. Login and register pages

**File**: `MyFinances/frontend/app/routes/login.tsx`, `MyFinances/frontend/app/routes/register.tsx` (new)

**Intent**: Each is a simple form (email + password, and for register, nothing else per the minimal-fields decision) that `POST`s to `/api/auth/login` or `/api/auth/register` via `apiFetch`, shows the ProblemDetails error message on failure, and on success navigates to `/`. A `clientLoader` on each redirects to `/` if `/api/auth/me` already succeeds (already-logged-in visitor shouldn't see the login form).

**Contract**: Standard React Router v8 route module shape (`clientLoader`, default export component) matching the pattern already established in [home.tsx](../../../MyFinances/frontend/app/routes/home.tsx).

#### 3. Protected home route

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Add a `clientLoader` that calls `GET /api/auth/me`; on 401, `throw redirect("/login")` (React Router's server-checked-loader guarding pattern, per the agreed decision). Add a logout button that `POST`s `/api/auth/logout` (with the antiforgery header) then navigates to `/login`.

**Contract**: `export async function clientLoader() { const res = await fetch("/api/auth/me"); if (!res.ok) throw redirect("/login"); return res.json(); }` — direct `fetch` (not `apiFetch`, which throws on non-OK rather than letting the loader inspect the status) is acceptable here since this is the one place that needs to branch on a 401 rather than treat it as an error.

#### 4. `apiFetch` antiforgery header

**File**: `MyFinances/frontend/app/lib/api.ts`

**Intent**: For mutating requests (`init.method` is `POST`/`PUT`/`PATCH`/`DELETE`), attach the antiforgery token as a header. The token is read from the non-HttpOnly cookie the backend's `/api/auth/antiforgery-token` endpoint sets (per Phase 2) — no extra fetch needed if that cookie is already present; call the antiforgery-token endpoint once at app bootstrap (e.g. in `root.tsx`'s loader) to guarantee the cookie exists before any mutating call.

**Contract**: `apiFetch` reads a cookie named e.g. `XSRF-TOKEN` (readable, non-HttpOnly — set by `GetAndStoreTokens`) and, for mutating methods, sets the `X-XSRF-TOKEN` request header to its value.

### Success Criteria:

#### Automated Verification:

- `npm run typecheck` passes from `MyFinances/frontend`
- `npm run build` succeeds

#### Manual Verification:

- Visiting `/` while logged out redirects to `/login`
- Registering with the allow-listed email succeeds and lands on `/`; registering with any other email shows an error
- Registering a second time with the same email shows a duplicate-email error
- Logging in with correct/incorrect credentials behaves correctly
- The weatherforecast table renders on `/` once logged in
- Clicking logout returns to `/login`, and a subsequent visit to `/` redirects to `/login` again (cookie no longer valid)
- Closing and reopening the browser (not just the tab) within 30 days keeps the session logged in

---

## Phase 4: Backend auth tests

### Overview

Add the repo's first test project, covering the auth endpoints and the now-protected weatherforecast endpoint as an integration smoke test.

### Changes Required:

#### 1. Test project

**File**: `MyFinances/backend/MyFinances.Api.Tests/MyFinances.Api.Tests.csproj` (new)

**Intent**: A standard xUnit test project referencing `Microsoft.AspNetCore.Mvc.Testing` (for `WebApplicationFactory<Program>`) and the main API project.

**Contract**: `dotnet new xunit` shape targeting `net10.0`, plus `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" ... />` and a `<ProjectReference Include="../MyFinances.Api.csproj" />`. Requires `Program` to be accessible to the test assembly — add `public partial class Program { }` at the end of `Program.cs` if top-level statements don't already expose it (net10.0 minimal APIs need this for `WebApplicationFactory<Program>` to resolve the type).

#### 2. Auth endpoint tests

**File**: `MyFinances/backend/MyFinances.Api.Tests/AuthEndpointsTests.cs` (new)

**Intent**: Cover, using `WebApplicationFactory` against a test configuration (in-memory or a disposable test Postgres/SQLite — implementer's call, documented in the test class):
- Register with the allow-listed email succeeds and sets a cookie.
- Register with a non-allow-listed email returns 400.
- Register with a password under 12 characters returns 400.
- Register twice with the same email returns 409/400 (duplicate).
- Login with correct credentials succeeds; with wrong password returns 401.
- `GET /api/weatherforecast` without a cookie returns 401; with a valid login cookie returns 200.
- Logout invalidates the session (subsequent authenticated request fails).

**Contract**: Standard `IClassFixture<WebApplicationFactory<Program>>` xUnit test class; no snippet needed beyond the enumerated cases above.

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes from `MyFinances/backend` (or repo root if a solution file covers both projects)
- All seven scenarios above pass

#### Manual Verification:

- None — this phase is fully automated by design

---

## Testing Strategy

### Unit Tests:

- Password length validation, allow-list email comparison (case-insensitivity) — covered implicitly via the endpoint integration tests in Phase 4 rather than isolated unit tests, since the logic is thin and lives inline in the endpoint handlers.

### Integration Tests:

- Phase 4's `WebApplicationFactory`-based suite is the integration layer: full HTTP round-trips through register/login/logout/me and the protected weatherforecast endpoint.

### Manual Testing Steps:

1. Start the backend (`dotnet run` from `MyFinances/backend`) and frontend (`npm run dev` from `MyFinances/frontend`).
2. Visit `http://localhost:5173/` — confirm redirect to `/login`.
3. Register with the allow-listed email — confirm success and redirect to `/`.
4. Register again with the same email — confirm duplicate-email error.
5. Register with a different email — confirm rejection.
6. Log out, log back in — confirm the weatherforecast table reappears.
7. Restart the browser (not just refresh) — confirm the session is still valid.

## Performance Considerations

None beyond default ASP.NET Core Identity/EF Core behavior — this app is single-user, low-QPS (per `tech-stack.md`'s `target_scale`).

## Migration Notes

This is a net-new schema addition (Identity's user tables); there's no existing data to migrate. The migration must be applied to the Neon Postgres instance backing both local dev and the Render deployment before auth-dependent code can run there.

## References

- Roadmap item: `context/foundation/roadmap.md` (F-01: Minimal auth scaffold)
- PRD: `context/foundation/prd.md` (FR-001, Access Control, NFR on data isolation)
- Change identity: `context/changes/minimal-auth-scaffold/change.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Identity data model & migration

#### Automated

- [x] 1.1 `dotnet build` succeeds from `MyFinances/backend` — 2ddda5c
- [x] 1.2 `dotnet ef migrations add InitialIdentitySchema` generates a migration touching only Identity user tables — 2ddda5c
- [x] 1.3 `dotnet ef database update` applies cleanly against the configured Postgres connection — 2ddda5c

#### Manual

- [x] 1.4 Inspect the generated migration file to confirm no unexpected tables/columns — 2ddda5c

### Phase 2: Auth endpoints & global enforcement

#### Automated

- [x] 2.1 `dotnet build` succeeds — 1da5bb4
- [x] 2.2 `/api/weatherforecast` with no cookie returns 401 — 1da5bb4
- [x] 2.3 `/api/auth/register` with a non-allow-listed email returns 400 — 1da5bb4
- [x] 2.4 `/api/auth/register` with the allow-listed email and valid password returns 200 and sets a cookie — 1da5bb4
- [x] 2.5 `/api/weatherforecast` with the resulting cookie returns 200 — 1da5bb4
- [x] 2.6 `/api/auth/login` returns 401 on wrong credentials, 200 + cookie on correct ones — 1da5bb4
- [x] 2.7 `/api/auth/logout` requires the antiforgery header and invalidates the session — 1da5bb4

#### Manual

- [x] 2.8 Exercise the four endpoints via Swagger/curl against the real dev database — 1da5bb4

### Phase 3: Frontend auth UI & route guarding

#### Automated

- [x] 3.1 `npm run typecheck` passes
- [x] 3.2 `npm run build` succeeds

#### Manual

- [x] 3.3 Visiting `/` while logged out redirects to `/login`
- [x] 3.4 Register with the allow-listed email succeeds; other emails are rejected
- [x] 3.5 Duplicate registration shows an error
- [x] 3.6 Login success/failure behaves correctly
- [x] 3.7 Weatherforecast table renders once logged in
- [x] 3.8 Logout returns to `/login` and invalidates the session
- [ ] 3.9 Session survives a full browser restart within 30 days

### Phase 4: Backend auth tests

#### Automated

- [ ] 4.1 `dotnet test` passes with all seven auth scenarios covered
