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

## 10xDevs AI Toolkit - Module 2, Lesson 3

Review AI-generated code before merge with the **implementation review chain**:

```
/10x-implement -> /10x-impl-review -> triage -> (/10x-lesson | fix | skip | disagree)
```

`/10x-impl-review` is the lesson focus. Review is a quality gate, not an instruction to fix every finding.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Code review (lesson focus)** | |
| `/10x-impl-review <change-id>` | You have implemented code and want a structured review before merge. The skill checks plan adherence, scope discipline, safety and quality, architecture, pattern consistency, and success criteria, then presents findings for triage. |
| **Recurring lesson outcome** | |
| `/10x-lesson` | A finding reveals a recurring project rule or agent failure pattern. Record it in `context/foundation/lessons.md` instead of treating it as a one-off note. |

### Triage discipline

- Severity says how bad the finding is. Impact says how much the decision matters now.
- Valid outcomes: fix now, fix differently, skip, accept as risk, record as recurring rule (`/10x-lesson`), disagree.
- Fix critical findings. Do not burn hours on low-impact observations just because the agent found them.
- Conscious skipping of low-impact findings is a valid review outcome, not negligence.
- If you disagree with a finding, record why. Wrong agent reasoning is also signal.

### Review boundaries

- This lesson reviews implemented code. It does not create the plan, execute new phases, or teach CI review.
- Testing strategy and quality gates are introduced in Module 3.
- Do not use `/10x-contract` as a triage outcome in this lesson.

### Paths used by this lesson

- `context/changes/<change-id>/plan.md` - expected implementation contract
- `context/changes/<change-id>/reviews/` - review output
- `context/foundation/lessons.md` - recurring lessons

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
