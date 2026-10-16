<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Chalk Theme on the Dashboard Implementation Plan

- **Plan**: context/archive/2026-10-02-ui-chalk-theme-dashboard/plan.md
- **Scope**: Full plan
- **Reviewed phases**: 1, 2, 3, 4
- **Date**: 2026-10-16
- **Review type**: Retrospective (backfilled after archive on 2026-10-16)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 3 observations

Method: code and git history only (commits aff22cc, 8cafb5c, aed91e4, c7fc757 / 218ff8c, range 377b1f6..c7fc757). No build, test or npm commands were run. Code at HEAD has evolved (ui-chalk-remaining-views retired `brand-*`); judged against the landing commits.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Drift summary (evidence)

- Phase 1: `components.json` (aliases `~/components`, `~/components/ui`, `~/lib/utils`, css `app/app.css`, empty tailwind config), `lib/utils.ts` (`cn` = clsx + twMerge), `root.tsx:22` Outfit link, `app.css` `--font-sans` Outfit. `:root` holds Chalk dark values, `--radius: 0.75rem`, `--destructive: oklch(0.68 0.19 25)` (distinct from teal secondary), `--success`, `--warning`, `--chart-1..12`, all exposed in `@theme inline`; `@layer base` uses `border-border outline-ring/50` and `bg-background text-foreground`; `color-scheme: dark`, `@custom-variant dark`, `brand-*` ramp kept. MATCH.
- Phase 2: `lib/chartPalette.ts` `chartColor(slot)` returns `var(--chart-${(slot % 12) + 1})`; both charts use it; tooltip/grid/axes/legend use `var(--popover|border|muted-foreground|foreground)` (CategorySpendDonut.tsx:166-171, CategoryTrendChart.tsx:280-289); toggles keep `aria-pressed` (CategoryTrendChart.tsx:206, 220). MATCH.
- Phase 3: `AppHeader` uses Button `ghost`/default via `asChild`, `bg-background/70 border-border backdrop-blur-md`, props and routes unchanged. `home.tsx` uses Card/Input/Button/`selectClassName`, `periodInputClass` removed, amounts `text-success`/`text-destructive` (home.tsx:790), duplicate banner on `warning` tokens (:625), errors `text-destructive`, landing gradient on `--color-primary`/`--color-secondary` (:267). No raw `<button>`/`<input>` left in home.tsx. MATCH.
- Phase 4: `CLAUDE.md` gains a "UI conventions" section covering all five planned rules, outside any 10x-cli block. `git diff --stat 377b1f6..c7fc757 -- MyFinances/backend` is empty. MATCH.
- Literal-colour grep re-done on the four dashboard files at c7fc757 (hex, gray/red/green/emerald/amber/brand-N, white/N): no hits.
- Scope: "Not doing" list respected (no light wiring, no toggle, `brand-*` kept, no new fonts, only Radix Slot added, native `<select>` kept, no backend or test-framework changes).

## Success Criteria

Retrospective review: automated checks (typecheck, build) were not re-run. Progress items 1.1-1.4, 2.1-2.3, 3.1-3.3, 4.1-4.3 carry commit SHAs that resolve in the repo; the grep-type criteria (1.4, 2.3, 3.3, 4.3) were independently confirmed from the diff. Manual items 1.5-1.7, 2.4-2.6, 3.4-3.7, 4.4-4.7 are all `[x]`; most are visual or behavioural and cannot be proven from the diff (see F1).

## Findings

### F1 — Phase 4 screenshot matrix has no artefact in the repo

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress item 4.4
- **Detail**: Item 4.4 ("7-state matrix captured in screenshots") is checked, but no image file appears in the change folder or in commits 377b1f6..c7fc757. Items 4.5-4.7 and the 375px check are likewise visual-only with no recorded evidence. Possible rubber-stamping, or evidence kept outside the repo.
- **Fix**: If screenshots exist, note where they are; otherwise treat the visual manual checks as unverified.
- **Decision**: PENDING

### F2 — lucide-react added but not imported anywhere

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: MyFinances/frontend/package.json (`lucide-react`)
- **Detail**: The plan listed lucide-react as a dependency (Performance note: "used only for imported icons"), but `git grep lucide-react c7fc757 -- MyFinances/frontend/app` finds no import. It is an unused dependency from the shadcn init.
- **Fix**: Remove the dependency, or keep it knowingly for future shadcn components.
- **Decision**: PENDING

### F3 — Checkbox rendered through the full-width Input component

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/frontend/app/routes/home.tsx:612-617
- **Detail**: The internal-transfer checkbox uses `<Input type="checkbox">` with overrides (`h-4 w-4 shrink-0 p-0 accent-primary`) to cancel the text-input styling (`h-9 w-full px-3`). It works and the plan said "the checkbox use Input styling", but it relies on twMerge to undo base classes.
- **Fix**: Optional: a dedicated Checkbox component later, or leave as is.
- **Decision**: PENDING
