# Chalk Theme on the Dashboard — Plan Brief

> Full plan: `context/changes/ui-chalk-theme-dashboard/plan.md`
> Research: `context/changes/ui-chalk-theme-dashboard/research.md`

## What & Why

Give MyFinances a real design-token layer using the tweakcn "Chalk" theme (dark only), and move the dashboard onto it. Today colours are 100+ literal gray/red/brand/hex values with no shared Button, Input or Card, so every view repeats them; this change fixes that for one view and leaves the tokens and components for the rest.

## Starting Point

`app.css` has only a blue `brand-*` ramp and a literal `bg-gray-950`; `<html class="dark">` is hard-coded. The dashboard files repeat button/input/card class strings, and both charts carry the same 12-hex palette. shadcn, `cn()` and `components.json` do not exist; the alias is `~/`.

## Desired End State

The dashboard (landing and logged-in view) shows the Chalk dark palette in Outfit, with colour coming only from semantic tokens and Button/Input/Card from `app/components/ui`. Charts read their series colours from `--chart-1..12`. A UI rule in `CLAUDE.md` keeps later agents on the tokens.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Colour modes | Dark only; light values kept in `theme-values.md`, not wired | App is already dark-only | change.md (user) |
| Dark `destructive` | Override to a red distinct from teal `secondary` | Chalk's value equals `secondary` | change.md (user) |
| shadcn | CLI init (`components.json`, `~/` alias) + Button, Input, Card | The path change.md names; later views add components with one command | Plan (user) |
| Chart colours | `--chart-1..5` from Chalk + `--chart-6..12` added; one `chartColor()` helper | Keeps 12 stable slots and cross-chart identity | Plan (user) |
| `brand-*` | Left as is for out-of-scope routes | No silent restyle of five other views | Plan (user) |
| Fonts | Outfit sans only, replacing Inter | One font load; mono/serif unused | Plan (user) |
| Extra tokens | `--success`, `--warning` added | Chalk has none; income, banner, badge need them | Research |
| Token location | Single `:root` with dark values | App is permanently dark | Plan |

## Scope

**In scope:** `home.tsx`, `AppHeader`, `CategorySpendDonut`, `CategoryTrendChart`; global tokens, font, shadcn setup, Button/Input/Card; `CLAUDE.md` UI rule.

**Out of scope:** import, categorize, settings, login, register views; light mode and toggle; removing `brand-*`; extra fonts; backend; Radix composite components.

## Architecture / Approach

Tokens in `app.css` (`:root` + `@theme inline`) feed Tailwind utilities and shadcn components; charts reference the same tokens through `var(--chart-N)`. Order: foundation, charts, header + page migration, verification gate.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Token foundation and shadcn path | Tokens, Outfit, components.json, Button/Input/Card | CLI rewrites `app.css` and drops existing rules |
| 2. Charts onto tokens | `chartColor()`, tokenised tooltip/grid/axes/badge | Recharts and `var()` fills; contrast of 7 new hues |
| 3. Header and dashboard migration | Header + both home branches on components/tokens | Shared header changes all 6 routes; large diff in `home.tsx` |
| 4. Verification gate and UI rule | State-matrix screenshots, regression check, `CLAUDE.md` rule | Contrast or mixed-palette issues on other routes |

**Prerequisites:** `npm install` in `MyFinances/frontend` (no `node_modules` yet); network access for the shadcn CLI and Google Fonts; backend running locally for manual checks.
**Estimated effort:** ~3-4 sessions across 4 phases.

## Open Risks & Assumptions

- Other routes will look mixed (new font and header accents over blue `brand-*` pages) until their follow-up changes land.
- The seven extra chart hues and the destructive red are chosen during implementation and need a contrast check.
- Recharts accepting `var(--chart-N)` is assumed from shadcn's chart docs and is unverified here.
- No frontend tests exist; verification is typecheck, build, a literal-colour grep and manual checks.

## Success Criteria (Summary)

- The dashboard looks like Chalk (dark, Outfit, orange primary) and the four files have no literal colours.
- Add, edit, delete, filtering and the charts behave as before, with visible keyboard focus everywhere.
- Other routes still work, and later views can reuse the tokens and components.
