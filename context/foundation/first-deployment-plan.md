---
project: my-finances
deployed_at: 2026-09-22
platform: Render
database: Neon Postgres
service_url: https://myfinances-api-q2a1.onrender.com
service_id: srv-dap2molg1s2s7396s8m0
---

## Context

Following the recommendation in [infrastructure.md](infrastructure.md) (Render for compute, Neon for Postgres — avoiding Render's own free Postgres, which expires after 30 days), this is the record of the project's first deployment: a single Docker web service on Render serving both the ASP.NET Core API and the built React SPA from one origin, per the architecture in [../../CLAUDE.md](../../CLAUDE.md).

## What was done

### Manual steps (human)

1. Created a Render account via GitHub OAuth and authorized access to `michLukaszewicz/MyFinances`.
2. Created a Neon account and a Postgres project (`empty-cell-65804777`), region close to Render's `oregon` region.
3. Copied the Neon **pooled** connection string (PgBouncer) from the project's Connection Details.
4. Installed the Render CLI (`winget install --id Render.CLI`) and logged in (`render login`, device-code browser flow).
5. Created the Blueprint service in the Render Dashboard (New → Blueprint → select repo/branch `main` → Render read `render.yaml` and created `myfinances-api`), since the installed CLI version (2.28.0) no longer ships a `blueprint launch` subcommand — only `blueprints validate`.
6. Pasted the Neon pooled connection string into the service's Environment tab as `ConnectionStrings__Default` (declared with `sync: false` in `render.yaml`, so never committed to the repo).

### Automated / code changes (agent)

- **`MyFinances/backend/Dockerfile`** (new): multi-stage build. Build stage uses `mcr.microsoft.com/dotnet/sdk:10.0`, installs Node 22 from NodeSource (Debian's default `nodejs` package is v18, too old for Vite 8 / React Router 8, which require Node ≥20.19), then runs `dotnet publish`, which triggers the existing `BuildAndCopyFrontend` MSBuild target (`npm ci && npm run build` in the frontend, copied into `wwwroot`). Final stage uses `mcr.microsoft.com/dotnet/aspnet:10.0`.
- **`MyFinances/backend/Program.cs`**: binds to `$PORT` via `builder.WebHost.UseUrls(...)` when the env var is present (Render injects `PORT` at runtime; ASP.NET Core has no built-in convention for it). Also registers `AppDbContext` via `AddDbContext<AppDbContext>(...).UseNpgsql(...)`, reading the connection string from configuration (`ConnectionStrings:Default` / `ConnectionStrings__Default` env var) — never hardcoded.
- **`MyFinances/backend/AppDbContext.cs`** (new): minimal placeholder `DbContext`, no domain models yet. Its only purpose for this deployment is to prove EF Core/Npgsql wiring works; the real data model is a separate, later task.
- **`MyFinances/backend/MyFinances.Api.csproj`**: added `Npgsql.EntityFrameworkCore.PostgreSQL` package reference.
- **`render.yaml`** (new, repo root): one Docker web service (`myfinances-api`, free plan), `dockerfilePath: MyFinances/backend/Dockerfile`, `dockerContext: .`, `ConnectionStrings__Default` declared as `sync: false`. No `databases:` block — Postgres is external (Neon).

### Notable issue found and fixed

The Docker build initially failed during the frontend's `npm run build` step with `SyntaxError: Unexpected token 'with'` — expected, caused by the too-old Node 18 from Debian's default `apt` package; fixed by installing Node 22 from NodeSource.

After fixing Node, the build still failed with `Prerender: Request failed for /: connect ECONNREFUSED 127.0.0.1:<port>`. Root cause: React Router's SPA-mode prerender step (`ssr: false` in `react-router.config.ts`) spins up a local server and fetches it over `localhost`; inside this Linux container, Node's default DNS resolution order returns the IPv6 loopback (`::1`) first, which the IPv4-only prerender server refuses. Fixed by setting `ENV NODE_OPTIONS=--dns-result-order=ipv4first` in the Dockerfile. Reproduced and confirmed outside of MSBuild too (plain `npm run build` in a `node:22-bookworm` container), so this is a general Docker/Node quirk for this project's frontend build, not an MSBuild-specific problem — worth remembering if the frontend build is ever containerized elsewhere (e.g. a future CI pipeline).

## Verification performed

- **Local Docker build**: `docker build -f MyFinances/backend/Dockerfile -t myfinances-api .` — succeeded after the two fixes above.
- **Local container run**: `docker run -e PORT=10000 -p 10000:10000 myfinances-api` — started cleanly (no connection string set), bound `0.0.0.0:10000`, `GET /` → 200 (SPA), `GET /api/weatherforecast` → 200 (JSON).
- **Push & Blueprint deploy**: pushed to `main` (commit `0de44fa`), created the Blueprint service in the Render Dashboard, first deploy went `live` in ~68s (`dep-dap2motg1s2s7396s96g`).
- **Production check**: `https://myfinances-api-q2a1.onrender.com/` → 200 (SPA), `/api/weatherforecast` → 200 (JSON).
- **Connection string + restart**: after setting `ConnectionStrings__Default` in the dashboard, restarted the service via `render restart srv-dap2molg1s2s7396s8m0 --confirm` — started cleanly with no errors (EF Core connections are lazy, so this confirms the app boots fine with the string present and correctly formatted enough for `UseNpgsql` to accept, not that a query has actually round-tripped to Neon yet — no endpoint touches `AppDbContext` yet).
- **Health check**: Render's default health check (`/`) is green.

## Not yet verified / follow-up

- No endpoint actually issues a query against Neon yet (`AppDbContext` is an empty placeholder) — first real migration/query is the way to fully confirm the Neon connection end-to-end. Worth doing as part of the first real feature/migration task.
- Cold-start behavior on the free web tier (~1 min after 15 min idle) has not yet been observed in practice — expect it on the first request after a gap.
- `MyFinances/frontend/Dockerfile` (leftover from the React Router template scaffold, assumes a standalone Node server) is unused by this Blueprint and was intentionally left in place; candidate for a separate cleanup PR.

## Manual-only actions going forward

Per the project's production-access posture ([`.claude/CLAUDE.md`](../../.claude/CLAUDE.md) infra lesson): deleting the Postgres database (Neon project), rotating the primary DB credential, and changing the Render plan/billing tier remain human-only actions, even though redeploying, restarting, and reading logs are safe for the agent to do unattended.
