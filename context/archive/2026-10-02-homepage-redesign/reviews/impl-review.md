<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Homepage Redesign Implementation Plan

- **Plan**: context/changes/homepage-redesign/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-09-24
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — AppHeader logout has no error handling

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/AppHeader.tsx:12
- **Detail**: `handleLogout` awaits `apiFetch("/auth/logout", ...)` with no try/catch. `apiFetch` throws on non-OK responses, so a failed logout (network error, expired antiforgery token, 500) produces an unhandled promise rejection and `navigate("/login")` never runs — the user is stuck with no feedback. `login.tsx`'s `handleSubmit` already has a try/finally pattern for this exact situation.
- **Fix**: Wrap the logout call in try/catch (or try/finally) matching `login.tsx`'s pattern, navigating to `/login` regardless of outcome.
- **Decision**: FIXED — wrapped in try/finally, navigates to /login regardless of outcome.

### F2 — home.tsx loader has no error handling for network failures

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/routes/home.tsx:31
- **Detail**: `clientLoader` calls raw `fetch` with no try/catch. A network error (offline, DNS failure) throws inside the loader and trips React Router's route error boundary instead of falling back to the public landing view, which is the intended behavior for "not authenticated."
- **Fix**: Wrap the fetch in try/catch and treat a thrown network error the same as `!res.ok` (render the public view).
- **Decision**: FIXED — wrapped in try/catch, returns null (public view) on thrown errors.

### F3 — login.tsx was modified despite the plan's explicit boundary

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: MyFinances/frontend/app/routes/login.tsx
- **Detail**: The plan's "What We're NOT Doing" list states "Not changing `login.tsx` or `register.tsx` — their existing redirect-if-authenticated and cross-links already fit the new home behavior unmodified." In practice, `login.tsx` (and presumably `register.tsx`) now imports and renders `<AppHeader authenticated={false} />` at the top — a change to a file the plan explicitly said would stay untouched.

  Fix A ⭐ Recommended: Accept as a reasonable, benign extension — `AppHeader` unifying branding across auth pages is a natural consequence of extracting the component, and it's cosmetic (adds the same header, no behavior change to redirect/cross-link logic).
  - Strength: Improves visual consistency across auth + home pages at near-zero risk; the "unmodified" guarantee was about *behavior* (redirect/cross-links), which is intact.
  - Tradeoff: The plan's stated boundary is no longer literally true — future readers trusting the plan as ground truth would be misled.
  - Confidence: MED — reasonable engineering call, but not what was scoped/approved.
  - Blind spot: Haven't visually verified register.tsx or confirmed no behavior changes beyond the header addition.

  Fix B: Revert `login.tsx`/`register.tsx` to their pre-change state, keeping `AppHeader` scoped to `home.tsx` only as originally planned.
  - Strength: Restores strict scope discipline; matches what was actually reviewed/approved.
  - Tradeoff: Loses the visual consistency; auth pages and home page will look mismatched.
  - Confidence: MED — depends on whether the user actually wants that consistency.
- **Decision**: ACCEPTED (Fix A) — AppHeader on login/register kept; cosmetic-only, redirect/cross-link behavior unchanged.

### F4 — home.tsx adds an unplanned 3-item marketing grid

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/frontend/app/routes/home.tsx
- **Detail**: The plan specified "a short pitch" (1-2 sentences) for the public landing view. The implementation adds a 3-card value-prop grid ("Import your statements / Categorize in minutes / See spend vs. history") — more marketing surface than scoped, and it names features (Import, Categorize) the plan explicitly said not to reference via nav links. It's not a nav link or a disabled control, so it doesn't violate that specific guardrail, but it is scope beyond "1-2 sentence pitch."
- **Fix**: Accept as-is (reasonable landing-page copy, low risk) — no code change needed unless you want to trim it back to a single pitch line.
- **Decision**: ACCEPTED — value-prop grid kept as-is.

### F5 — AppHeader prop renamed from plan's `showLogout` to `authenticated`

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/components/AppHeader.tsx
- **Detail**: Plan's contract named the prop `showLogout: boolean`; implementation uses `authenticated: boolean` and additionally renders Log in/Register links when `false` (not specified in the plan, but consistent with reusing it on login/register pages per F3).
- **Fix**: No action needed — functionally equivalent, arguably better name given the expanded reuse.
- **Decision**: SKIPPED

### F6 — Unguarded `user.email` on the authenticated view

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/routes/home.tsx:122
- **Detail**: `useLoaderData` return isn't strongly typed against a guaranteed non-null `email`; if `/api/auth/me` ever omitted the claim, this would render "Welcome back, null" with no fallback. Low likelihood given the current `AuthEndpoints` implementation always sets the claim.
- **Fix**: No action needed now — flag if `/api/auth/me`'s contract ever changes.
- **Decision**: SKIPPED

### F7 — MyFinances.Api.http changed outside the plan's listed files

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/backend/MyFinances.Api.http
- **Detail**: Commit 1cddd31 updated a stale `/weatherforecast` REST-client scratch request to `/api/auth/me`, mirroring the endpoint removal. Not listed in the plan, but tightly scoped and directly tied to Phase 1's change.
- **Fix**: No action needed — sensible companion fix.
- **Decision**: SKIPPED

### F8 — Public view heading is visually hidden (`sr-only`), not a visible "MyFinances" heading

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/routes/home.tsx
- **Detail**: Plan said the public view should have a "MyFinances" heading; implementation makes it screen-reader-only, likely because the logo/branding already appears in `AppHeader`. Reasonable given the header now carries the branding visually, but technically deviates from the plan's literal wording.
- **Fix**: No action needed — branding is present via AppHeader, this is a minor wording deviation not a functional gap.
- **Decision**: SKIPPED

### F9 — Work landed via direct commits to `main`, not a feature branch + PR

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: N/A (process, not code)
- **Detail**: `context/foundation/lessons.md` records "Use feature branches, merge via PR — never commit/push directly to `main`" (applies to: implement). All phases of this change (and the prior `minimal-auth-scaffold` and `homepage-visual-refresh` changes) were committed directly to `main` per `git log`. This is a recurring pattern across changes, not unique to this one.
- **Fix**: No code fix — this is a workflow decision. Consider whether future `/10x-implement` runs should actually branch, or whether the lesson should be revised/retired if solo direct-to-main is the intended workflow for this project.
- **Decision**: SKIPPED
