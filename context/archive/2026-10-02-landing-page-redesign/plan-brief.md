# Landing Page Redesign — Plan Brief

> Full plan: `context/changes/landing-page-redesign/plan.md`
> Change notes: `context/changes/landing-page-redesign/change.md`

## What & Why

Turn the signed-out home view into a real landing page, with animation and a product preview, that gives visitors a reason to stay and scroll. Today it is one sentence, two links and three tiles. Reference for the feel: sentry.io/welcome (dark hero, big headline, floating product visuals, sections that reveal on scroll).

## Starting Point

The `!user` branch of `home.tsx` (lines 254-321) holds a centered column over a gradient and two drifting blobs; `app.css` has five keyframes and a global reduced-motion block. There are no screenshots or illustrations, only the logo in the header. One current claim is wrong: it promises Revolut CSV import, which was dropped. The Chalk theme (tokens, Button, Card) is planned but not yet built; this plan assumes its Phase 1 is done.

## Desired End State

Signed out, `/` shows a full-height hero (large headline, CTA pair, starfield/glow, floating example dashboard), then a banks strip, three feature cards, a three-step explainer and a closing CTA with footer, each fading in once on scroll. Every claim matches what the app does today. It works at 375px and 1440px, respects reduced motion, and the signed-in dashboard is untouched.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Separate change | Own change, planned after Chalk Phase 1 | New design scope; keep the theme plan unchanged | Plan (user) |
| Page length | 5 sections: hero, banks, features, how it works, closing CTA | Looks like a real page without inventing content | Plan (user) |
| Scroll animation | CSS transitions + small IntersectionObserver hook, no library | Works in every modern browser; keeps the no-library rule | Plan (user) |
| Product visual | Animated mock built in JSX/SVG, labelled "Example data" | Shows the product without screenshots; honest about being a sample | Plan (user) |
| Copy truth | Claims limited to implemented slices; remove Revolut | Roadmap says budgets and Revolut were dropped | Research |
| Structure | New `app/components/landing/` folder, `home.tsx` renders it | Shrinks `home.tsx`; clean hand-off from Chalk's landing tokenising | Plan |
| Visibility safety | Hidden start style only after JS marks `reveal-ready`; reduced motion reveals at once | Content never stays invisible | Plan |

## Scope

**In scope:** landing component folder, `useReveal` hook, new keyframes in `app.css`, hero, mock, banks strip, feature cards, steps, closing CTA and footer, og meta tags, a `CLAUDE.md` rule.

**Out of scope:** the signed-in dashboard, `AppHeader`, auth, routes, backend, customer logos or testimonials, real screenshots, animation libraries, light mode, extra pages.

## Architecture / Approach

`home.tsx` renders `AppHeader` plus `LandingPage`, which stacks section components. Each section wraps its content with `useReveal`, which flips a `data-revealed` attribute once; CSS handles the fade/slide using `opacity` and `transform` only. Colours come from the Chalk tokens; the mock donut is SVG using `--chart-N`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Foundation and hero | Landing extracted, `useReveal`, animations, new hero, Revolut fix | Hidden content if reveal fails (no JS / reduced motion) |
| 2. Product mock and content sections | Example dashboard mock, banks strip, feature cards, steps | A claim that is not true of the app, or a mock that looks real |
| 3. Closing CTA, polish and verification | Closing CTA, footer, og tags, a11y and responsive checks, rule | Mobile layout and focus regressions |

**Prerequisites:** Chalk Phase 1 done (tokens, Button, Card, `npm install`).
**Estimated effort:** ~2-3 sessions across 3 phases.

## Open Risks & Assumptions

- Blocked until `ui-chalk-theme-dashboard` Phase 1 lands; its Phase 3 landing re-tokenising is superseded by this change.
- Sentry was inspected only at the top of the page and by text; the section list is inspired by it, not copied.
- Exact headline and body copy is written during implementation within the allowed-claims list; it may need your review.
- No frontend tests exist; verification is typecheck, build, greps and manual checks.

## Success Criteria (Summary)

- The signed-out page feels like a product site: animated, scrollable, with a clear path to register.
- Everything stated on the page is true of the app today, and nothing is hidden for reduced-motion or no-JS visitors.
- The dashboard and other routes behave exactly as before.
