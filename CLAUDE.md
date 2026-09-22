# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**MyFinances** — a personal-finance MVP (single user) for importing bank CSV statements (mBank, then Revolut, then Erste Bank Polska), manually categorizing transactions, detecting duplicates and internal transfers, and visualizing spend-per-category against both historical averages and optional per-category budgets. Full requirements live in [context/foundation/prd.md](context/foundation/prd.md). Solo build, after-hours, 3-week MVP timeline, no multi-tenant/sharing.

The repo is currently a freshly-scaffolded split stack — the backend is still the default WeatherForecast template and the frontend is still the default React Router welcome page. No domain code (auth, import, categorization, charts) has been implemented yet.

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
```

Frontend (from `MyFinances/frontend`):
```bash
npm run dev          # Vite dev server at http://localhost:5173, proxies /api to the backend
npm run build        # production build -> build/client + build/server
npm run typecheck    # react-router typegen && tsc
```

No test suite exists yet in either project.

## 10xDevs AI Toolkit context

This repo was scaffolded using the `/10x-*` skill chain (`10x-init` → `10x-shape` → `10x-prd` → `10x-tech-stack-selector` → `10x-bootstrapper`). Key hand-off artifacts:

- [context/foundation/prd.md](context/foundation/prd.md) — the locked PRD (source of truth for scope/requirements).
- [context/foundation/tech-stack.md](context/foundation/tech-stack.md) — backend hand-off (`starter_id: dotnet`).
- [context/foundation/tech-stack-frontend.md](context/foundation/tech-stack-frontend.md) — companion frontend hand-off (`starter_id: react-router`); this split exists because the bootstrapper schema carries one `starter_id` per file, and the project requested a polyglot .NET + React stack.
- [context/changes/bootstrap-verification/verification.md](context/changes/bootstrap-verification/verification.md) — audit trail from the scaffold run.
- `context/foundation/lessons.md` — recurring rules & pitfalls, consumed by later planning/review skills.

Skills must not write to `context/archive/` — it's immutable; a change targeting it should be redirected to `/10x-new`.

For how any `/10x-*` skill works mechanically (task routing, conflict policy, verification-log schema, etc.), see that skill's own definition under `.claude/skills/<skill-name>/`.
<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 2, Lesson 1

Move from sprint-zero setup to project orchestration with the **roadmap chain**:

```
(Module 1 foundation docs) -> /10x-roadmap -> backlog-ready roadmap items
```

`/10x-roadmap` is the lesson focus. `/10x-new` is intentionally introduced in Module 2, Lesson 2, when a selected roadmap item becomes an implementation change folder.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Roadmap (lesson focus)** | |
| `/10x-roadmap` | You have `context/foundation/prd.md` and a scaffolded project baseline, and you need a vertical-first MVP roadmap. The skill reads the PRD, inspects the code baseline, uses available foundation docs such as `tech-stack.md`, `infrastructure.md`, and `deploy-plan.md`, then writes `context/foundation/roadmap.md`. Use it BEFORE creating per-change folders or implementation plans. |
| **Re-run upstream if needed** | |
| `/10x-shape` / `/10x-prd` / `/10x-tech-stack-selector` / `/10x-bootstrapper` / `/10x-agents-md` / `/10x-infra-research` | Bundled from Module 1 so foundation contracts can be fixed before roadmap sequencing. If roadmap generation exposes a PRD gap, repair the PRD before pretending the backlog is ready. |

### How the chain hands off

- `/10x-roadmap` bridges product and implementation. It does not choose frameworks, design schemas, or write a per-change implementation plan.
- The output is `context/foundation/roadmap.md`: ordered milestones, vertical slices, bounded foundations, dependencies, unknowns, risk, and backlog handoff fields.
- Roadmap items should receive stable human-readable identifiers in backlog tools. The actual `context/changes/<change-id>/` folder is created in Lesson 2 with `/10x-new`.

### Roadmap boundaries

- Default to vertical slices: user-visible outcomes that cross UI, data, business logic, and integrations.
- Horizontal work is allowed only as a bounded enabler that names the downstream vertical milestone it unlocks.
- Avoid orphan horizontal work such as "build the whole database", "build all API endpoints", or "design the whole UI" before the first user-visible flow.
- Roadmap is not a calendar estimate. Do not invent dates, story points, or sprint velocity unless the user explicitly asks for a separate planning artifact.

### Foundation paths used by this lesson

- `context/foundation/prd.md` - input
- `context/foundation/tech-stack.md` - optional input
- `context/foundation/infrastructure.md` - optional input
- `context/deployment/deploy-plan.md` - optional input
- `context/foundation/roadmap.md` - output
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
