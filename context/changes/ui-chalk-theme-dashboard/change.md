---
change_id: ui-chalk-theme-dashboard
title: Apply Chalk theme tokens to the dashboard view
status: implementing
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Run via `/10x-ui`. **One view:** dashboard (`MyFinances/frontend/app/routes/home.tsx` plus `AppHeader`, `CategorySpendDonut`, `CategoryTrendChart`). Global tokens ship with it; other views (import, categorize, settings, login, register) are follow-up changes.

**Token source:** tweakcn theme "Chalk" by Anton Grouchtchak (registry name `qrafthive`, https://tweakcn.com/r/themes/cmjgilzlg000404ju2wgs7uj9). Raw values deposited in [theme-values.md](theme-values.md).

**Contract variant:** no design system + named preset. Adds a semantic token block (`:root`/`.dark` + `@theme inline`) and only the 2–3 components this view needs, added through shadcn's path; no second palette, no whole-library import. Adding shadcn/ui is a dependency decision — confirm in plan.

**Decisions (user, 2026-10-02):**
1. Dark mode only. Light values are kept in `theme-values.md` but not wired; app keeps `color-scheme: dark`.
2. Theme's dark `destructive` equals `secondary` (teal) — override `destructive` in dark so errors/expenses stay distinguishable from neutral accents.

**Pre-audit (literal-colour lines):** home.tsx 50, AppHeader 8, CategorySpendDonut 27, CategoryTrendChart 28 (113 total). Existing `brand-*` palette in `app.css` is not a semantic token set. No UI rules in CLAUDE.md yet — add after the change (outside any 10x-cli block).
