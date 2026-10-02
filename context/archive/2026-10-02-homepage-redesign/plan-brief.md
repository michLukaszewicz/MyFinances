# Homepage Redesign — Plan Brief

> Full plan: `context/changes/homepage-redesign/plan.md`

## What & Why

Replace the scaffold-era home route — a WeatherForecast demo table plus React Router welcome boilerplate, unconditionally auth-gated — with a real MyFinances home route. The route now serves two variants: a public landing page for logged-out visitors (pitch + login/register links) and a minimal authenticated dashboard placeholder for logged-in users (welcome message + empty-state note), since no downstream feature (import/categorize/charts) exists yet to put on it.

## Starting Point

`home.tsx`'s `clientLoader` currently redirects to `/login` whenever `/api/auth/me` fails — the index route is fully auth-gated today, and its content is 100% scaffold demo (weather table + React Router logo/links). `login.tsx`/`register.tsx` already redirect to `/` when already authenticated and link to each other — neither needs to change.

## Desired End State

Visiting `/` while logged out shows a public landing page with MyFinances branding, a short pitch, and working Login/Register links. Visiting `/` while logged in shows a shared header (app name + Log out) and a "Welcome back, {email}" message with an empty-state note. Both variants are responsive and support dark mode. The backend no longer exposes the dead WeatherForecast demo endpoint.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Home page content | Minimal welcome + shared app shell, no fake dashboard widgets | Nothing downstream is built yet — showing sample data would be throwaway work rebuilt at S-04. |
| Route structure | Dual public/authenticated variant on the same `/` route (not two separate routes) | User wants one homepage that adapts, with a real explanation + login/register links for logged-out visitors instead of a hard redirect. |
| Auth check mechanism | `clientLoader` calls `/api/auth/me` and branches on the result instead of throwing a redirect | Reuses the existing auth-check call with a one-line behavior change; no new endpoint or auth pattern needed. |
| Header/nav scope | Shared `AppHeader` component (app name + logout), reused by future routes | Future slices (import, categorize, charts) can plug into the same header instead of re-building chrome each time. |
| Nav links for unbuilt features | None — no disabled "coming soon" stubs | Nothing to click that goes nowhere; each future slice adds its own nav entry when it actually lands. |
| WeatherForecast demo cleanup | Remove both the frontend usage and the backend `/api/weatherforecast` endpoint | Fully removes the scaffold demo; its only real value (a protected-endpoint probe in 3 auth tests) is served just as well by the already-protected `/api/auth/me`. |
| Branding copy | "MyFinances" + a short tagline | Matches the existing project name from `CLAUDE.md`/PRD — no new naming decision needed. |
| Responsive/dark mode | Required for both variants | Matches the existing convention (`dark:` classes throughout `login.tsx`/`register.tsx`) — avoids a visual regression for existing dark-mode users. |

## Scope

**In scope:**
- Backend: delete the `/api/weatherforecast` endpoint; repoint the 3 auth-enforcement tests that used it as a generic protected-endpoint probe to `/api/auth/me`.
- Frontend: new shared `AppHeader` component; `home.tsx` rewritten to branch on auth state (public landing vs. authenticated dashboard); delete the now-dead `app/welcome/` scaffold directory.

**Out of scope:**
- Placeholder/disabled nav links for Import, Categorize, or Charts.
- Placeholder dashboard widgets or sample chart data.
- Any change to `login.tsx`, `register.tsx`, or the `/api/auth/me` response shape.
- Introducing a frontend test runner (none exists in this repo yet).

## Architecture / Approach

Backend cleanup lands first (Phase 1) so the frontend work targets a backend already in its final state. Frontend work proceeds bottom-up: extract the reusable header chrome (Phase 2), then rewrite the home route around it with the new branch-not-redirect loader (Phase 3), then delete the orphaned scaffold once nothing references it (Phase 4).

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend cleanup | WeatherForecast endpoint removed; auth tests repointed to `/api/auth/me` | Low — mechanical rename, same assertions |
| 2. Shared AppHeader | Reusable header component (app name + conditional logout) | Low — new file, no route wiring yet |
| 3. Dual home route | `/` renders public landing or authenticated dashboard based on auth state | Medium — loader behavior change (redirect → branch) touches the reviewed auth flow |
| 4. Scaffold cleanup | `app/welcome/` deleted | Low — pure deletion, verified via grep first |

**Prerequisites:** `minimal-auth-scaffold` change is implemented and reviewed (done — see `context/changes/minimal-auth-scaffold/`).
**Estimated effort:** ~1 session across 4 phases — small, mostly single-file changes.

## Open Risks & Assumptions

- Assumes `/api/auth/me`'s existing 401-on-unauthenticated behavior is stable enough to double as both the dashboard's data source and the test suite's generic auth probe — confirmed by reading `AuthEndpoints.cs`, not just assumed.
- The public landing page's exact pitch copy is left to the implementer to draft (1-2 sentences, "MyFinances" + tagline) rather than dictated word-for-word — low risk given it's easily adjusted post-implementation.

## Success Criteria (Summary)

- A logged-out visitor to `/` sees an explanatory landing page and can reach Login/Register — no forced redirect.
- A logged-in visitor to `/` sees a personalized welcome and can log out from the same shared header future routes will reuse.
- No trace of the WeatherForecast scaffold demo remains in either frontend or backend.
