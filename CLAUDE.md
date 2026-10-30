# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**MyFinances** — a personal-finance MVP (single user) for importing bank statements (CSV: mBank, Erste Bank Polska; PDF: VeloBank, mBank, Erste), manually categorizing transactions, detecting duplicates and internal transfers, and visualizing spend and income per category (donut charts, trend line chart, period selector, deviation from the category's own historical average). Revolut import and per-category budgets were dropped from the MVP on 2026-10-02 (parked in the roadmap). Full requirements live in [context/foundation/prd.md](context/foundation/prd.md). Solo build, after-hours, no multi-tenant/sharing.

All MVP slices (F-01, S-01..S-04, S-06, S-08..S-12, S-14..S-16) are implemented and merged to `main`: auth, CSV/PDF import with dedup, manual entry, categorization queue with internal-transfer auto-flagging, custom categories, bank accounts, transaction history, charts. See [context/foundation/roadmap.md](context/foundation/roadmap.md) for per-slice status.

## Domain language

Name things in code, tests and plans with the terms from the glossary below, and avoid the "how NOT to call it" variants. Background and evidence: [context/domain/01-domain-distillation.md](context/domain/01-domain-distillation.md).

@context/domain/glossary.md

## Architecture

Split stack under `MyFinances/`, single-origin in production:

- **`MyFinances/backend`** — ASP.NET Core (net10.0) minimal-API project (`MyFinances.Api.csproj`). All endpoints are mounted under `/api` via `app.MapGroup("/api")` in [Program.cs](MyFinances/backend/Program.cs). In production the API also serves the built frontend as static files (`UseStaticFiles` + `MapFallbackToFile("index.html")`), so the browser only ever talks to one origin and no CORS is needed there.
- **`MyFinances/frontend`** — React Router v8 SPA (Vite, TypeScript, Tailwind v4). API calls go through [app/lib/api.ts](MyFinances/frontend/app/lib/api.ts)'s `apiFetch`, which targets `VITE_API_BASE_URL` or defaults to `/api`.
- **Dev-time wiring**: the frontend's Vite dev server proxies `/api/*` to `http://localhost:5007` (see [vite.config.ts](MyFinances/frontend/vite.config.ts)); the backend's `FrontendDev` CORS policy allows `http://localhost:5173` for the case where the frontend is hit directly instead of through the proxy.
- **Production build wiring**: `MyFinances.Api.csproj`'s `BuildAndCopyFrontend` target runs `npm ci && npm run build` in the frontend and copies `build/client/**` into the publish output's `wwwroot`, *after* the `Publish` target (not before — see the comment in the csproj for why). `dotnet build`/`dotnet run` never touch the frontend; local dev always uses the separate Vite server + proxy.

## Commands

Backend (from `MyFinances/backend`):
```bash
dotnet run                # http://localhost:5007, Swagger UI at /swagger
dotnet build
dotnet publish            # also builds+copies the frontend into wwwroot (see above)
dotnet tool restore       # once; installs dotnet-stryker from dotnet-tools.json
```

Mutation testing (from `MyFinances/backend/Tests`; incremental, mutates only code changed since the target branch — locally use `main`, the remote is named `Master` so `origin/main` exists only in CI):
```bash
dotnet stryker --since:main   # report in StrykerOutput/ (git-ignored)
```

Frontend (from `MyFinances/frontend`):
```bash
npm run dev          # Vite dev server at http://localhost:5173, proxies /api to the backend
npm run build        # production build -> build/client + build/server
npm run typecheck    # react-router typegen && tsc
```

Backend has an xUnit test suite (`MyFinances/backend/Tests`, run via `dotnet test` from `MyFinances/backend`). No frontend test framework exists yet.

## UI conventions

- The app is dark-only (Chalk theme, `<html class="dark">`); no light-mode wiring or theme toggle.
- Colour comes only from semantic tokens in [app/app.css](MyFinances/frontend/app/app.css) (`bg-background`, `text-muted-foreground`, `border-border`, `bg-primary`, `text-destructive`, `text-success`, `text-warning`, ...). No `gray-*`/`red-*`/`emerald-*`/`amber-*` utilities, `white/N`, or hex literals in any route or component.
- Use the shared components in `app/components/ui` (Button, Input, Card; native `<select>` uses `selectClassName`). Add new ones through the shadcn CLI, not by hand-copying.
- Chart series colours come from `chartColor(slot)` in `app/lib/chartPalette.ts` (`--chart-1..12`); keep it in sync with the tokens.
- All views are migrated to the Chalk theme; the `--color-brand-*` ramp no longer exists, so do not use `brand-*` utilities.
- Public-page (landing) animation is CSS plus the `useReveal` hook; no animation library. Animate `opacity`/`transform` only.
- The reveal starting style (hidden state) applies only under the `reveal-ready` ancestor, so content stays visible without JS and under reduced motion.
- Landing copy may only claim implemented features; check [context/foundation/roadmap.md](context/foundation/roadmap.md), not `prd.md`.
