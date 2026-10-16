<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Landing Page Redesign

- **Plan**: context/archive/2026-10-02-landing-page-redesign/plan.md
- **Scope**: Full plan (Phases 1-3 of 3)
- **Reviewed phases**: 1, 2, 3
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 3 observations

Method: code and git history only, judged against the landing commits 27f292b (p1), 1b4489f (p2) and 4d10a25 (p3), all dated 2026-10-07. No build, test or npm command was run. Success-criteria greps were re-evaluated statically with `git grep` at 4d10a25. `context/foundation/lessons.md` holds one rule (backend xUnit test layout) that does not apply to this frontend-only change. The landing folder is unchanged between 4d10a25 and HEAD.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Unplanned Reveal wrapper component

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/frontend/app/components/landing/Reveal.tsx:1
- **Detail**: The plan lists `useReveal.ts` and the section files but no `Reveal.tsx`. It was added in 1b4489f as a thin wrapper over `useReveal` (ref, `data-revealed`, `revealDelayStyle`) and is used by BanksStrip, Features, HowItWorks and ClosingCta. It keeps the hook contract from the plan and avoids repeating the same wiring four times. Hero and ProductMock use the plan's CSS entrance animation instead. Benign extra.
- **Fix**: None required. Optionally note the wrapper in the plan's file list if the plan is ever reused as a template.
- **Decision**: PENDING

### F2 — Manual checkboxes marked complete without recorded evidence

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/archive/2026-10-02-landing-page-redesign/plan.md:292-296
- **Detail**: Items 1.5-1.8, 2.5-2.8 and 3.5-3.9 are `[x]`, but the diffs (27f292b, 1b4489f, 4d10a25) change only the checkbox lines. There are no stored screenshots and no Lighthouse score or findings (3.9 allows "or findings documented"). The code does support the claims: reveal falls back to immediate when `prefers-reduced-motion` matches or IntersectionObserver is missing (useReveal.ts:34-39); the hidden state exists only under `.reveal-ready` (app.css:259-270); the hero uses CSS-only entrance animation (Hero.tsx:30-45); there is a single h1 and one `<main>`; the mock is labelled "Example data". Automated items match the diff: the banned-claims grep returns only "No budget required" (Hero.tsx:38, plus home.tsx og/meta text), the literal-colour grep returns nothing, there is exactly one `<h1`, and no backend files changed between 27f292b^ and 4d10a25. Manual results could not be independently confirmed from the repository.
- **Fix**: None for the archived change. For future plans, record a one-line note for Lighthouse or screenshot checks (score or path).
- **Decision**: PENDING

### F3 — Below-the-fold content is visible in static/SSR markup, then hidden after hydration

- **Severity**: 👁 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/landing/LandingPage.tsx:7-9
- **Detail**: `reveal-ready` is set in `useEffect`, as the plan prescribes (Critical Implementation Details, State sequencing). Sections that are already in view on a first paint (for example BanksStrip on a tall viewport) can therefore paint visible, snap to hidden, then reveal again. This is a known trade-off of the planned no-JS-safe design, not a deviation. Reduced motion is unaffected, because the transition is 0.01ms and `revealed` is true immediately.
- **Fix**: If a flash is ever seen, set `reveal-ready` in `useLayoutEffect`, or add a short guard that skips the hidden state for elements already intersecting at mount.
  - Strength: Removes the flash while keeping no-JS visibility.
  - Tradeoff: `useLayoutEffect` warns during SSR unless guarded.
  - Confidence: MED — not reproduced, judged from code only.
  - Blind spot: Whether SSR is enabled for this route, and whether the flash is visible in practice.
- **Decision**: PENDING

## Drift summary (plan vs. landed code)

| Plan item | Verdict | Evidence |
|-----------|---------|----------|
| LandingPage folder, `home.tsx` renders `<LandingPage />`, `valueProps` removed, `meta()` kept | MATCH | 27f292b home.tsx diff |
| `useReveal`: one shared observer, observe-once, unobserve after reveal, disconnect on unmount, reduced-motion and no-IO fallback | MATCH | useReveal.ts:3-77 |
| app.css: `.reveal` under `reveal-ready`, `float-slow`, `twinkle`, CSS starfield, only opacity/transform/translate, existing keyframes and reduced-motion block untouched | MATCH | app.css hunk in 27f292b |
| Hero: visible h1 with accent word, subhead, Button pair, aria-hidden decorative layers, reuses gradient-pan/drift-slow | MATCH (Register is placed before Log in, benign) | Hero.tsx |
| ProductMock: SVG donut, `--chart` palette via chartColor, deviation badges, "Example data" label, float-slow, round sample numbers | MATCH | ProductMock.tsx |
| BanksStrip: mBank and Erste CSV+PDF, VeloBank PDF, PLN note, no logos | MATCH | BanksStrip.tsx |
| Features and HowItWorks: 3 cards and 3 steps, staggered via delay, h2/h3 hierarchy, hover lift via transform | MATCH | Features.tsx, HowItWorks.tsx |
| ClosingCta: Register and Log in, footer with product name and year only | MATCH | ClosingCta.tsx |
| Meta: og:title, og:description, og:type, no og:image | MATCH | 4d10a25 home.tsx |
| CLAUDE.md UI rules | MATCH (3 bullets covering the planned rules) | 4d10a25 CLAUDE.md |
| Reveal.tsx wrapper | EXTRA (F1) | 1b4489f |

"What We're NOT Doing" was respected: no animation library, no images or logos, no backend or auth changes, and the dashboard branch is untouched. The Revolut claim was removed, and no banned claims appear in the landing copy.
