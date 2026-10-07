---
date: 2026-10-02T14:47:59+02:00
researcher: Claude (Sonnet 5.5)
git_commit: d3a61faeefd290b15a770cf1e8747ab6da80874b
branch: claude/ui-design-10x-ui-ed6a1f
repository: Kurs 10xDev (MyFinances)
topic: "What must change to apply the Chalk tweakcn theme (dark only) to the dashboard view?"
tags: [research, codebase, frontend, tailwind-v4, shadcn, tokens, recharts, home, AppHeader, CategorySpendDonut, CategoryTrendChart]
status: complete
last_updated: 2026-10-02
last_updated_by: Claude (Sonnet 5.5)
---

# Research: Applying the Chalk theme to the dashboard

**Date**: 2026-10-02T14:47:59+02:00 · **Branch**: claude/ui-design-10x-ui-ed6a1f · **Commit**: d3a61fa (change folder is untracked)

## Research Question

For `ui-chalk-theme-dashboard` (dark mode only, Chalk values in `theme-values.md`, dashboard = `home.tsx` + `AppHeader` + `CategorySpendDonut` + `CategoryTrendChart`): what does the codebase look like today, what must a semantic token block and 2–3 shadcn-path components integrate with, and what constraints/conflicts exist?

## Summary

- **No token layer exists.** `app/app.css` holds one plain `@theme` block with `--font-sans` (Inter) and `--color-brand-50..900` (app.css:5-19); no `:root`, `.dark` or `@theme inline` block. The dark background is a literal `bg-gray-950` (app.css:21-24). The app is dark-only by hard-coded `className="dark"` on `<html>` (root.tsx:28) and a class-based `@custom-variant dark` already exists (app.css:3), which is what shadcn's token block expects.
- **Literal colours are everywhere in the dashboard files**: gray-* / red / emerald / amber / brand-* Tailwind utilities in `home.tsx` and `AppHeader`, and hex literals in both charts (this inspected set only; counts below are grep-line counts, approximate).
- **Charts bypass CSS entirely**: two byte-identical 12-hex `PALETTE` arrays (CategorySpendDonut.tsx:44, CategoryTrendChart.tsx:34) plus hex tooltip/grid/axis colours (CategorySpendDonut.tsx:181-186; CategoryTrendChart.tsx:298-307).
- **Hard constraint from prior changes**: categories need ≥12 stable, distinct colour slots, identical in both charts. Chalk supplies only `chart-1..5`, so it cannot replace the palette 1:1.
- **Blast radius**: tokens in `app.css` are global; `AppHeader` is imported by 6 routes (home, import, categorize, settings, login, register) so restyling it changes every page. Charts are imported only by `home.tsx`. Other routes keep `brand-*`/`gray-*` and will look mixed until follow-ups.
- **shadcn is not installed**: no `components.json`, no `cn()`, none of clsx/tailwind-merge/cva/lucide/radix/tw-animate-css, no `app/components/ui`. No prior decision rejects it. The alias is `~/*` → `./app/*` (tsconfig.json:12-14), not shadcn's default `@/`.
- **Brand conflict**: Chalk primary is orange (≈ oklch 0.72 0.13 50); the logo/`brand-*` accent is blue and is used for focus outlines and links across all routes.

## Detailed Findings

### Current styling infrastructure (observed)

- `app.css:1` `@import "tailwindcss"`; `:3` dark custom variant `&:where(.dark, .dark *)`; `:5-19` `@theme` (non-inline); `:21-25` `html, body { bg-gray-950; color-scheme: dark }`; keyframes fade-slide-in, ambient-glow, drift-slow, drift-slow-reverse, gradient-pan and a reduced-motion block follow (app.css:27-96). There is no body text colour.
- `root.tsx:13-24` loads Inter from Google Fonts via `<link>`; `root.tsx:28` hard-codes `dark`; `ErrorBoundary` (root.tsx:48-75) has no colours (inherits body).
- `package.json` (caret ranges): react ^19.2.8, react-router ^8.4.0, recharts ^3.10.1, tailwindcss + @tailwindcss/vite ^4.2.2, vite ^8.0.3. Scripts: dev, build, typecheck only. No lint/format/test config in the frontend. Resolved versions in `package-lock.json` were not read. `node_modules` is absent in this worktree (npm install not run).
- `vite.config.ts` plugins: `tailwindcss()`, `reactRouter()`, `resolve.tsconfigPaths: true`, `/api` proxy to :5007.
- `app/lib` holds `api.ts`, `categories.ts`, `period.ts`; no `cn`/`clsx` helper. `app/components` holds only the three components named above.

### Literal-colour inventory by semantic role (dashboard files)

| Role | Current literal | Representative anchors |
|---|---|---|
| Page bg | `bg-gray-950`; header `bg-gray-950/70 border-white/5` | app.css:23, AppHeader.tsx:19 |
| Card | no fill; `border border-gray-800 rounded-lg p-3/p-4` (4 uses) | home.tsx:308, 503, 520, 787 |
| Text primary / muted / faint | `text-gray-200` (~25) / `text-gray-400` (~14) / `text-gray-500` (2) | home.tsx:310, 278, 791 |
| Solid primary button | `bg-brand-500 text-white hover:bg-brand-600` (5 incl. active toggle) | home.tsx:290, 643, 870; AppHeader.tsx:70; CategoryTrendChart.tsx:106 |
| Outline brand button | `text-brand-400 ring-1 ring-inset ring-brand-400` (1) | home.tsx:296 |
| Ghost brand link | `text-brand-400 hover:bg-white/5 hover:text-brand-300` (~10; same long string ×6 in AppHeader) | AppHeader.tsx:30-68; home.tsx:770, 807 |
| Neutral outline button | `border-gray-700 text-gray-200 hover:bg-white/5` (4) | home.tsx:514, 650, 661, 850 |
| Input/select | 3 string variants, 11+ uses; `controlClass`/`periodInputClass` defined twice | home.tsx:535, 549, 564, 577, 597, 693-729; CategoryTrendChart.tsx:101-102 |
| Focus ring | inputs: `focus:ring-1 focus:ring-brand-500`; buttons/links: `focus-visible:outline-2 outline-brand-500`; ≥8 buttons in home.tsx have none (514, 632, 650, 770, 807, 816, 820, 832) | home.tsx:290, 296, 643, 661, 850, 870; AppHeader (9 uses) |
| Income / expense | `text-emerald-500` / `text-red-500` | home.tsx:797-800 |
| Errors | `text-red-600` (home 620, 842; both chart load errors) vs `text-red-400` (home 734, Trend 267) — inconsistent | |
| Warning banner | `border-amber-700/60 bg-amber-950/30 text-amber-300` | home.tsx:623-632 |
| Badge | `DEVIATION_BADGE` map: red-900/40+red-300, green-900/40+green-300, gray-700+gray-300 (only badge; uses `green` while amounts use `emerald`) | CategorySpendDonut.tsx:20-24 |
| Chart tooltip/axes/grid | `#111827`, `#374151`, `#e5e7eb`, `#9ca3af` | see Summary anchors |
| Glow blobs | brand-based gradients | home.tsx:260-272, 672 |
| Overlay/modal | none found in these files | — |

Notes: this table is from a worker that did not read all of `home.tsx` (lines ~316-500 and ~600-660 only partly seen); counts are grep-line matches. Candidate shared components by repeated pattern: Button (solid/outline/ghost/neutral variants), Input/Select, Card, Badge; there is no `<table>` (transaction list is `<ul>/<li>`, home.tsx:783-787). Pre-audit in change.md:22 counted 113 literal-colour lines (home 50, AppHeader 8, Donut 27, Trend 28); this research did not independently recount.

### Charts

- Both read no CSS variables. The 12-hex `PALETTE` is `#6366f1 #f59e0b #10b981 #ef4444 #3b82f6 #ec4899 #14b8a6 #f97316 #8b5cf6 #84cc16 #06b6d4 #eab308`, indexed by the category's position in the shared category list modulo length (CategorySpendDonut.tsx:145; CategoryTrendChart.tsx:183). Legend dots use inline `backgroundColor`.
- `CategoryDto` is `{id, name, kind}` — no colour field, and no backend colour field was found (lib/categories.ts:2-6).
- Recharts `fill`/`stroke` accept CSS `var()` strings; this is stated by a worker and by shadcn chart docs showing `--chart-n` variables, but was not verified against Recharts 3.10 in this session.

### Prior decisions that constrain the change (archive/changes)

- Palette ≥12 entries, modulo-indexed (`archive/2026-09-28-category-spend-donut-chart/reviews/plan-review.md:57-65`); colours must remain stable per category across both charts (`archive/2026-10-02-category-trend-line-chart/reviews/impl-review.md:46-53`). Dark-theme contrast on `bg-gray-950` was a manual acceptance criterion (donut `plan.md:172, 240`).
- Dark forced app-wide, light mode dropped (`archive/2026-10-02-homepage-visual-refresh/plan.md:301, 316`); app.css `@theme` declared the single place for colour tokens (`plan.md:13`); every interactive element needs a visible `focus-visible` outline in `outline-brand-500` (`reviews/impl-review.md:40-41`); CSS-only animation, "minimal-dependency stack" (`plan-brief.md:24`). Verdicts: dark-only — still holds and matches the user decision in change.md:19; single-token-home — partially superseded if a `:root/.dark` block is added; "minimal-dependency" — a stance on animation, not a ban on UI libraries (no document found that rejects shadcn/Radix).
- PRD NFRs (prd.md:96-100) contain three items (user-scoped data, visible feedback within a few seconds, modern desktop browser); none mentions contrast/WCAG/language. UI copy is English (`lang="en"`, root.tsx:28).
- `docs/reference/contract-surfaces.md` does not exist (gap). `context/foundation/lessons.md` has one lesson (backend test AAA); no UI lessons apply.
- Other open changes (bootstrap-verification, mbank-single-page-pdf, mutation-testing-stryker, testing-import-integrity-dedup) were checked only by their `change.md` and carry no frontend styling content.

### External: shadcn / Tailwind v4 (Context7, `/websites/ui_shadcn`)

- Token layout: `@custom-variant dark (&:is(.dark *))`, `@theme inline { --color-X: var(--X) … }` for background, foreground, card, popover, primary, secondary, muted, accent, destructive, border, input, ring, chart-1..5, radius scale, sidebar-*; values in `:root` and `.dark`; base layer `* { @apply border-border outline-ring/50 } body { @apply bg-background text-foreground }`. New tokens (e.g. `--warning`) follow the same define-then-expose pattern (ui.shadcn.com/docs/theming).
- Manual install requires `shadcn`, class-variance-authority, clsx, tailwind-merge, lucide-react, tw-animate-css, a `cn()` util, and `components.json` with aliases (docs/installation/manual, /components-json). The `~/` alias in this repo must be set in `components.json`; the existing dark variant (`:where(.dark, .dark *)`) is already equivalent in effect.
- Chalk's `theme-values.md` defines no `--warning` or success/income token (checked in theme-values.md:11-33); income/expense/warning/deviation-badge roles in the table above have no Chalk token and need added semantic tokens.

## Code References

- `MyFinances/frontend/app/app.css:3,5-19,21-25` — dark variant, brand ramp, literal bg
- `MyFinances/frontend/app/root.tsx:13-24,28` — Inter link, hard-coded `dark`
- `MyFinances/frontend/app/components/CategorySpendDonut.tsx:20-24,44,145,181-186` — badge map, palette, slot fill, tooltip/legend hex
- `MyFinances/frontend/app/components/CategoryTrendChart.tsx:34,101-106,183,298-307` — palette, control/toggle classes, grid/axis/tooltip hex
- `MyFinances/frontend/app/routes/home.tsx:260-272,290-296,308,503-532,620-661,693-729,783-870` — hero, cards, forms, list, actions
- `MyFinances/frontend/app/components/AppHeader.tsx:19,30-70` — header surface, repeated ghost link string
- `MyFinances/frontend/tsconfig.json:12-14` — `~/*` alias
- `context/changes/ui-chalk-theme-dashboard/change.md:12-22`, `theme-values.md:3-39`

## Architecture Insights

- Brand usage (`brand-` line counts, grep): home 21, AppHeader 7, import 8, settings 6, categorize 3, login 4, register 4, Donut 1, Trend 2 — i.e. 56 lines across 9 files; the 5 non-dashboard routes (24 lines) are out of this change's scope and will keep the blue brand while tokens go global.
- Remapping `brand-*` to Chalk's orange (rather than deleting it) is one way to avoid a mixed palette on out-of-scope routes; this is an option, not an observed decision.
- `AppHeader` is the one dashboard component shared with other routes, so it is the main leak of restyling.

## Historical Context (from prior changes)

See "Prior decisions" above; main sources: `context/archive/2026-09-28-category-spend-donut-chart/`, `context/archive/2026-10-02-category-trend-line-chart/`, `context/archive/2026-10-02-homepage-visual-refresh/`, `context/archive/2026-10-02-homepage-redesign/`.

## Related Research

None under `context/changes/**/research.md` for this topic.

## Open Questions

Decisions for `/10x-plan` (not resolved here):
1. **Dependency**: add shadcn path (clsx, tailwind-merge, cva, lucide?, tw-animate-css, Radix only if a component needs it) — or hand-write tokens + 2-3 components in shadcn style without the CLI. change.md:16 requires confirming in plan.
2. **Chart palette**: keep 12 slots but derive from tokens (chart-1..5 + extra derived hues), or keep the hex palette and only tokenize tooltip/grid/axis. Must preserve ≥12 stable slots and cross-chart identity; contrast on the new background is unverified.
3. **`brand-*` fate**: remove, alias to primary (orange), or keep for out-of-scope routes; logo blue vs orange primary and focus-outline colour (`outline-brand-500` mandated by a past review).
4. **Extra semantic tokens** for income/expense, warning banner, deviation badge (Chalk has none); and the dark `destructive` override value (change.md:20 — value not yet chosen).
5. **Fonts**: Chalk specifies Outfit/JetBrains Mono/Merriweather vs current Inter link (root.tsx:22); whether to swap, add, or keep Inter.
6. **Gaps in this research**: `home.tsx` lines ~316-500/600-660 not read fully; Recharts `var()` support and default tick/cursor colours unverified; `package-lock.json` versions, `.github/workflows/ci.yml`, and `~/` vs relative import usage not checked; no screenshot/baseline taken (app not run).
