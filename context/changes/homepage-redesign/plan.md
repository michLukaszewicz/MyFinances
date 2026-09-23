# Homepage Redesign Implementation Plan

## Overview

Replace the scaffold-era home route (WeatherForecast demo table + React Router welcome boilerplate, unconditionally auth-gated) with a real MyFinances home route that serves two variants from the same URL: a public landing page for logged-out visitors, and a minimal authenticated dashboard placeholder for logged-in users. Also remove the now-dead WeatherForecast demo endpoint from the backend.

## Current State Analysis

- [home.tsx](MyFinances/frontend/app/routes/home.tsx) is the index route (`routes.ts:4`). Its `clientLoader` calls `/api/auth/me` and `throw redirect("/login")` on failure — the route is unconditionally auth-gated today.
- The route renders a `WeatherForecastTable` (fetches `/api/weatherforecast` via `apiFetch`) followed by `<Welcome />` — both are leftover scaffold demo content, plus a working `Log out` button (`apiFetch("/auth/logout", { method: "POST" })` then `navigate("/login")`).
- [welcome.tsx](MyFinances/frontend/app/welcome/welcome.tsx) renders the React Router logo + "What's next?" links to React Router docs/Discord — pure scaffold boilerplate, no project content. Its only consumer is `home.tsx`.
- [login.tsx](MyFinances/frontend/app/routes/login.tsx) and [register.tsx](MyFinances/frontend/app/routes/register.tsx) already redirect to `/` when `/api/auth/me` succeeds, and link to each other (`Register` / `Log in` links) — this plan does not need to touch either file.
- Backend: `GET /api/weatherforecast` ([Program.cs:90-102](MyFinances/backend/Program.cs:90)) sits inside the `RequireAuthorization()` group (`Program.cs:88`) purely as a scaffold demo with no product purpose. `GET /api/auth/me` ([AuthEndpoints.cs:74-78](MyFinances/backend/Auth/AuthEndpoints.cs:74)) is already a real, protected (no `AllowAnonymous()`) endpoint that returns `{ email }` for the current user and 401 otherwise.
- `AuthEndpointsTests.cs` uses `/api/weatherforecast` three times purely as a generic "some protected endpoint" probe to verify auth-cookie enforcement ([WeatherForecast_RequiresAuthCookie](MyFinances/backend/Tests/AuthEndpointsTests.cs:156), and twice inside [Logout_RequiresAntiforgeryHeader_ThenInvalidatesSession](MyFinances/backend/Tests/AuthEndpointsTests.cs:174)) — none of these tests assert anything about weather data itself.
- Styling convention across the auth pages: Tailwind v4 utility classes, `dark:` variants throughout, `max-w-[300px]` centered card layout, blue-700/blue-500 link color, gray-700/gray-200 text — see [login.tsx:50-99](MyFinances/frontend/app/routes/login.tsx:50).

### Key Discoveries:

- No PRD or roadmap content specifies homepage copy or layout — this is a net-new design decision, confirmed via user interview (see Key Decisions in the brief).
- `/api/auth/me` already returns exactly the shape (`{ email }`) needed to drive both the "who's greeting the user" dashboard text and the auth-probe tests — no new backend endpoint is needed anywhere in this plan.
- `app/welcome/` has exactly one consumer (`home.tsx`); once removed there, the whole directory (including the two SVG logo assets) is dead.

## Desired End State

Visiting `/` while logged out shows a public landing page: "MyFinances" branding, a short pitch, and Login/Register links — no auth check failure, no redirect. Visiting `/` while logged in shows a minimal authenticated view: a shared header (app name + working Log out button) and a "Welcome back, {email}" message with an empty-state note that import is coming soon. Both variants are responsive and support dark mode consistent with the rest of the app. The backend no longer exposes the WeatherForecast demo endpoint, and the auth-enforcement tests probe `/api/auth/me` instead.

### Key Discoveries:

- [home.tsx:14-18](MyFinances/frontend/app/routes/home.tsx:14) is the only place the redirect-on-unauth behavior lives; changing it to branch instead of throw is a localized, single-file loader change.
- The header/logout chrome (`home.tsx:82-91`) is the part of the current page worth keeping and promoting into a shared component (`AppHeader`) for reuse by future authenticated routes (import, categorize, charts).

## What We're NOT Doing

- Not adding placeholder/disabled nav links for not-yet-built features (Import, Categorize, Charts) — the header shows only app name + Log out (when authenticated) per the confirmed decision.
- Not building placeholder dashboard widgets, charts, or sample data — the authenticated view is a welcome message + empty-state note only.
- Not changing `login.tsx` or `register.tsx` — their existing redirect-if-authenticated and cross-links already fit the new home behavior unmodified.
- Not changing the `/api/auth/me` response shape, the antiforgery flow, or any other auth endpoint.
- Not writing new automated frontend tests — no frontend test runner exists in this repo yet (out of scope to introduce one here).

## Implementation Approach

Backend first (remove the dead endpoint and repoint the tests that incidentally depended on it), then frontend: extract the shared header, then rewrite the home route to branch on auth state instead of redirecting, then delete the now-orphaned welcome scaffold. This order means the frontend phase is written against a backend that already reflects its final state, and each phase leaves the tree in a working, verifiable state.

## Phase 1: Backend — remove WeatherForecast demo endpoint

### Overview

Delete the scaffold `GET /api/weatherforecast` endpoint and its `WeatherForecast` record, and repoint the three tests that used it purely as a generic protected-endpoint probe to `GET /api/auth/me`.

### Changes Required:

#### 1. Remove the demo endpoint

**File**: `MyFinances/backend/Program.cs`

**Intent**: Delete the `/weatherforecast` route registration, the `summaries` array it used, and the `WeatherForecast` record — none of it is reachable from the product once the frontend no longer calls it.

**Contract**: Remove `Program.cs:83-102` (the `summaries` array through `.WithName("GetWeatherForecast")`) and the `WeatherForecast` record definition (`Program.cs:117-120`). The `api` group (`Program.cs:88`) and `api.MapAuthEndpoints()` (`Program.cs:104`) remain unchanged.

#### 2. Repoint auth-enforcement tests

**File**: `MyFinances/backend/Tests/AuthEndpointsTests.cs`

**Intent**: These tests exist to verify the auth-cookie enforcement policy on the `/api` group in general — swap the probed path from the now-deleted `/api/weatherforecast` to `/api/auth/me`, which is already a protected, `AllowAnonymous()`-free endpoint.

**Contract**: Replace every `"/api/weatherforecast"` string literal (3 occurrences: `AuthEndpointsTests.cs:161`, `:169`, `:188`, `:198`) with `"/api/auth/me"`. Rename the test method `WeatherForecast_RequiresAuthCookie` (`:156`) to `AuthMe_RequiresAuthCookie` to match. Assertions (status codes) stay identical since `/api/auth/me` returns 200/401 the same way the old endpoint did.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- No remaining references to `weatherforecast`/`WeatherForecast` in the backend: `grep -ri weatherforecast MyFinances/backend -r` returns no matches

#### Manual Verification:

- None required for this phase — fully covered by automated tests.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Frontend — extract shared AppHeader

### Overview

Introduce a small reusable header component carrying the app name and (when authenticated) a working Log out button, so the home route's dashboard view and future authenticated routes share one piece of chrome instead of duplicating it.

### Changes Required:

#### 1. New shared header component

**File**: `MyFinances/frontend/app/components/AppHeader.tsx` (new file/directory)

**Intent**: Render "MyFinances" branding plus a conditional Log out button, reusable by any authenticated view. Not auth-aware itself — the caller passes whether to show the logout action.

**Contract**: Exports a component accepting `{ showLogout: boolean }` (or equivalent). Logout behavior (call `apiFetch("/auth/logout", { method: "POST" })` then `navigate("/login")`) moves here verbatim from `home.tsx:76-79`, reusing the existing `useNavigate` + `apiFetch` pattern. Styling follows the existing dark-mode/blue-link conventions used in `login.tsx`/`register.tsx`.

### Success Criteria:

#### Automated Verification:

- Frontend typecheck passes: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend build succeeds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- None yet — `AppHeader` isn't wired into a route until Phase 3; deferred to that phase's manual check.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Frontend — dual public/authenticated home route

### Overview

Rewrite `home.tsx` so `clientLoader` branches on auth state instead of redirecting, and the component renders either a public landing view or an authenticated dashboard view using `AppHeader`. Update `meta()` to real MyFinances branding.

### Changes Required:

#### 1. Home route loader and rendering

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Replace the unconditional auth-redirect with a branch: fetch `/api/auth/me`, and on success return the user info for the dashboard view, on failure return `null` for the public view — never throwing a redirect from this route. Remove the `WeatherForecastTable` component and the `<Welcome />` render entirely.

**Contract**: `clientLoader` returns `{ email: string } | null` (no `throw redirect(...)`). `meta()` returns `{ title: "MyFinances" }` and a description tag with the pitch tagline instead of the React Router boilerplate strings. Default export reads the loader data via `useLoaderData` and renders:
  - Logged out (`null`): a public landing view with "MyFinances" heading, a 1-2 sentence pitch, and links/buttons to `/login` and `/register`.
  - Logged in (user present): `<AppHeader showLogout />` plus a "Welcome back, {email}" message and a short empty-state note that import is coming soon.

Both views use responsive, dark-mode-consistent Tailwind classes matching `login.tsx`'s conventions (max-width centered card, `dark:` variants throughout).

### Success Criteria:

#### Automated Verification:

- Frontend typecheck passes: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend build succeeds: `npm run build` (from `MyFinances/frontend`)
- No remaining reference to `WeatherForecastTable`, `apiFetch<WeatherForecast[]>`, or `/weatherforecast` in the frontend: `grep -ri weatherforecast MyFinances/frontend -r` returns no matches

#### Manual Verification:

- Logged out, visiting `/` shows the public landing page (no redirect to `/login`), with working links to `/login` and `/register`.
- Logged in (via the existing register/login flow), visiting `/` shows the authenticated dashboard view with the correct email and a working Log out button that returns to the public landing view at `/`.
- Both views render correctly at a mobile viewport width and in dark mode (OS/browser dark-mode preference).
- Refreshing the page in each auth state preserves the correct view (no flash of the wrong variant).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Cleanup — remove dead welcome scaffold

### Overview

Delete the now-unreferenced React Router welcome boilerplate directory.

### Changes Required:

#### 1. Delete unused scaffold assets

**File**: `MyFinances/frontend/app/welcome/` (directory: `welcome.tsx`, `logo-dark.svg`, `logo-light.svg`)

**Intent**: Remove dead code — nothing imports from this directory after Phase 3 removes `home.tsx`'s `<Welcome />` usage.

**Contract**: Directory deleted entirely. Verify no remaining imports first.

### Success Criteria:

#### Automated Verification:

- No remaining references to the welcome directory: `grep -r "welcome/welcome\|welcome/logo" MyFinances/frontend/app -r` returns no matches
- Frontend typecheck passes: `npm run typecheck` (from `MyFinances/frontend`)
- Frontend build succeeds: `npm run build` (from `MyFinances/frontend`)

#### Manual Verification:

- App still loads correctly in both auth states after the deletion (quick re-check of Phase 3's manual steps).

**Implementation Note**: After completing this phase and all automated verification passes, this plan is complete.

---

## Testing Strategy

### Unit Tests:

- Backend: the three repointed `AuthEndpointsTests.cs` tests continue to cover auth-cookie enforcement and antiforgery-gated logout, now via `/api/auth/me` instead of the deleted endpoint.

### Integration Tests:

- None new — no frontend test runner exists in this repo (per `CLAUDE.md`); frontend verification is manual (Phase 3/4 Manual Verification) plus `npm run typecheck` / `npm run build`.

### Manual Testing Steps:

1. Start backend (`dotnet run` in `MyFinances/backend`) and frontend (`npm run dev` in `MyFinances/frontend`).
2. Visit `http://localhost:5173/` logged out — confirm the public landing page renders with working Login/Register links.
3. Register a new account (or log in with an existing one) — confirm redirect to `/` shows the authenticated dashboard view with the correct email.
4. Click Log out — confirm return to the public landing view at `/`.
5. Toggle OS/browser dark mode and a mobile viewport width — re-check both views render correctly.

## Performance Considerations

None — this is a static content swap with no new data fetching beyond the existing `/api/auth/me` call already made by the loader today.

## Migration Notes

Not applicable — no persisted data or schema changes.

## References

- [home.tsx](MyFinances/frontend/app/routes/home.tsx) — route being rewritten
- [welcome.tsx](MyFinances/frontend/app/welcome/welcome.tsx) — scaffold being deleted
- [login.tsx](MyFinances/frontend/app/routes/login.tsx) — styling/redirect convention to match
- [Program.cs](MyFinances/backend/Program.cs) — backend endpoint being removed
- [AuthEndpoints.cs](MyFinances/backend/Auth/AuthEndpoints.cs) — `/api/auth/me` contract reused as the test probe
- [AuthEndpointsTests.cs](MyFinances/backend/Tests/AuthEndpointsTests.cs) — tests being repointed

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — remove WeatherForecast demo endpoint

#### Automated

- [x] 1.1 Backend builds: `dotnet build` — 1cddd31
- [x] 1.2 Backend tests pass: `dotnet test` — 1cddd31
- [x] 1.3 No remaining references to weatherforecast in the backend — 1cddd31

### Phase 2: Frontend — extract shared AppHeader

#### Automated

- [x] 2.1 Frontend typecheck passes — 7711baf
- [x] 2.2 Frontend build succeeds — 7711baf

### Phase 3: Frontend — dual public/authenticated home route

#### Automated

- [x] 3.1 Frontend typecheck passes — 90e3622
- [x] 3.2 Frontend build succeeds — 90e3622
- [x] 3.3 No remaining reference to WeatherForecastTable/weatherforecast in the frontend — 90e3622

#### Manual

- [x] 3.4 Logged out `/` shows public landing page with working Login/Register links — 90e3622
- [x] 3.5 Logged in `/` shows authenticated dashboard view with correct email and working Log out — 90e3622
- [x] 3.6 Both views render correctly at mobile viewport width and in dark mode — 90e3622
- [x] 3.7 Refreshing the page preserves the correct view per auth state — 90e3622

### Phase 4: Cleanup — remove dead welcome scaffold

#### Automated

- [x] 4.1 No remaining references to the welcome directory — 11b5cef
- [x] 4.2 Frontend typecheck passes — 11b5cef
- [x] 4.3 Frontend build succeeds — 11b5cef

#### Manual

- [x] 4.4 App still loads correctly in both auth states after deletion — 11b5cef
