# Minimal Auth Scaffold — Plan Brief

> Full plan: `context/changes/minimal-auth-scaffold/plan.md`

## What & Why

Add email/password authentication so every MyFinances API endpoint is scoped to a logged-in user and the session persists across visits (roadmap F-01, PRD FR-001 / Access Control). This is the foundation slice — every later roadmap item (S-01 through S-08) persists or reads user-scoped financial data, so none can be built safely before this lands.

## Starting Point

The repo has zero auth code today: `Program.cs` registers no authentication, `AppDbContext` is an explicit empty placeholder, the frontend has one unguarded route, and no test project exists in either stack. The SPA and API already share a single origin in both dev (Vite proxy) and production (static files served from the API), which is what makes a cookie-based session viable without CORS complexity.

## Desired End State

A visitor hitting the app while logged out is redirected to `/login`. They can register only with one pre-configured (allow-listed) email address, log in, and stay logged in via an HttpOnly cookie that survives a browser restart for up to 30 days of inactivity. Every `/api/*` endpoint — including the existing weatherforecast one — returns 401 without that cookie.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Session mechanism | HttpOnly cookie (ASP.NET Core cookie auth) | Same-origin SPA+API in prod makes cookies simplest and avoids XSS token-theft risk. |
| User store | Full ASP.NET Core Identity (roles-less `IdentityUserContext`) | Batteries-included password hashing/validation; roles table skipped since the model is flat. |
| Registration exposure | Allow-listed single email via config | App is on a public Render URL but meant for exactly one person. |
| Password rules | Minimum length only (12 chars) | Matches current NIST/OWASP guidance; single-account app, not a target-rich environment. |
| CSRF defense | Anti-forgery token (double-submit cookie + header) | Defense-in-depth beyond `SameSite=Strict` alone, per explicit user choice. |
| Session lifetime | 30-day sliding expiration | Matches "persists across visits" and the persona's weekly/monthly usage pattern. |
| Frontend route guarding | Server-checked loader (`GET /api/auth/me`) | Cookie is HttpOnly so the SPA can't inspect it client-side; the loader asks the API directly. |
| Login lockout | None for MVP | Single-account app, low brute-force risk at this scale, matches the 3-week timeline. |
| Logout | Built now | Trivial once cookie auth exists; every later slice needs a working logout for manual testing. |
| Test seeding | Backend xUnit tests only, this change | Auth is the highest-risk code so far; frontend test tooling deferred to keep scope bounded. |
| User entity fields | Minimal (`Id`, `Email`, `PasswordHash`, `CreatedAtUtc`) | Nothing in the app currently needs a display name or profile data. |
| Error response shape | RFC 9457 `ProblemDetails` | ASP.NET Core's built-in default — zero extra code, consistent with future endpoints. |

## Scope

**In scope:**
- Backend: Identity data model + migration, register/login/logout/me endpoints, global `/api` auth enforcement, antiforgery.
- Frontend: `/login` + `/register` pages, protected-route guarding, logout control, antiforgery header wiring.
- Backend xUnit test project covering all auth scenarios.

**Out of scope:**
- Password reset, email verification, account lockout/rate-limiting.
- Roles, multi-user admin surface, OAuth/Google login.
- Frontend test tooling.
- Deployment config changes beyond documenting one new Render env var.

## Architecture / Approach

ASP.NET Core Identity (roles-less, `IdentityUserContext<AppUser, Guid>`) backs the user store; cookie authentication (`IdentityConstants.ApplicationScheme`, 30-day sliding) is the sign-in mechanism instead of Identity's default bearer-token minimal-API endpoints. The `/api` route group defaults to `RequireAuthorization()`, with `register`/`login` explicitly `AllowAnonymous()` — this makes the existing `/api/weatherforecast` endpoint protected with zero code changes to its own handler, and it doubles as the test target proving global enforcement works. Antiforgery uses ASP.NET Core's built-in double-submit-cookie service. On the frontend, a `clientLoader` on the home route calls `/api/auth/me` and redirects to `/login` on 401 — the cookie is HttpOnly so the SPA can't check auth state itself.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Identity data model & migration | `AppUser`, `IdentityUserContext`-based `AppDbContext`, EF migration | Migration must land cleanly against the shared Neon Postgres instance |
| 2. Auth endpoints & global enforcement | register/login/logout/me endpoints, `/api` group requires auth, antiforgery | Cookie auth's default redirect-to-login-page behavior must be overridden to return clean 401s for a JSON API |
| 3. Frontend auth UI & guarding | `/login`, `/register`, protected home route, logout, antiforgery header | Getting the antiforgery cookie bootstrapped before the first mutating request |
| 4. Backend auth tests | `MyFinances.Api.Tests` (xUnit), 7 scenarios via `WebApplicationFactory` | `Program` needs to be exposed as `partial class` for `WebApplicationFactory<Program>` to resolve it |

**Prerequisites:** None beyond the current scaffold (backend/frontend split, Neon Postgres already wired).
**Estimated effort:** ~2-3 after-hours sessions across 4 phases.

## Open Risks & Assumptions

- Assumes the allow-listed email is provided once via config/env at deploy time — not yet wired into `render.yaml` (documented as a manual deploy-time step, not code).
- The antiforgery double-submit pattern is a slightly unusual combination with pure JSON minimal APIs (more commonly paired with Razor/Blazor); Phase 2/3 need to verify the cookie-to-header round-trip works cleanly with `fetch`-based calls.
- Assumes `net10.0` minimal APIs support the `public partial class Program` pattern for `WebApplicationFactory<Program>` the same way earlier .NET versions did — verify during Phase 4.

## Success Criteria (Summary)

- A visitor can register only with the allow-listed email, log in, and stay logged in across a browser restart.
- Every `/api/*` endpoint (including the pre-existing weatherforecast one) rejects unauthenticated requests with 401.
- `dotnet test` passes covering all register/login/logout/protected-endpoint scenarios.
