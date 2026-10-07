# Chalk Standard on the Remaining Views Implementation Plan

## Overview

Apply the Chalk standard (semantic tokens, Button/Input/Card from `app/components/ui`, visible focus ring, dark-only) to the five routes not yet migrated by `ui-chalk-theme-dashboard` — login, register, settings, categorize, import — and replace the blue favicon with an orange one matching the recoloured logo. Then retire `--color-brand-*` and update the CLAUDE.md UI rule.

## Current State Analysis

- Literal colours remain only in `app/routes/{login,register,settings,categorize,import}.tsx` (9, 9, 24, 19, 41 lines) and the `brand-*` ramp in `app/app.css`. Dashboard, `AppHeader`, charts and landing are already on tokens.
- Shared building blocks already exist: `Button` (default/outline/ghost/destructive-ghost, sizes default/sm, `asChild`), `Input` + `inputClassName`/`selectClassName`, `Card`, `cn()`; tokens `--success`/`--warning`/`--destructive`/`--muted`/`--accent`; reference implementation is `app/routes/home.tsx` (authenticated branch).
- `public/favicon.ico` is a 16x16 red icon unrelated to the logo; the logo PNG (`app/assets/myfinances-logo.png`) is now `#E78A53` (= `--primary`).
- No frontend tests or lint; gates are `npm run typecheck`, `npm run build` and greps.

## Desired End State

All routes render in the Chalk palette with no `gray-*`/`red-*`/`green-*`/`emerald-*`/`amber-*`/`brand-*`/hex literals under `app/routes`, `app/components` (excluding `ui/` internals if shadcn-generated) and no `--color-brand-*` left in `app.css` if unused. Favicon is orange. CLAUDE.md UI rule states all views are migrated. Verify: typecheck, build, literal-colour grep over `app/routes app/components` returns nothing, backend untouched; screenshots of each route.

### Key Discoveries

- Mapping used on the dashboard: errors `text-destructive`; success `text-success`; warning banners `warning` tokens; cards `Card`; submit/CTA `Button default`; secondary `outline`; row actions `ghost`/`destructive-ghost`; text `text-foreground`/`text-muted-foreground`; borders `border-border`.
- Behaviour, state and fetch logic must not change; only classes and element wrappers.

## What We're NOT Doing

- No light mode, no new components beyond what the CLI offers if truly required, no layout redesign, no backend changes, no changes to `home.tsx`, `AppHeader`, charts or landing.

## Phase 1: Favicon and auth routes

### Changes Required

- `MyFinances/frontend/public/favicon.ico`: replace with an orange icon derived from the logo's spark mark in `#E78A53` on transparent background, multi-size ICO (16, 32, 48); ensure `root.tsx` links it (add a `<link rel="icon">` only if missing).
- `app/routes/login.tsx`, `app/routes/register.tsx`: Card form, Input fields, Button submit, `text-destructive` errors, muted links with focus ring.

### Success Criteria

#### Automated Verification
- `npm run typecheck`, `npm run build` (from `MyFinances/frontend`)
- `grep -nE "#[0-9a-fA-F]{3,6}|(gray|red|green|emerald|amber|brand)-[0-9]|white/[0-9]" MyFinances/frontend/app/routes/login.tsx MyFinances/frontend/app/routes/register.tsx` returns no lines

#### Manual Verification
- Favicon shows orange in the browser tab
- Login and register look coherent with the dashboard; login/register flows still work

## Phase 2: Settings and categorize

### Changes Required

- `app/routes/settings.tsx`, `app/routes/categorize.tsx`: move to Card/Input/Button/select class and tokens; keep all handlers and state untouched; destructive actions use `destructive-ghost`.

### Success Criteria

#### Automated Verification
- `npm run typecheck`, `npm run build`
- literal-colour grep (as in Phase 1) over `settings.tsx` and `categorize.tsx` returns no lines

#### Manual Verification
- Settings (bank accounts) and categorize queue look coherent and all actions work (add/edit/delete account, categorize, flag transfer)

## Phase 3: Import and brand retirement

### Changes Required

- `app/routes/import.tsx`: same migration (upload form, results, duplicate review side-by-side, banners with `warning`/`destructive`/`success` tokens).
- `app/app.css`: remove the `--color-brand-*` ramp if no usages remain (`grep -rn "brand-" MyFinances/frontend/app`); keep keyframes and reduced-motion block.
- `CLAUDE.md` UI conventions: replace the "brand-* stays on not-yet-migrated routes" bullet with "all views are migrated; no `brand-*`", and widen the no-literals rule to every route/component.

### Success Criteria

#### Automated Verification
- `npm run typecheck`, `npm run build`
- `grep -rnE "#[0-9a-fA-F]{3,6}|(gray|red|green|emerald|amber|brand)-[0-9]|white/[0-9]" MyFinances/frontend/app/routes MyFinances/frontend/app/components --include=*.tsx` returns no lines (excluding `components/ui` if shadcn-generated code legitimately matches; document any exception)
- `grep -rn "brand-" MyFinances/frontend/app` returns no lines
- `git diff --stat main -- MyFinances/backend` shows no changes

#### Manual Verification
- Import flow (upload, duplicate review, confirm) works and looks coherent
- Every route at 375px has no horizontal overflow; Tab shows visible focus rings

## References

- Standard: `context/changes/ui-chalk-theme-dashboard/plan.md`, CLAUDE.md "UI conventions"
- Reference implementation: `MyFinances/frontend/app/routes/home.tsx`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Favicon and auth routes

#### Automated

- [x] 1.1 Type checking passes: `npm run typecheck` — 3c89f87
- [x] 1.2 Production build passes: `npm run build` — 3c89f87
- [x] 1.3 No literal colours in login.tsx and register.tsx (grep returns no lines) — 3c89f87

#### Manual

- [ ] 1.4 Favicon shows orange in the browser tab
- [ ] 1.5 Login and register look coherent with the dashboard and their flows still work

### Phase 2: Settings and categorize

#### Automated

- [x] 2.1 Type checking passes: `npm run typecheck` — d478027
- [x] 2.2 Production build passes: `npm run build` — d478027
- [x] 2.3 No literal colours in settings.tsx and categorize.tsx (grep returns no lines) — d478027

#### Manual

- [ ] 2.4 Settings and categorize look coherent and all actions work

### Phase 3: Import and brand retirement

#### Automated

- [ ] 3.1 Type checking passes: `npm run typecheck`
- [ ] 3.2 Production build passes: `npm run build`
- [ ] 3.3 No literal colours under app/routes and app/components (grep returns no lines)
- [ ] 3.4 No `brand-` usages or tokens remain in app/
- [ ] 3.5 Backend untouched: `git diff --stat main -- MyFinances/backend` shows no changes

#### Manual

- [ ] 3.6 Import flow works and looks coherent
- [ ] 3.7 Every route at 375px has no horizontal overflow; Tab shows visible focus rings
