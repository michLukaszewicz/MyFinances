# Homepage Visual Refresh — Plan Brief

> Full plan: `context/changes/homepage-visual-refresh/plan.md`

## What & Why

The homepage currently reads as unfinished placeholder text — no logo, no color identity, no motion — which undersells the app to first-time visitors. This plan uses the user-supplied logo to give MyFinances a real brand identity and makes the homepage feel alive, so visitors are more likely to register and stay.

## Starting Point

`home.tsx` renders plain centered text for both the logged-out (marketing) and logged-in ("welcome back") states, with two underlined blue links as the only styling. `AppHeader` shows "MyFinances" as unstyled text. No animation library exists; Tailwind v4's `@theme` block currently only overrides the font. The logo PNG is saved at `MyFinances/frontend/app/assets/myfinances-logo.png`.

## Desired End State

Logged-out visitors see an animated hero: logo with ambient glow, staggered fade-in for heading/tagline/CTAs, brand-colored buttons, and a 3-item value-prop section (import, categorize, compare-to-history). Logged-in users see the same logo in the header everywhere, plus a lighter entrance animation on the "welcome back" screen — no marketing copy, since they're already convinced.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Scope | Homepage + shared header | Keeps branding consistent everywhere it's visible without touching login/register forms | Plan |
| Logo usage | Use PNG as-is, sized responsively | Zero extra tooling, ships immediately; raster quality is fine at hero sizes | Plan |
| Animation style | Entrance + subtle ambient motion | Feels alive without being distracting or gimmicky | Plan |
| Animation tech | CSS-only (Tailwind keyframes) | Matches the project's minimal-dependency stack; no bundle cost | Plan |
| Color theme | Adopt logo blue as accent color (`brand-*`) | Cohesive identity instead of a mismatched default Tailwind blue | Plan |
| Content | Add a short value-prop section | Gives visitors a concrete reason to register, not just a vague tagline | Plan |
| Dark mode | Same logo, verified for contrast | Logo's saturated blue reads fine on `gray-950`; avoids blocking on a second asset | Plan |
| Auth state | Both public and authenticated home get a refresh, at different depths | Consistent feel without redundant marketing copy for existing users | Plan |

## Scope

**In scope:**
- Public (logged-out) homepage hero redesign with logo, animation, and value props
- Shared `AppHeader` logo branding
- Authenticated "welcome back" homepage lighter refresh
- New `brand-*` Tailwind color token and shared CSS keyframes

**Out of scope:**
- `login.tsx` / `register.tsx` restyling
- Any animation library (Framer Motion, etc.)
- Recreating the logo as SVG or producing a dark-mode-specific variant
- Backend or auth-flow changes

## Architecture / Approach

Add the brand color token and shared `@keyframes` to `app.css` first (Phase 1), so the homepage hero (Phase 2) and the header/authenticated view (Phase 3) consume the same tokens instead of duplicating values. Phase 2 covers the highest-value surface (first-time visitors); Phase 3 extends the same building blocks to every other authenticated screen.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Brand theme & asset wiring | `brand-*` Tailwind color scale + shared entrance/ambient `@keyframes` | Picking a scale that doesn't match the logo's actual sampled blue |
| 2. Public homepage hero | Animated logged-out hero with logo, value props, brand CTAs | Ambient animation reads as distracting instead of subtle |
| 3. Header branding & authenticated home | Logo in header everywhere; lighter refresh on "welcome back" | Logo too small/cramped in the compact header bar |

**Prerequisites:** None — logo asset already saved, no new dependencies to install.
**Estimated effort:** ~1 session across 3 phases (small, single-app, CSS-only change).

## Open Risks & Assumptions

- The logo's exact hex wasn't color-picked precisely — Phase 1 implementation should sample the actual PNG pixel value rather than relying on the ~`#2E2BEA` estimate in this plan.
- "Subtle" ambient glow is a judgment call verified manually in Phase 2 — if it reads as too flashy or too faint, adjust the keyframe timing/opacity range before moving to Phase 3.

## Success Criteria (Summary)

- A first-time visitor sees a branded, animated hero with a clear value proposition instead of plain placeholder text
- Branding (logo + accent color) is consistent across the public homepage, header, and authenticated homepage
- No regressions in dark mode legibility or keyboard accessibility of CTAs
