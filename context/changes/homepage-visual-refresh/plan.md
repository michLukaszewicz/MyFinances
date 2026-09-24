# Homepage Visual Refresh Implementation Plan

## Overview

Give MyFinances a brand identity built on the user-supplied logo: extend the Tailwind theme with the logo's blue as an accent color, rebuild the public homepage into an animated hero with a short value-prop section, and extend the branding plus a lighter animation treatment to the shared header and the authenticated "welcome back" homepage state.

## Current State Analysis

The homepage ([home.tsx](../../../MyFinances/frontend/app/routes/home.tsx)) currently renders as plain centered text in both its logged-out and logged-in states — a small `text-lg font-semibold` heading, one paragraph of gray text, and (logged-out) two underlined blue links. [AppHeader.tsx](../../../MyFinances/frontend/app/components/AppHeader.tsx) shows "MyFinances" as unstyled text with a logout link. There is no logo anywhere in the UI, no color beyond default Tailwind `gray-*`/`blue-700`, and no motion. Tailwind v4 is configured via a single `@theme` block in [app.css](../../../MyFinances/frontend/app/app.css) (only `--font-sans` is currently overridden); there is no animation library in `package.json` — any motion must be CSS-only (Tailwind's built-in transition utilities plus custom `@keyframes`).

### Key Discoveries:

- Tailwind v4's `@theme` directive in `app.css` is the single place color tokens are defined ([app.css:3-6](../../../MyFinances/frontend/app/app.css)) — extending it with a `--color-brand-*` scale makes the color available as `bg-brand-600`, `text-brand-600`, etc. everywhere, matching how `--font-sans` is already wired.
- `home.tsx` branches into two fully separate JSX blocks for `!user` vs `user` ([home.tsx:25-53](../../../MyFinances/frontend/app/routes/home.tsx) vs [home.tsx:55-69](../../../MyFinances/frontend/app/routes/home.tsx)) — the plan's Phase 2/Phase 3 split follows this existing branch rather than introducing a new one.
- `AppHeader` is already shared between the authenticated homepage and (per the login/register `clientLoader` redirect-if-authenticated pattern) implicitly excluded from login/register, so header branding changes have exactly one call site today ([home.tsx:57](../../../MyFinances/frontend/app/routes/home.tsx)).
- The supplied logo ([myfinances-logo.png](../../../MyFinances/frontend/app/assets/myfinances-logo.png)) is a solid saturated blue (~`#2E2BEA`-range) mark on a transparent background — visually inspected to have strong contrast against both `bg-white` and `dark:bg-gray-950`, confirming the "same logo in both modes" decision needs no second asset.
- No animation library is installed ([package.json](../../../MyFinances/frontend/package.json)) — entrance/ambient motion must be implemented as Tailwind utility classes plus custom `@keyframes` declared in `app.css`.

## Desired End State

The public (logged-out) homepage shows the logo, a staggered fade/slide-in entrance animation, a subtle ambient glow behind the mark, brand-colored CTA buttons, and a 3-item value-prop section explaining what the app does. The shared header and the authenticated "welcome back" homepage carry the same logo/brand-color treatment and a lighter entrance animation, without the marketing copy. All Tailwind color/link usages that previously hardcoded `blue-700`/`blue-500` on these surfaces now reference the new brand token.

**Verification**: manually load `/` logged out and logged in (via dev server), confirm logo renders, animations play once on load without looping distractingly, and dark mode (OS-level `prefers-color-scheme: dark`) still reads cleanly.

## What We're NOT Doing

- Not adding an animation library (Framer Motion or similar) — CSS-only per the confirmed decision.
- Not recreating the logo as SVG — using the supplied PNG as-is.
- Not producing a second (dark-mode-specific) logo asset.
- Not changing the backend, auth flow, or any non-visual behavior.
- Not building a full design system or additional pages beyond home + header + (as of Phase 4) login/register.

> **Scope revision (after Phase 2 shipped)**: the plan originally excluded `login.tsx`/`register.tsx`. Based on direct user feedback after seeing Phase 2 live, that exclusion is lifted — see Phase 4, added below the original three phases.

## Implementation Approach

Add the brand color and animation keyframes to the shared theme layer first (Phase 1), so both the homepage and header phases consume the same tokens instead of duplicating hex values. Then rebuild the public hero (Phase 2), which is the highest-value surface since it's what unauthenticated visitors see. Finish with the header and authenticated home state (Phase 3), reusing the keyframes/tokens from Phase 1 so the two logged-in surfaces feel consistent with the public one without re-deriving color/animation decisions.

## Phase 1: Brand theme & asset wiring

### Overview

Add the logo's blue as a reusable Tailwind theme color, add the logo image as an importable module asset, and declare the shared CSS `@keyframes` (entrance fade/slide, ambient glow pulse) that later phases will apply via utility classes.

### Changes Required:

#### 1. Brand color token

**File**: `MyFinances/frontend/app/app.css`

**Intent**: Make the logo's blue available as a semantic Tailwind color scale so buttons/links/accents across the homepage and header can reference `brand-*` instead of the generic `blue-*` palette, and so a future page can reuse the same token.

**Contract**: Add `--color-brand-{50,100,...,900}` entries to the existing `@theme` block, anchored on the logo's sampled blue (~`#2E2BEA`) as `brand-600`, with lighter/darker steps generated around it. Also declare two custom `@keyframes` blocks in the stylesheet body (outside `@theme`): `fade-slide-in` (opacity 0→1, translateY 8px→0) and `ambient-glow` (a slow opacity/scale pulse, ~4-6s ease-in-out infinite) for use via arbitrary Tailwind `animate-[...]` utilities or a small set of custom utility classes.

#### 2. Logo asset import

**File**: `MyFinances/frontend/app/assets/myfinances-logo.png` (already saved), consumed via `import` in Phase 2/3 components.

**Intent**: Confirm the asset is importable as a Vite-processed module (`import logo from "../assets/myfinances-logo.png"`) so it gets fingerprinting/caching like other Vite-built assets, rather than being referenced via a raw `/public` path.

**Contract**: No code change here beyond the existing file — this is a checkpoint that Vite's default asset handling picks up `.png` imports under `app/assets/` (react-router's Vite preset supports this out of the box; verified during Phase 2 implementation, not a separate config change).

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck` (run from `MyFinances/frontend`)
- Frontend builds cleanly: `npm run build` (run from `MyFinances/frontend`)

#### Manual Verification:

- `bg-brand-600`, `text-brand-600` etc. resolve to the intended blue when used in a scratch element (spot-checked in browser devtools before moving to Phase 2)

---

## Phase 2: Public homepage hero

### Overview

Rebuild the logged-out branch of `home.tsx` into an animated hero: logo with ambient glow, staggered entrance animation for heading/tagline/CTAs, brand-colored buttons, and a 3-item value-prop section.

### Changes Required:

#### 1. Logged-out hero layout

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Replace the current plain-text logged-out block (`home.tsx:26-51`) with a hero section: the imported logo image (sized responsively, e.g. capped width with `h-auto`), the existing tagline copy, brand-colored `Log in`/`Register` CTAs (buttons or styled links using `brand-*`), and a new value-prop section with 3 short items (e.g. "Import your statements", "Categorize in minutes", "See spend vs. your own history") — content should reflect the PRD's actual feature set (import, categorize, compare-to-history), not generic marketing filler.

**Contract**: The `clientLoader`/`meta` exports and the `!user` branch's role as the public-visitor view are unchanged. The value-prop items are static local data (no new loader dependency). Entrance animation is applied via `animate-[fade-slide-in_...]` utility classes (or a small local class using the Phase 1 keyframe) with staggered `animation-delay` per element (logo → heading → tagline → CTAs → value props). Ambient glow is a pseudo-element or wrapping `div` behind the logo using the `ambient-glow` keyframe from Phase 1.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Frontend builds cleanly: `npm run build`

#### Manual Verification:

- Logged-out `/` shows the logo, entrance animation plays once on load (not looping in a distracting way), ambient glow is subtle rather than flashy
- Value-prop section is legible and accurately describes the app in both light and dark mode
- CTA buttons/links use the new brand color and remain keyboard-accessible (visible focus state)
- No layout shift/flash before the logo image loads

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Header branding & authenticated home

### Overview

Add the logo to the shared `AppHeader`, and give the authenticated "Welcome back" homepage state a lighter version of the same refresh: branding via the header, brand-colored accents, and an entrance animation — without the value-prop marketing copy (not relevant to an already-registered user).

### Changes Required:

#### 1. Header branding

**File**: `MyFinances/frontend/app/components/AppHeader.tsx`

**Intent**: Replace the plain-text "MyFinances" span with the logo image (small, header-appropriate size), so branding is consistent on every authenticated screen that renders `AppHeader`.

**Contract**: `AppHeaderProps` (`{ showLogout: boolean }`) is unchanged. The log-out button's brand color reference updates from `text-blue-700`/`dark:text-blue-500` to the new `brand-*` token for consistency with Phase 2's CTAs.

#### 2. Authenticated homepage refresh

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Apply the same entrance animation treatment (fade/slide-in on the "Welcome back" heading and tagline) to the `user` branch (`home.tsx:55-69`), keeping the copy as-is — no value-prop section here since the visitor is already a user.

**Contract**: The `user` branch's structure (`AppHeader` + centered text block) is unchanged; only animation utility classes and any `blue-*` → `brand-*` color reference updates are added.

> **Implementation note**: a single small, restrained `ambient-glow` accent was added behind the "Welcome back" block during implementation (not in the original wording above) — disclosed and confirmed by the user during Phase 3's manual verification.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Frontend builds cleanly: `npm run build`

#### Manual Verification:

- Logged-in `/` shows the logo in the header on every load, log-out link still works
- "Welcome back" text has a subtle entrance animation matching the homepage's feel, without the value-prop section
- Both light and dark mode look intentional (no contrast regressions on the header logo or accent color)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Login & register visual refresh

### Overview

Extend the brand treatment established in Phases 1-3 to `login.tsx` and `register.tsx`: the logo, brand-colored inputs/buttons, and a lightweight entrance animation, consistent with the homepage's now-forced dark theme. This phase was added after Phase 2 shipped, per direct user feedback wanting these pages to "look similar" to the refreshed homepage.

### Changes Required:

#### 1. Login page branding

**File**: `MyFinances/frontend/app/routes/login.tsx`

**Intent**: Add the logo above the "Log in" heading (small, consistent with the header's sizing from Phase 3), replace the plain gray input borders and `blue-700`/`blue-500` submit-button/link colors with the `brand-*` scale, and apply a `fade-slide-in` entrance animation to the form container so the page doesn't feel like a jarring drop from the animated homepage.

**Contract**: `clientLoader` (redirect-if-authenticated) and the form's `handleSubmit`/fetch logic to `/api/auth/login` are unchanged — this is styling only. Inputs keep their existing `border`/`rounded-lg` structure; only the border/focus color references move to `brand-*` (e.g. `focus:border-brand-500` or `focus:ring-brand-500` if a focus ring is added). The submit button adopts the same `bg-brand-500 hover:bg-brand-600` treatment as the homepage's "Log in" CTA for visual consistency.

#### 2. Register page branding

**File**: `MyFinances/frontend/app/routes/register.tsx`

**Intent**: Apply the identical treatment as login.tsx — logo, brand-colored inputs/button, entrance animation — to keep the two auth pages visually identical in structure (they already share near-identical JSX shape today).

**Contract**: `clientLoader` and the `/api/auth/register` submit logic are unchanged. Reuse the exact color/animation classes from login.tsx rather than inventing new values, since the two pages are meant to look like a matched pair.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Frontend builds cleanly: `npm run build`

#### Manual Verification:

- `/login` and `/register` show the logo and brand-colored inputs/button, matching the homepage's dark visual language
- Form submission still works end-to-end (login with a valid account, register a new account) — no regression in the auth flow itself
- Entrance animation is subtle and doesn't delay the form becoming usable/focusable
- Keyboard navigation (tab order, focus rings) still works correctly on both forms

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Shared top navbar on every page

### Overview

Generalize `AppHeader` into a top navbar that appears on every page — public homepage, login, register, and authenticated home — instead of only the authenticated homepage. Logged-out visitors see the logo plus `Log in`/`Register` links in the navbar; authenticated users keep today's logo + `Log out`. This phase was added after Phase 4 shipped, per direct user feedback wanting consistent top-of-page navigation everywhere.

### Changes Required:

#### 1. Generalize `AppHeader` for guest and authenticated states

**File**: `MyFinances/frontend/app/components/AppHeader.tsx`

**Intent**: Replace the `showLogout: boolean` prop with a mode that also covers the logged-out case, so the same component renders `Log in` / `Register` links (brand-colored, matching the homepage hero's CTA styling) when the visitor isn't authenticated, and today's `Log out` button when they are.

**Contract**: New prop shape `{ authenticated: boolean }` (replaces `showLogout`). `authenticated: true` renders exactly what today's `showLogout` path renders (no visual change for existing authenticated call site). `authenticated: false` renders `Log in` and `Register` links instead. The existing call site in `home.tsx` (`<AppHeader showLogout />`) updates to `<AppHeader authenticated />`.

#### 2. Mount the navbar on the public homepage

**File**: `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Add `<AppHeader authenticated={false} />` above the `!user` branch's hero content. Since the logo now lives in the persistent navbar, remove the hero's large centered logo image + its dedicated ambient-glow wrapper (the duplication the user flagged) — the hero keeps its tagline, CTAs, value-prop cards, and the layered drifting background blobs for atmosphere. Adjust top spacing/padding now that a navbar occupies the top of the page.

**Contract**: `clientLoader`/`meta` unchanged. The `user` (authenticated) branch already renders `<AppHeader authenticated />` (renamed from `showLogout` per item 1) — no structural change there beyond the prop rename.

#### 3. Mount the navbar on login/register

**File**: `MyFinances/frontend/app/routes/login.tsx`, `MyFinances/frontend/app/routes/register.tsx`

**Intent**: Add `<AppHeader authenticated={false} />` above each page's form card, and remove the small inline logo + glow that Phase 4 added directly above the "Log in"/"Register" heading (now redundant with the navbar logo).

**Contract**: `clientLoader`/`handleSubmit` logic unchanged — styling/structure only. Both pages keep the same `fade-slide-in` entrance treatment on the remaining elements (heading, form, footer link), just without the now-removed inline logo block.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Frontend builds cleanly: `npm run build`

#### Manual Verification:

- Every page (public home, login, register, authenticated home) shows the same top navbar with the logo
- Logged-out navbar shows working `Log in`/`Register` links; authenticated navbar still shows a working `Log out` button
- No duplicate logo anywhere (hero and login/register no longer show their own separate logo now that the navbar has one)
- Layout doesn't feel cramped now that a navbar occupies the top of every page

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- None — this project has no test suite yet (per `CLAUDE.md`: "No test suite exists yet in either project"); verification is via `typecheck`, `build`, and manual browser checks.

### Integration Tests:

- None (see above).

### Manual Testing Steps:

1. Start both dev servers (`npm run dev` in `MyFinances/frontend`, `dotnet run` in `MyFinances/backend`), load `http://localhost:5173/` logged out.
2. Confirm the hero renders: logo, animated entrance, ambient glow, value props, brand-colored CTAs.
3. Register/log in, confirm redirect to `/` shows the authenticated "Welcome back" view with header branding and entrance animation.
4. Toggle OS dark mode (or emulate via devtools `prefers-color-scheme`) and re-check both states for contrast/legibility.
5. Resize to a narrow (mobile) viewport and confirm the logo and layout stay usable, not overflowing.

## Performance Considerations

Entrance/ambient animations are CSS-only (`transform`/`opacity`), which stay on the compositor thread and avoid layout thrashing. The logo PNG is a single ~100KB asset reused via one Vite-processed import — no additional image processing pipeline is introduced.

## Migration Notes

Not applicable — purely additive frontend/visual change, no data model or API changes.

## References

- Prior related work: `context/changes/homepage-redesign/` (closed; established the public/authenticated dual-view split this plan builds on)
- Project conventions: `CLAUDE.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Brand theme & asset wiring

#### Automated

- [x] 1.1 Type checking passes: `npm run typecheck` — 4d7b57d
- [x] 1.2 Frontend builds cleanly: `npm run build` — 4d7b57d

#### Manual

- [x] 1.3 `bg-brand-600`/`text-brand-600` resolve to the intended blue in a scratch element — 4d7b57d

### Phase 2: Public homepage hero

#### Automated

- [x] 2.1 Type checking passes: `npm run typecheck` — 667e084
- [x] 2.2 Frontend builds cleanly: `npm run build` — 667e084

#### Manual

- [x] 2.3 Logged-out `/` shows logo, one-shot entrance animation, ambient glow (expanded per user feedback to layered drifting blobs + panning gradient backdrop) — 667e084
- [x] 2.4 Value-prop section legible and accurate (dark mode is now the app-wide default per user feedback; light mode no longer applies) — 667e084
- [x] 2.5 CTA buttons/links use brand color and remain keyboard-accessible — 667e084
- [x] 2.6 No layout shift/flash before logo image loads — 667e084

### Phase 3: Header branding & authenticated home

#### Automated

- [x] 3.1 Type checking passes: `npm run typecheck` — 587a5d9
- [x] 3.2 Frontend builds cleanly: `npm run build` — 587a5d9

#### Manual

- [x] 3.3 Logged-in `/` shows logo in header on every load, log-out still works — 587a5d9
- [x] 3.4 "Welcome back" entrance animation matches homepage feel, no value-prop section — 587a5d9
- [x] 3.5 Light and dark mode both look intentional, no contrast regressions (dark-only app-wide per Phase 2 revision) — 587a5d9

### Phase 4: Login & register visual refresh

#### Automated

- [x] 4.1 Type checking passes: `npm run typecheck` — af5eb42
- [x] 4.2 Frontend builds cleanly: `npm run build` — af5eb42

#### Manual

- [x] 4.3 `/login` and `/register` show the logo and brand-colored inputs/button, matching the homepage's dark visual language — af5eb42
- [x] 4.4 Form submission still works end-to-end (login with a valid account, register a new account) — af5eb42
- [x] 4.5 Entrance animation is subtle and doesn't delay the form becoming usable/focusable — af5eb42
- [x] 4.6 Keyboard navigation (tab order, focus rings) still works correctly on both forms — af5eb42

### Phase 5: Shared top navbar on every page

#### Automated

- [x] 5.1 Type checking passes: `npm run typecheck` — af5eb42
- [x] 5.2 Frontend builds cleanly: `npm run build` — af5eb42

#### Manual

- [x] 5.3 Every page shows the same top navbar with the logo — af5eb42
- [x] 5.4 Logged-out navbar shows working Log in/Register links; authenticated navbar still shows working Log out — af5eb42
- [x] 5.5 No duplicate logo anywhere — af5eb42
- [x] 5.6 Layout doesn't feel cramped with the navbar occupying the top of every page (sticky glass navbar, pill CTAs) — af5eb42
