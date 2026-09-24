<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Homepage Visual Refresh Implementation Plan

- **Plan**: context/changes/homepage-visual-refresh/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-09-24
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 1 observation

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

### F1 — No `prefers-reduced-motion` accommodation

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/app.css
- **Detail**: Every animated element across the refreshed pages (`fade-slide-in` entrances, `ambient-glow`, `drift-slow`/`drift-slow-reverse`, `gradient-pan`) runs unconditionally. There is no `@media (prefers-reduced-motion: reduce)` override anywhere in the stylesheet, which is a real gap for motion-sensitive users (WCAG 2.3.3), especially since several of these animations are infinite/ambient rather than one-shot.
- **Fix**: Add a global override in `app.css` that disables/shortens the `animate-*` utilities under `@media (prefers-reduced-motion: reduce)` (e.g. `animation: none !important` scoped to elements using the arbitrary-value `animate-[...]` classes, or a broader `*, *::before, *::after { animation-duration: 0.01ms !important; animation-iteration-count: 1 !important; }` rule).
- **Decision**: FIXED — added `@media (prefers-reduced-motion: reduce)` override in app.css

### F2 — Navbar controls missing `focus-visible` styling

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/frontend/app/components/AppHeader.tsx:21-35
- **Detail**: Every other interactive element added in this refresh (home.tsx's CTAs, login/register's submit buttons) consistently uses `focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500`. The navbar's three controls (`Log out`, `Log in`, `Register`) — the one component present on literally every page — never got this treatment and rely on browser-default focus only, which is inconsistent with the rest of the refresh and may be visually suppressed by the header's dark backdrop-blur background in some browsers.
- **Fix**: Add the same `focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500` pattern to all three `AppHeader` controls.
- **Decision**: FIXED — added focus-visible outline to Log out, Log in, and Register

### F3 — AppHeader's Log out/Log in links no longer brand-colored (Phase 3 drift)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: MyFinances/frontend/app/components/AppHeader.tsx:24-32
- **Detail**: Phase 3's plan explicitly required "The log-out button's brand color reference updates from `text-blue-700`/`dark:text-blue-500` to the new `brand-*` token." It did, briefly — but the later "make the navbar modern" pass (done live, per your feedback, before Phase 4/5 committed) restyled `Log out` and `Log in` to neutral `text-gray-300 hover:text-white`, leaving only `Register` brand-colored as the pill CTA. This is a deliberate design choice (common nav pattern: one colored primary action, neutral secondary links) made in direct response to your "doesn't look modern" feedback, but it does contradict Phase 3's literal plan text and was never explicitly called out as a decision at the time.
- **Fix A ⭐ Recommended**: Accept as intentional — document this in the plan as the final decision (single-accent-color nav is a deliberate, already-shipped design call).
  - Strength: Matches what you already approved when you saw and liked the modern navbar; no code change, no risk of re-breaking the look you approved.
  - Tradeoff: Plan text technically stays inaccurate unless updated for the record.
  - Confidence: HIGH — you explicitly reviewed and approved this navbar look in this session.
  - Blind spot: None significant.
- **Fix B**: Recolor `Log out`/`Log in` to `text-brand-400 hover:text-brand-300` to literally match Phase 3's original wording.
  - Strength: Restores literal plan compliance.
  - Tradeoff: Makes every nav item brand-colored, diluting `Register`'s visual priority as the primary CTA — likely a step backward from the "modern" look you just approved.
  - Confidence: MEDIUM — plausible this looks worse, but not verified visually.
  - Blind spot: Haven't rendered Fix B to compare.
- **Decision**: FIXED via Fix B — recolored Log out/Log in to text-brand-400 hover:text-brand-300

### F4 — Extra ambient-glow blob on authenticated home (undocumented in plan text)

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/frontend/app/routes/home.tsx:113-116
- **Detail**: Phase 3's plan text only asked for a fade/slide-in entrance on the authenticated "Welcome back" block. The implementing subagent (acting on this session's explicit "optionally add one small, restrained ambient glow accent... if it fits naturally" instruction) added one small `ambient-glow` blob behind it. This was disclosed to you as "one restrained ambient glow" when Phase 3 was presented for manual verification, and you confirmed the phase — so it's known and accepted, just never literally added to the plan's "Changes Required" text.
- **Fix**: No action needed — already disclosed and approved. Optionally add a one-line note to Phase 3 in plan.md for the record.
- **Decision**: FIXED — added implementation note to Phase 3 in plan.md

