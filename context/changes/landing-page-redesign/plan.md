# Landing Page Redesign Implementation Plan

## Overview

Replace the logged-out view of the home route (one sentence, two links, three tiles) with a real, animated landing page of five sections that gives a visitor reasons to stay and scroll: hero, supported-banks strip, feature cards, "how it works", closing call to action with footer. The new markup lives in its own component folder; `home.tsx` only renders it. Animation stays CSS-only, with one small IntersectionObserver hook for scroll reveal.

## Current State Analysis

- The logged-out view is the `!user` branch of `Home` in `MyFinances/frontend/app/routes/home.tsx:254-321`: a `min-h-screen` centered `max-w-lg` column with a backdrop gradient (`gradient-pan`), two blurred blobs (`drift-slow`, `drift-slow-reverse`), an `sr-only` h1, one tagline, a Log in/Register pair and a `valueProps` grid (`home.tsx:21-34`).
- Keyframes in `app/app.css:27-86`: `fade-slide-in`, `ambient-glow` (unused on this branch), `drift-slow`, `drift-slow-reverse`, `gradient-pan`; a global `prefers-reduced-motion` block (`app.css:88-96`) shortens all animation and transition durations, so new animations inherit it.
- `valueProps[0]` (`home.tsx:24`) claims "CSV exports from mBank, Revolut, and Erste" — Revolut import was dropped from the MVP (S-07, roadmap, 2026-10-02); the PDF half (mBank, Erste, VeloBank) is accurate.
- Assets: only `app/assets/myfinances-logo.png` and `public/favicon.ico`; no screenshots or illustrations. `AppHeader` already renders the logo, so the page must not repeat it.
- Public vs authenticated: `/` shows the landing when `/api/auth/me` fails (`home.tsx:131-153`); `login`/`register` redirect home when signed in. SEO today is one `meta()` (`home.tsx:36-45`): title and description, no og tags.
- The Chalk theme change (`ui-chalk-theme-dashboard`) is planned but not implemented at planning time: no `--primary`/`@theme inline` tokens, no `app/components/ui/`. This plan assumes its Phase 1 has landed.

## Desired End State

A visitor to `/` signed out sees a full-height hero with a large headline, a CTA pair, an animated starfield/glow background and a floating product mock; scrolling reveals a banks strip, three feature cards, a three-step explainer and a closing CTA with footer, each section fading/sliding in once. Every claim matches what the app does today. The page works at 375px and 1440px, honours reduced motion, shows visible keyboard focus, and the signed-in dashboard is unchanged. Verify: `npm run typecheck` and `npm run build` pass; the literal-colour and banned-claim greps return nothing; screenshots at both widths look right.

### Key Discoveries:

- Reduced motion is already global (`app.css:88-96`), but an observer-driven reveal must also reveal immediately under that media query, otherwise sections could stay hidden.
- Entrance animations use `both` fill and `transform`/`opacity` only (`archive/2026-10-02-homepage-visual-refresh/plan.md:90, 265`); keep that.
- Past decisions: no animation library (`archive/2026-10-02-homepage-visual-refresh/plan-brief.md:24`), no duplicate logo (`plan.md:238`), no fake dashboard widgets (`archive/2026-10-02-homepage-redesign/plan-brief.md:21, 36-38`) — the mock must be clearly labelled as example data.
- Sample claims allowed by the roadmap (all slices done): mBank and Erste CSV+PDF import, VeloBank PDF, PLN only, side-by-side duplicate review, manual categorization with no auto-suggestions, internal-transfer flagging, spend and income donuts, trend chart, above/below/in-line average signal, per-user private data.
- Must not be claimed: budgets, Revolut, auto/AI categorization, bank-API sync, other currencies, sharing, mobile app, OAuth login, compliance claims (roadmap S-05/S-07 dropped; `prd.md` and `CLAUDE.md` are stale on budgets and Revolut).

## What We're NOT Doing

- No animation library, no Framer Motion, no canvas/WebGL; no scroll-jacking.
- No customer logos, testimonials or fake metrics (none exist); no screenshots of the real app.
- No changes to the signed-in dashboard, `AppHeader` markup, auth, routes or backend.
- No light mode, no new fonts beyond the Chalk theme's.
- No blog/pricing/docs pages; single `/` route only.
- No logo redesign (PNG stays in the header).

## Implementation Approach

Build on the Chalk tokens and `Button`/`Card` so the landing is the first consumer of the new design system; keep all colour in tokens. Extract the landing into `app/components/landing/` (one file per section) so `home.tsx` shrinks and Chalk's Phase 3 landing re-tokenising is superseded cleanly. Scroll reveal: a `useReveal` hook sets a `data-revealed` attribute through one shared IntersectionObserver; CSS transitions `opacity`/`translate` from the hidden state; the hidden initial state is applied only when JS has marked the page as ready, so content is visible without JS and under reduced motion.

## Critical Implementation Details

- **Timing & lifecycle**: the clientLoader decides signed-in vs signed-out after render; the landing must mount only on the `!user` branch and the observer must be disconnected on unmount so signed-in navigation leaves no observers.
- **State sequencing**: apply the "hidden" starting style only after the hook has run on the client (e.g. a `reveal-ready` class on the section root set in an effect), never in the static markup, so first paint and no-JS render show all content.

## Phase 1: Foundation and hero

### Overview

Extract the landing, add the reveal mechanism and background animation, and ship the new hero with corrected copy.

### Changes Required:

#### 1. Landing component folder

**File**: `MyFinances/frontend/app/components/landing/LandingPage.tsx` (new), `MyFinances/frontend/app/routes/home.tsx`

**Intent**: Move the signed-out UI out of `home.tsx` into a `LandingPage` component composed of section components; `home.tsx` keeps `meta()`, the loader and the dashboard.

**Contract**: `home.tsx` `!user` branch becomes `<AppHeader authenticated={false} /><LandingPage />`; `valueProps` and the landing-only markup are removed from `home.tsx`. `meta()` stays exported from `home.tsx`.

#### 2. Scroll-reveal hook

**File**: `MyFinances/frontend/app/lib/useReveal.ts` (new)

**Intent**: Reveal sections once as they enter the viewport, using IntersectionObserver, without a library.

**Contract**: `useReveal<T extends HTMLElement>(): { ref, revealed }` (or an equivalent attribute-based API); observes once, unobserves after reveal, disconnects on unmount; returns `revealed = true` immediately when `window.matchMedia("(prefers-reduced-motion: reduce)")` matches or `IntersectionObserver` is undefined. Optional per-item delay is expressed via an inline `animation-delay`/`transition-delay` custom property.

#### 3. Animation styles

**File**: `MyFinances/frontend/app/app.css`

**Intent**: Add the reveal and decoration animations next to the existing keyframes, token-coloured.

**Contract**: A `.reveal` utility (initial `opacity:0; translate: 0 16px`, transitions to visible when `[data-revealed]`, only applied under a `reveal-ready` ancestor); keyframes `float-slow` (translateY loop, for the mock) and `twinkle` (starfield opacity); starfield rendered as layered `radial-gradient`s or a handful of absolutely positioned dots in CSS, no image. Animate only `opacity`/`transform`. Existing keyframes and the reduced-motion block are unchanged.

#### 4. Hero

**File**: `MyFinances/frontend/app/components/landing/Hero.tsx`

**Intent**: A full-height hero with a large headline (one accent-coloured word), a one-sentence subhead, Log in / Register as Button (default + outline), starfield and glow background.

**Contract**: Visible `<h1>` (replacing the `sr-only` one); headline and subhead state the wedge ("compare your spend with your own history, no budget required") and make no claim from the do-not-claim list; Buttons link to `/login` and `/register` with the token focus ring; backdrop reuses `gradient-pan`/`drift-slow` blobs recoloured with `--primary`/`--secondary`; decorative layers `aria-hidden` and `pointer-events-none`. Mock slot below the CTAs is filled in Phase 2.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck` (from `MyFinances/frontend`)
- Production build passes: `npm run build`
- No Revolut mention on the landing: `grep -rniE "revolut" MyFinances/frontend/app/routes/home.tsx MyFinances/frontend/app/components/landing` returns no lines
- No literal colours in the new files: `grep -rnE "#[0-9a-fA-F]{3,6}|(gray|red|green|emerald|amber|brand)-[0-9]" MyFinances/frontend/app/components/landing MyFinances/frontend/app/lib/useReveal.ts` returns no lines

#### Manual Verification:

- Signed out, `/` shows the hero with animated background and a visible headline; Log in and Register work
- With the OS "reduce motion" setting on, content is fully visible and nothing loops
- With JavaScript disabled in dev tools, the hero text and buttons are visible
- Signed in, `/` still shows the unchanged dashboard

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Product mock and content sections

### Overview

Add the animated product mock to the hero and the banks, features and how-it-works sections, each revealed on scroll.

### Changes Required:

#### 1. Product mock

**File**: `MyFinances/frontend/app/components/landing/ProductMock.tsx`

**Intent**: A floating card under the hero that previews the spend donut and a category list with the average-deviation badges, built in JSX/SVG with sample data.

**Contract**: Donut as SVG circles (`stroke-dasharray`) coloured with `--chart-1..N`; list rows with category name, amount and an "Above average"/"Below average"/"In line" badge in the same token tints as the real chart; a visible "Example data" label; `float-slow` on the card; the whole mock `aria-hidden` or labelled as an illustration, with sample numbers that are obviously round (no real-looking personal data).

#### 2. Banks strip

**File**: `MyFinances/frontend/app/components/landing/BanksStrip.tsx`

**Intent**: A row of the banks the app imports from, as text pills (no third-party logos), with a short note on formats.

**Contract**: mBank and Erste Bank Polska (CSV and PDF), VeloBank (PDF); copy states PLN accounts; reveal on scroll.

#### 3. Feature cards and steps

**File**: `MyFinances/frontend/app/components/landing/Features.tsx`, `HowItWorks.tsx`

**Intent**: Three Card tiles (import and duplicate review; categorize in minutes with transfers flagged; spend vs. your own history) and a three-step explainer (import, categorize, compare), staggered reveal.

**Contract**: Copy limited to the safe-to-claim list in Key Discoveries; Card hover lift using `transform`/`opacity` only; stagger through the `useReveal` delay variable; headings form a valid hierarchy (h2 per section, h3 per card).

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Production build passes: `npm run build`
- No banned claims in landing copy: `grep -rniE "budget|auto-categor|machine learning|\bAI\b|open banking|revolut|multi-currency" MyFinances/frontend/app/components/landing` returns only the allowed wedge phrase "no budget required" (reviewed by the implementer)
- No literal colours in the landing files (same grep as Phase 1)

#### Manual Verification:

- Scrolling reveals each section once, in order, without jank or layout shift
- The mock reads clearly as an example and its badge colours match the real dashboard
- Every sentence on the page is true of the current app
- Page is usable at 375px width: no horizontal scroll, mock and cards stack

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Closing CTA, polish and verification

### Overview

Finish the page, tighten SEO and accessibility, and verify end to end.

### Changes Required:

#### 1. Closing CTA and footer

**File**: `MyFinances/frontend/app/components/landing/ClosingCta.tsx`

**Intent**: A final call to action with Register (primary) and Log in, plus a minimal footer.

**Contract**: Footer holds the product name and year only (no links to unbuilt pages); Buttons reuse the same destinations as the hero.

#### 2. Meta tags

**File**: `MyFinances/frontend/app/routes/home.tsx` (`meta()` only)

**Intent**: Improve link previews without new assets.

**Contract**: Keep `title`/`description`; add `og:title`, `og:description`, `og:type=website`; no `og:image` (none exists).

#### 3. UI rule

**File**: `CLAUDE.md` (repo root), the UI conventions section added by `ui-chalk-theme-dashboard` (create it outside any 10x-cli block if absent)

**Intent**: Record the landing conventions for later agents.

**Contract**: Short rules: public-page animation is CSS plus `useReveal`, no animation library; landing copy may only claim implemented features (check the roadmap, not `prd.md`); reveal starting style only under `reveal-ready`; animate `opacity`/`transform` only.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Production build passes: `npm run build`
- Backend untouched: `git diff --stat main -- MyFinances/backend` shows no changes
- Landing has exactly one h1: `grep -rc "<h1" MyFinances/frontend/app/components/landing` totals 1

#### Manual Verification:

- Full-page screenshots at 1440px and 375px reviewed and acceptable
- Tab order reaches every link and button in the page with a visible focus ring; the entrance animation never delays focus
- Reduced-motion setting shows a static, complete page
- Signed-in dashboard and the login, register, import, categorize and settings routes render as before
- Lighthouse accessibility score for `/` signed out is ≥ 90 (or documented findings)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- None: the frontend has no test framework and none is added. `useReveal` is behaviour-light; its reduced-motion and no-IntersectionObserver fallbacks are checked manually.

### Integration Tests:

- None added; typecheck, build and the greps are the automated gates.

### Manual Testing Steps:

1. Log out and open `/` at 1440px; scroll top to bottom and watch each reveal.
2. Repeat at 375px and in a narrow tablet width.
3. Toggle the OS reduced-motion setting and reload; toggle JavaScript off and reload.
4. Tab through the page; log in and confirm the dashboard.

## Performance Considerations

Animate `opacity`/`transform` only; one shared observer; no images added (mock is SVG/CSS), so no extra network cost; infinite animations limited to the background and mock float.

## Migration Notes

No data or API changes. The `valueProps` constant and landing markup leave `home.tsx`; Chalk's Phase 3 landing re-tokenising becomes redundant for this branch if this change lands first, and is superseded by this change if it lands after.

## References

- Change notes: `context/changes/landing-page-redesign/change.md`
- Design-system dependency: `context/changes/ui-chalk-theme-dashboard/plan.md` (Phase 1 tokens, Button, Card)
- Current landing: `MyFinances/frontend/app/routes/home.tsx:254-321`
- Keyframes and reduced motion: `MyFinances/frontend/app/app.css:27-96`
- Prior decisions: `context/archive/2026-10-02-homepage-visual-refresh/plan-brief.md:24`, `context/archive/2026-10-02-homepage-redesign/plan-brief.md:21`
- Feature truth: `context/foundation/roadmap.md` (S-05 and S-07 dropped)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Foundation and hero

#### Automated

- [x] 1.1 Type checking passes: `npm run typecheck`
- [x] 1.2 Production build passes: `npm run build`
- [x] 1.3 No Revolut mention on the landing (grep returns no lines)
- [x] 1.4 No literal colours in the new landing files and hook (grep returns no lines)

#### Manual

- [ ] 1.5 Signed out, `/` shows the hero with animated background and a visible headline; Log in and Register work
- [ ] 1.6 With OS reduce-motion on, content is fully visible and nothing loops
- [ ] 1.7 With JavaScript disabled, the hero text and buttons are visible
- [ ] 1.8 Signed in, `/` still shows the unchanged dashboard

### Phase 2: Product mock and content sections

#### Automated

- [ ] 2.1 Type checking passes: `npm run typecheck`
- [ ] 2.2 Production build passes: `npm run build`
- [ ] 2.3 No banned claims in landing copy (grep reviewed; only "no budget required" allowed)
- [ ] 2.4 No literal colours in the landing files (grep returns no lines)

#### Manual

- [ ] 2.5 Scrolling reveals each section once, in order, without jank or layout shift
- [ ] 2.6 The mock reads clearly as an example and its badge colours match the real dashboard
- [ ] 2.7 Every sentence on the page is true of the current app
- [ ] 2.8 Page is usable at 375px width: no horizontal scroll, mock and cards stack

### Phase 3: Closing CTA, polish and verification

#### Automated

- [ ] 3.1 Type checking passes: `npm run typecheck`
- [ ] 3.2 Production build passes: `npm run build`
- [ ] 3.3 Backend untouched: `git diff --stat main -- MyFinances/backend` shows no changes
- [ ] 3.4 Landing has exactly one h1

#### Manual

- [ ] 3.5 Full-page screenshots at 1440px and 375px reviewed and acceptable
- [ ] 3.6 Tab order reaches every link and button with a visible focus ring; the entrance animation never delays focus
- [ ] 3.7 Reduced-motion setting shows a static, complete page
- [ ] 3.8 Signed-in dashboard and the other routes render as before
- [ ] 3.9 Lighthouse accessibility score for `/` signed out is at least 90, or findings documented
