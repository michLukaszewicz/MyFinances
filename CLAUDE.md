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

## 10xDevs AI Toolkit - Module 2, Lesson 2

Turn one roadmap item into the first implementation cycle with the **change planning chain**:

```
/10x-roadmap -> /10x-new -> /10x-plan -> /10x-plan-review -> /10x-implement
```

`/10x-new`, `/10x-plan`, `/10x-plan-review`, and `/10x-implement` are the lesson focus. `/10x-frame` and `/10x-research` are not required rituals here; they are escalation paths introduced in the next lesson.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Change setup (lesson focus)** | |
| `/10x-new <change-id>` | You selected a roadmap item and need a stable change folder. Creates `context/changes/<change-id>/change.md` so planning, implementation, progress, commits, and later review all share one identity. Use AFTER roadmap selection, BEFORE `/10x-plan`. |
| **Planning (lesson focus)** | |
| `/10x-plan <change-id>` | You have a change folder and need a reviewable implementation plan. Reads roadmap context, foundation docs, codebase evidence, and any existing change notes; writes `plan.md` and `plan-brief.md` with phases, file contracts, success criteria, and `## Progress`. |
| **Plan readiness (lesson focus)** | |
| `/10x-plan-review <change-id>` | You have `plan.md` and need a light pre-code readiness check. Use it to catch missing end state, weak contracts, malformed progress, scope drift, or blind spots before code changes begin. |
| **Implementation (lesson focus)** | |
| `/10x-implement <change-id> phase <n>` | You have an approved plan and want to execute one phase with verification, manual gate, commit ritual, and SHA write-back to `## Progress`. |
| **Lifecycle closure** | |
| `/10x-archive <change-id>` | A change is merged or intentionally closed. Move it out of active `context/changes/` into archive state. |

### How the chain hands off

- `/10x-new` creates the durable change identity.
- `/10x-plan` turns that identity into an implementation contract.
- `/10x-plan-review` checks the plan before the agent mutates code.
- `/10x-implement` executes one planned phase, verifies, asks for manual confirmation when needed, commits, and records progress.

### Lesson boundaries

- Plan is the default router after roadmap selection. Start with `/10x-plan` unless the problem is unclear or external evidence is blocking.
- Do not run `/10x-frame + /10x-research` as ceremony for every change.
- Do not turn this lesson into a full end-to-end product build. A checkpoint with a planned and partially or fully implemented stream is valid.
- Code review of the implemented diff belongs to Lesson 3 via `/10x-impl-review`.
- Lifecycle closure via `/10x-archive` after a change is merged or intentionally closed.

### Paths used by this lesson

- `context/foundation/roadmap.md` - upstream roadmap
- `context/changes/<change-id>/change.md` - change identity
- `context/changes/<change-id>/plan.md` - implementation contract
- `context/changes/<change-id>/plan-brief.md` - compressed handoff
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
