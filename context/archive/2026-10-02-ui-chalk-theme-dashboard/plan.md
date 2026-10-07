# Chalk Theme on the Dashboard Implementation Plan

## Overview

Introduce a semantic design-token layer (the tweakcn "Chalk" theme, dark only) and the shadcn component path (Button, Input, Card), then move the dashboard view — `home.tsx`, `AppHeader`, `CategorySpendDonut`, `CategoryTrendChart` — off literal colours onto those tokens and components. Global tokens ship with this change; the other routes are follow-up changes.

## Current State Analysis

- No token layer: `app/app.css` has one plain `@theme` with `--font-sans` (Inter) and `--color-brand-50..900`; no `:root`, `.dark` or `@theme inline`. Page background is literal `bg-gray-950` (`app.css:21-25`). Dark is forced by `<html className="dark">` (`root.tsx:28`) and a class-based `@custom-variant dark` already exists (`app.css:3`).
- Dashboard files hard-code colours: gray/red/emerald/amber/brand Tailwind utilities in `home.tsx` and `AppHeader.tsx`; hex literals in both charts. Repeated patterns with no shared component: solid/outline/ghost/neutral buttons (~20 uses), inputs/selects (11+ uses; the same class string is defined as `periodInputClass` in `home.tsx:347` and `controlClass` in `CategoryTrendChart.tsx:101`), cards (4), one badge map (`CategorySpendDonut.tsx:20-24`).
- Charts: two identical 12-hex `PALETTE` arrays (`CategorySpendDonut.tsx:44`, `CategoryTrendChart.tsx:34`) indexed by category position modulo length; hex tooltip/grid/axis/legend colours (`CategorySpendDonut.tsx:181-186`, `CategoryTrendChart.tsx:298-307`).
- shadcn is absent: no `components.json`, no `cn()`, no clsx/tailwind-merge/cva/lucide/radix/tw-animate-css. Alias is `~/*` → `./app/*` (`tsconfig.json:12-14`). Only quality gate is `npm run typecheck`; no frontend tests or lint. `node_modules` is not installed in this worktree.
- `AppHeader` is imported by 6 routes (home, import, categorize, settings, login, register); the charts only by `home.tsx`.

## Desired End State

The dashboard (logged-out landing and authenticated view) renders with the Chalk dark palette, Outfit type, 0.75rem radius and soft shadows. Colour in the four dashboard files comes only from semantic tokens (`bg-background`, `text-muted-foreground`, `border-border`, `bg-primary`, `ring-ring`, `text-destructive`, `text-success`, chart tokens) — no `gray-*`/`red-*`/`emerald-*`/`amber-*`/`brand-*` utilities and no hex literals in those files. Button, Input and Card exist under `app/components/ui/`. Both charts take series colours from one shared helper reading `--chart-1..12`. Verify: `npm run typecheck` and `npm run build` pass; a grep for the banned literals over the four files returns nothing; screenshots of the 7-state matrix look right; other routes still render. `CLAUDE.md` carries a UI rule.

### Key Discoveries:

- Chalk dark `destructive` equals `secondary` (`theme-values.md:30`); user decision is to override it (`change.md:20`).
- Chalk defines 5 chart colours, but categories need ≥12 stable distinct slots, identical in both charts (`archive/2026-09-28-category-spend-donut-chart/reviews/plan-review.md:57-65`; `archive/2026-10-02-category-trend-line-chart/reviews/impl-review.md:46-53`).
- Chalk has no success/warning token; income/expense (`home.tsx:797-800`), the duplicate-warning banner (`home.tsx:623`) and the deviation badge need them.
- Every interactive element needs a visible `focus-visible` outline (`archive/2026-10-02-homepage-visual-refresh/reviews/impl-review.md:40-41`); today ≥8 buttons in `home.tsx` have none.
- shadcn's token convention: `:root`/`.dark` values + `@theme inline { --color-x: var(--x) }`; new tokens follow the same define-then-expose pattern (Context7 `/websites/ui_shadcn`, theming + manual install pages).

## What We're NOT Doing

- No light mode wiring (light values stay in `theme-values.md` only); no theme toggle.
- No restyling of import, categorize, settings, login, register beyond what the shared `AppHeader` forces.
- No removal or recolouring of `--color-brand-*`; other routes keep using it. The logo is unchanged.
- No JetBrains Mono / Merriweather font loading.
- No Radix-based composite components (Select, Dialog, etc.); native `<select>` stays. Button may pull `@radix-ui/react-slot`/`radix-ui` only if the CLI's Button requires it.
- No new per-category colour field in the API or DB; slot-by-list-position stays.
- No frontend test framework, no backend changes.

## Implementation Approach

Foundation first (tokens + components + font) so nothing is migrated against an unstable base; then charts (self-contained, dashboard-only); then the header and page migration; then a verification gate and the rule that keeps later agents on the tokens. Dark values live in a single `:root` block (the app is permanently dark, and `color-scheme: dark` stays), so tokens resolve even before the `dark` class is read; shadcn's `dark:` variants still match because `<html class="dark">` stays.

## Critical Implementation Details

- **State sequencing**: run `shadcn init` and `add` first and review the diff to `app.css`/`package.json` before hand-editing; the CLI may overwrite `app.css`. Keep the existing keyframes, reduced-motion block, `brand-*` ramp and the existing `@custom-variant dark` line; discard the CLI's light `:root` values and any `.dark` block, replacing with Chalk dark values.
- **User experience spec**: with `brand-*` kept for other routes, the shared header on those routes will show the new primary/ring accents over blue-accent pages until their follow-up changes. This is accepted (decision recorded in the brief).

## Phase 1: Token foundation and shadcn path

### Overview

Install the shadcn path, define the Chalk dark token set, and swap the font, without touching any dashboard markup yet.

### Changes Required:

#### 1. shadcn setup

**File**: `MyFinances/frontend/components.json`, `MyFinances/frontend/package.json`, `MyFinances/frontend/app/lib/utils.ts`

**Intent**: Initialise shadcn for Vite/Tailwind v4 with the `~/` alias so components are added by CLI, with a `cn()` helper.

**Contract**: `components.json` aliases point at `~/components`, `~/components/ui`, `~/lib/utils`, css file `app/app.css`, empty tailwind config; `cn(...inputs)` exported from `~/lib/utils` using clsx + tailwind-merge. New deps: clsx, tailwind-merge, class-variance-authority, tw-animate-css, lucide-react (and the Radix slot package only if the generated Button imports it).

#### 2. Chalk tokens

**File**: `MyFinances/frontend/app/app.css`

**Intent**: Add the semantic token block from `theme-values.md` (dark section) and expose it through `@theme inline`; keep `brand-*`, keyframes and reduced-motion.

**Contract**: One `:root` block with the Chalk dark values for background, foreground, card, popover, primary, secondary, muted, accent, destructive, border, input, ring, chart-1..5, sidebar-*; `--radius: 0.75rem`; Chalk shadows. `destructive` overridden to a red distinct from `secondary` teal (target oklch ≈ 0.68 0.19 25 on the 0.18-lightness background; confirm ≥4.5:1 against `--background` for text use). New tokens `--success`/`--success-foreground` (green) and `--warning`/`--warning-foreground` (amber) and `--chart-6..12` (hues spaced between Chalk's five, lightness ≈0.65-0.75), each exposed as `--color-*` in `@theme inline`. `@layer base`: `* { border-border outline-ring/50 }`, `body { bg-background text-foreground }` replacing `@apply bg-gray-950`. `color-scheme: dark` remains.

#### 3. Font

**File**: `MyFinances/frontend/app/root.tsx`, `MyFinances/frontend/app/app.css`

**Intent**: Replace Inter with Outfit globally (decision: Outfit sans only).

**Contract**: Google Fonts `<link>` (`root.tsx:22`) requests Outfit with `display=swap`; `--font-sans` in `@theme` becomes Outfit then the system stack; Inter removed from both.

#### 4. Components

**File**: `MyFinances/frontend/app/components/ui/button.tsx`, `input.tsx`, `card.tsx`

**Intent**: Add Button, Input and Card via the CLI, then adjust variants so every button pattern in the dashboard maps to one.

**Contract**: Button variants cover solid (`default`), outline, ghost, destructive-ghost (row Delete) and size `sm`/`default`; every variant carries a `focus-visible:ring`/outline using the `ring` token. Input exports a shared class (or `selectClassName`) so native `<select>` matches Input. Card uses `bg-card text-card-foreground border`.

### Success Criteria:

#### Automated Verification:

- Dependencies install cleanly: `npm install` (from `MyFinances/frontend`)
- Type checking passes: `npm run typecheck`
- Production build passes: `npm run build`
- Tokens present: `app.css` defines `--background`, `--primary`, `--destructive`, `--success`, `--warning`, `--chart-1` through `--chart-12`, and `--color-*` mappings for each

#### Manual Verification:

- Dev server (`npm run dev`) shows the app in Outfit on the Chalk background without console errors
- Other routes (login, import, settings) still render legibly with `brand-*` accents intact
- Reviewed diff shows the CLI did not drop keyframes, `brand-*` ramp or reduced-motion rules

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Charts onto tokens

### Overview

Replace both hex palettes and chart chrome colours with token-driven values, keeping slot order and cross-chart identity.

### Changes Required:

#### 1. Shared palette helper

**File**: `MyFinances/frontend/app/lib/chartPalette.ts`

**Intent**: One place that maps a category slot to its colour, replacing the duplicated `PALETTE` arrays.

**Contract**: `chartColor(slot: number): string` returns `var(--chart-N)` with `N = (slot mod 12) + 1`; 12 slots, same modulo semantics as today. A note that it must stay in sync with the `--chart-1..12` tokens.

#### 2. Donut chart

**File**: `MyFinances/frontend/app/components/CategorySpendDonut.tsx`

**Intent**: Use `chartColor` for slice `fill` and legend dots; move tooltip/legend text and text/badge classes to tokens.

**Contract**: Remove `PALETTE` (`:44-57`); tooltip `contentStyle` uses `var(--popover)`/`var(--border)` and `var(--popover-foreground)`; legend text `var(--foreground)`; `DEVIATION_BADGE` classes use `destructive`/`success`/`muted` token tints; status text uses `text-muted-foreground` / `text-destructive`; list row hover uses `hover:bg-accent`.

#### 3. Trend chart

**File**: `MyFinances/frontend/app/components/CategoryTrendChart.tsx`

**Intent**: Same palette helper and tokens; fold `controlClass`/`toggleClass` onto Input/Button styling.

**Contract**: Remove `PALETTE` (`:34-47`); `colorFor` calls `chartColor`; `CartesianGrid` stroke `var(--border)`, axes `var(--muted-foreground)`, tooltip and legend as in the donut; the granularity/kind toggles use Button (`default` when active, `ghost` otherwise) keeping `aria-pressed`; date inputs use Input; error text `text-destructive`.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Production build passes: `npm run build`
- No hex literals or gray/red/green palette utilities remain in the two chart files: `grep -nE "#[0-9a-fA-F]{3,6}|(gray|red|green|emerald|amber)-[0-9]" MyFinances/frontend/app/components/CategorySpendDonut.tsx MyFinances/frontend/app/components/CategoryTrendChart.tsx` returns no lines

#### Manual Verification:

- Slice and line colours render (Recharts accepts `var(--chart-N)`) and a given category has the same colour in donut, legend dots and trend lines
- All 12 slots are visibly distinct and readable against the new background; no two adjacent slots are confusable
- Tooltip, axes and grid are legible; deviation badges read as above/below/in line

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Header and dashboard migration

### Overview

Rebuild `AppHeader` and both branches of `home.tsx` on Button, Input, Card and the tokens.

### Changes Required:

#### 1. App header

**File**: `MyFinances/frontend/app/components/AppHeader.tsx`

**Intent**: Replace the six-times-repeated ghost-link string and the Register button with Button variants; use tokens for surface and border.

**Contract**: Header `bg-background/70 border-border backdrop-blur-md`; nav links and Log out use Button `ghost`, Register uses `default`, via `asChild` for `Link`/`<a>` or an equivalent variant-class export; focus ring from the Button variants. Used by all 6 routes, so props and routes stay unchanged.

#### 2. Logged-out landing

**File**: `MyFinances/frontend/app/routes/home.tsx` (`!user` branch, `:254-321`)

**Intent**: Tokenise the hero: CTAs as Button, value-prop tiles as Card, glow blobs from primary/secondary.

**Contract**: Gradient `style` and blob classes reference `--primary`/`--secondary` (via `var(--color-primary)` etc.) instead of `--color-brand-*`; text `text-muted-foreground`/`text-foreground`; keep animations and `aria-hidden` blobs.

#### 3. Authenticated dashboard

**File**: `MyFinances/frontend/app/routes/home.tsx` (`transactionForm` `:501-665`, main view `:667-880`)

**Intent**: Move form, period selector, transaction list, banners and actions to shared components and tokens; unify focus handling.

**Contract**: Form and list rows use Card; text/date/number inputs and the checkbox use Input styling, selects use the shared select class, `periodInputClass` is deleted in favour of it; buttons map to Button variants (solid for submit/CTA, outline for Cancel/Close/Add/Load more, ghost for Edit/Clear/Cancel, destructive-ghost for Delete/Confirm delete); amount `text-success` / `text-destructive`; duplicate banner uses `warning` tokens; errors use `text-destructive` (one tone, replacing the 600/400 split); every button and input gets the shared focus-visible ring. No behaviour, state or fetch logic changes.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Production build passes: `npm run build`
- No literal colours remain in the dashboard files: `grep -nE "#[0-9a-fA-F]{3,6}|(gray|red|green|emerald|amber|brand)-[0-9]|white/[0-9]" MyFinances/frontend/app/routes/home.tsx MyFinances/frontend/app/components/AppHeader.tsx MyFinances/frontend/app/components/CategorySpendDonut.tsx MyFinances/frontend/app/components/CategoryTrendChart.tsx` returns no lines

#### Manual Verification:

- Logged-out landing and the dashboard look coherent in the Chalk palette
- Add, edit, delete, duplicate-warning ("Save anyway") and load-more flows still work as before
- Keyboard Tab visits every button, input and link with a visible focus ring
- Income amounts and expenses are visually distinct from neutral accents (destructive ≠ secondary teal)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: Verification gate and UI rule

### Overview

Prove the view in its states, confirm no collateral damage, and leave a rule so later agents keep using the tokens.

### Changes Required:

#### 1. UI rule

**File**: `CLAUDE.md` (repo root), new "UI conventions" section outside any 10x-cli block

**Intent**: Tell later agents to use semantic tokens and the shared components in `app/components/ui`, and what is not migrated yet.

**Contract**: Short rules: colour only from tokens (no gray-*/hex in migrated views); add components via the shadcn CLI; chart colours via `chartColor`; `brand-*` remains only on not-yet-migrated routes (import, categorize, settings, login, register); app is dark-only.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npm run typecheck`
- Production build passes: `npm run build`
- Backend untouched: `git diff --stat main -- MyFinances/backend` shows no changes

#### Manual Verification:

- 7-state matrix on the dashboard captured in screenshots: default, hover, focus, disabled (submit while saving / load more), loading, empty (no transactions, no chart data), error (invalid period, load failure)
- Chart text, axes and the muted-foreground text meet readable contrast on the Chalk background
- Login, register, import, categorize and settings render without layout breakage and the shared header works on each
- Mobile-width viewport (375px) renders the dashboard without horizontal overflow

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- None: the frontend has no test framework and this change adds no logic beyond `chartColor` (a modulo mapping). The backend suite is untouched.

### Integration Tests:

- None added; `npm run typecheck`, `npm run build` and the literal-colour grep are the automated gates.

### Manual Testing Steps:

1. Run `npm run dev` with the backend and sign in with the local test account; load the dashboard.
2. Walk the add/edit/delete/duplicate/filter-by-slice flows and the period and trend controls.
3. Tab through the page for focus visibility; resize to 375px.
4. Visit every other route for regressions.

## Performance Considerations

One font family instead of Inter (no net increase); shadcn components are source-copied, so bundle impact is limited to the added small dependencies (clsx, tailwind-merge, cva, lucide-react used only for imported icons).

## Migration Notes

No data migration. Global effects of this change on out-of-scope routes: new font (Outfit), new background/foreground tokens, new `AppHeader` styling. `brand-*` utilities keep working.

## References

- Related research: `context/changes/ui-chalk-theme-dashboard/research.md`
- Token source: `context/changes/ui-chalk-theme-dashboard/theme-values.md`
- Chart colour constraints: `context/archive/2026-09-28-category-spend-donut-chart/reviews/plan-review.md:57-65`, `context/archive/2026-10-02-category-trend-line-chart/reviews/impl-review.md:46-53`
- Focus convention: `context/archive/2026-10-02-homepage-visual-refresh/reviews/impl-review.md:40-41`
- Current duplicated patterns: `MyFinances/frontend/app/routes/home.tsx:347`, `MyFinances/frontend/app/components/CategoryTrendChart.tsx:101`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Token foundation and shadcn path

#### Automated

- [x] 1.1 Dependencies install cleanly: `npm install` (from `MyFinances/frontend`) — 8e048ba
- [x] 1.2 Type checking passes: `npm run typecheck` — 8e048ba
- [x] 1.3 Production build passes: `npm run build` — 8e048ba
- [x] 1.4 Tokens present in `app.css`: background, primary, destructive, success, warning, chart-1 through chart-12 and their `--color-*` mappings — 8e048ba

#### Manual

- [x] 1.5 Dev server shows the app in Outfit on the Chalk background without console errors
- [x] 1.6 Other routes (login, import, settings) still render legibly with `brand-*` accents intact
- [x] 1.7 Reviewed diff shows the CLI did not drop keyframes, `brand-*` ramp or reduced-motion rules

### Phase 2: Charts onto tokens

#### Automated

- [x] 2.1 Type checking passes: `npm run typecheck` — 78a6fed
- [x] 2.2 Production build passes: `npm run build` — 78a6fed
- [x] 2.3 No hex literals or gray/red/green palette utilities remain in the two chart files (grep returns no lines) — 78a6fed

#### Manual

- [x] 2.4 Slice and line colours render via `var(--chart-N)` and a category keeps the same colour in donut, legend dots and trend lines
- [x] 2.5 All 12 slots are visibly distinct and readable against the new background
- [x] 2.6 Tooltip, axes and grid are legible; deviation badges read as above/below/in line

### Phase 3: Header and dashboard migration

#### Automated

- [x] 3.1 Type checking passes: `npm run typecheck` — 97f8939
- [x] 3.2 Production build passes: `npm run build` — 97f8939
- [x] 3.3 No literal colours remain in the four dashboard files (grep returns no lines) — 97f8939

#### Manual

- [x] 3.4 Logged-out landing and the dashboard look coherent in the Chalk palette
- [x] 3.5 Add, edit, delete, duplicate-warning and load-more flows still work as before
- [x] 3.6 Keyboard Tab visits every button, input and link with a visible focus ring
- [x] 3.7 Income and expenses are visually distinct from neutral accents (destructive differs from secondary teal)

### Phase 4: Verification gate and UI rule

#### Automated

- [x] 4.1 Type checking passes: `npm run typecheck` — 7ced6ba
- [x] 4.2 Production build passes: `npm run build` — 7ced6ba
- [x] 4.3 Backend untouched: `git diff --stat main -- MyFinances/backend` shows no changes — 7ced6ba

#### Manual

- [x] 4.4 7-state matrix captured in screenshots: default, hover, focus, disabled, loading, empty, error
- [x] 4.5 Chart text, axes and muted-foreground text meet readable contrast on the Chalk background
- [x] 4.6 Login, register, import, categorize and settings render without breakage and the shared header works on each
- [x] 4.7 Mobile-width viewport (375px) renders the dashboard without horizontal overflow
