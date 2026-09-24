<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Minimal Auth Scaffold Implementation Plan

- **Plan**: context/changes/minimal-auth-scaffold/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-09-23
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Adherence notes (not findings — already justified during implementation)

- `MyFinances/frontend/app/lib/api.ts` deviates from the plan's literal "read the XSRF-TOKEN cookie" contract: ASP.NET Core's antiforgery double-submit pattern returns a `RequestToken` in the response body that is cryptographically distinct from the cookie value, so the plan's literal approach would not have worked. The implementation instead fetches a fresh token from the response body immediately before every mutating request. Verified as a necessary correction, not sloppy drift.
- The test project moved from the plan's specified `MyFinances/backend/MyFinances.Api.Tests/` to `MyFinances/backend/Tests/`, wired into the pre-existing `MyFinances.Api.slnx` rather than a new solution file. This was an explicit user redirect during Phase 4, not an unreviewed deviation.
- `.claude/launch.json`, the `MyFinances.Api.slnx` update, and `context/foundation/roadmap.md`'s status flip are expected supporting infrastructure / lifecycle bookkeeping, not scope creep.

## Findings

### F1 — CORS policy for the direct-backend-hit dev path doesn't enable credentials

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Program.cs:28-34
- **Detail**: The `FrontendDev` CORS policy exists specifically so the frontend can hit the backend directly (bypassing the Vite proxy) per CLAUDE.md's documented dev wiring, but it never calls `.AllowCredentials()`, and no frontend `fetch` call sets `credentials: "include"`. In that direct-hit scenario, the auth cookie would silently never be sent, so login would appear to "work" (200 + Set-Cookie) but every subsequent request would come back 401 — contradicting the CORS policy's stated purpose. The primary dev flow (Vite proxy, same apparent origin) is unaffected.
- **Fix**: Add `.AllowCredentials()` to the `FrontendDev` CORS policy and `credentials: "include"` to the frontend's fetch calls, or add a code comment noting the direct-hit path is aspirational/untested if it's not meant to fully work yet.
- **Decision**: FIXED — added `.AllowCredentials()` to the FrontendDev CORS policy (Program.cs); added `credentials: "include"` to all 7 frontend fetch call sites (api.ts x2, home.tsx, login.tsx x2, register.tsx x2).

### F2 — login.tsx / register.tsx bypass the apiFetch contract the plan specified

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/routes/login.tsx:35, MyFinances/frontend/app/routes/register.tsx:34
- **Detail**: Phase 3's plan text says the login/register forms "`POST`s to `/api/auth/login` or `/api/auth/register` via `apiFetch`" — the implementation uses raw `fetch` instead. This hardcodes the `/api` prefix (bypassing `apiFetch`'s `VITE_API_BASE_URL` resolution, so a deployment that sets that env var to something other than `/api` would silently break these two pages while the rest of the app keeps working), and neither `handleSubmit` wraps the `fetch` call itself in a try/catch, so a network failure (not just a non-OK response) throws an unhandled rejection instead of showing the user a friendly error. Currently harmless in practice only because register/login need no antiforgery header and the default `/api` base is what's configured everywhere.
- **Fix**: Switch both to `apiFetch`, and wrap the network call in a try/catch so a connection failure surfaces the same friendly error path as a non-OK response.
- **Decision**: FIXED — login.tsx/register.tsx now call `apiFetch` (fixes hardcoded `/api` prefix + adds try/catch for network failures). Introduced `ApiError` in api.ts (carries the raw `Response`) so `extractErrorMessage` can still read the server's `title` field without regressing the existing friendly-error UX.

### F3 — fetchXsrfToken doesn't check response.ok before parsing JSON

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/lib/api.ts:13-16
- **Detail**: If `GET /api/auth/antiforgery-token` fails (network blip, backend 5xx), `fetchXsrfToken` calls `res.json()` unconditionally, throwing a raw `SyntaxError: Unexpected end of JSON input` instead of a clear error — the same class of bug found and fixed elsewhere in this change (the logout empty-body parsing issue). The caller's mutating request never gets a chance to run.
- **Fix**: Check `res.ok` before parsing and throw/surface a clear error message on failure, matching the pattern already used in `apiFetch` itself.
- **Decision**: FIXED — `fetchXsrfToken` now checks `res.ok` and throws `ApiError` with the status before attempting `res.json()`.
