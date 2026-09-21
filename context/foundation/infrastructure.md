---
project: my-finances
researched_at: 2026-09-21
recommended_platform: Render
runner_up: Railway
context_type: mvp
tech_stack:
  language: C# / TypeScript
  framework: ASP.NET Core (net10.0) minimal API + React Router v8 SPA (Vite)
  runtime: .NET 10 (Docker container) + Node/Vite build for the SPA, served as static files from the .NET app's wwwroot
---

## Recommendation

**Deploy on Render, with the database on Neon (not Render's own managed Postgres).**

Render was picked over the initial leader (Railway) after the anti-bias cross-check and the developer's explicit swap decision. Both platforms require the same Dockerfile-based path since neither has a native .NET buildpack, and both score identically on CLI-first, managed/serverless, agent-readable docs, and MCP integration. Render's more mature CLI (rollback reachable via API/CLI, not dashboard-only like Railway) and its official MCP server (GA since August 2025) tipped the decision.

Render's own Postgres was the default pairing, but its free tier hard-expires after 30 days — a direct risk for a cost-minimizing solo MVP that would otherwise need to start paying for a database from day one just to avoid data loss. Neon's Postgres has a genuine, non-expiring free tier (serverless, scale-to-zero) and was already acceptable per the developer's own interview answer that external providers are fine. The trade-off, accepted explicitly: one extra vendor/connection string to operate instead of a single Render-only stack.

## Platform Comparison

Hard filter applied first: Cloudflare Workers/Pages, Vercel, and Netlify were all **dropped** — none can run ASP.NET Core server-side code (JS/TS/Python/Rust/WASM runtimes only), and splitting the SPA onto one of them while the .NET API runs elsewhere would break this project's single-origin architecture (`UseStaticFiles` + `MapFallbackToFile`, explicitly chosen to avoid CORS).

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Total |
|---|---|---|---|---|---|---|
| Render | Pass | Pass | Pass | Pass | Pass | 5 Pass |
| Railway | Partial | Pass | Pass | Pass | Pass | 4 Pass / 1 Partial |
| Fly.io | Pass | Pass | Pass | Pass | Partial | 4 Pass / 1 Partial |
| Azure App Service | Pass | Pass | Pass | Partial | Partial | 3 Pass / 2 Partial |
| Cloudflare Workers/Pages | — | — | — | — | — | Dropped (runtime mismatch) |
| Vercel | — | — | — | — | — | Dropped (runtime mismatch) |
| Netlify | — | — | — | — | — | Dropped (runtime mismatch) |

Notes per platform:
- **Render**: CLI covers deploy, restart, logs, DB sessions, `--output json|yaml`; rollback via API/CLI (`rollback-deploy`) though autodeploy can override it if a new commit lands mid-rollback. Docs published at `render.com/llms.txt`. Official MCP server (`mcp.render.com`), GA since August 2025, 20+ tools. Free web tier spins down after 15 min idle (~1 min cold start). Render's own free Postgres expires after 30 days — this project sidesteps that by using Neon (external, genuinely free) for the database instead of Render's managed Postgres; see Recommendation.
- **Railway**: Rollback to an *arbitrary older* deployment is dashboard-only, not CLI-scriptable — the one gap against full CLI-first. Docs at `docs.railway.com/llms.txt`. Official MCP server, but recently launched with thinner maturity signal. Co-located Postgres with real backup/PITR. Usage-based billing on the $5 Hobby plan can exceed the credit without a default spending alert.
- **Fly.io**: Strongest raw-compute cost (~$2–5/month for a small always-on machine) and mature `flyctl`/docs, but Fly's own managed Postgres is deprecated/expensive (~$38/month), pushing this project to an external DB provider (e.g. Neon, Supabase). No first-party MCP server — community wrappers only.
- **Azure App Service**: Only platform with a genuinely native ASP.NET Core runtime (no Dockerfile needed), and Microsoft Learn docs are mirrored as GitHub markdown. But realistic MVP cost (B1 tier + Azure Database for PostgreSQL Flexible Server) lands around $25/month — 2–5x the other candidates — which weighs against the developer's stated cost-minimization priority. Deployment-slot rollback needs Standard tier, above Basic.

### Shortlisted Platforms

#### 1. Render (Recommended)

Wins on the combination that matters most for this project: cheapest reasonable floor among fully agent-operable platforms, a CLI mature enough for the full deploy/logs/rollback loop, and an official, actively-used MCP server. The free-Postgres-expiry gotcha is real but easily mitigated by moving straight to a paid Postgres plan once real data exists (see Risk Register).

#### 2. Railway

Nearly tied with Render on every criterion; loses only on CLI-completeness (rollback-to-old-deploy needs the dashboard) and MCP maturity (server launched more recently). Would be the natural fallback if Render's free-Postgres-expiry trap or 15-minute idle spin-down becomes a practical problem during the build.

#### 3. Fly.io

Cheapest raw compute and the most mature CLI/docs of the three, but the weakest data-layer story for this project: Fly's own Postgres offering is deprecated/costly, forcing an external database vendor into the stack, and there's no first-party MCP server to operate it structurally rather than by CLI-output-parsing.

## Anti-Bias Cross-Check: Render

### Devil's Advocate — Weaknesses

1. No native .NET runtime — same Dockerfile-ownership burden as every other non-Azure candidate; a missed `ASPNETCORE_URLS`/`ASPNETCORE_HTTP_PORTS` binding causes a silent deploy failure with a cryptic health-check error.
2. Free tier's Postgres hard-expires 30 days after creation (14-day grace period) — a real trap if the free tier is used to save money during the 3-week build and the upgrade is forgotten before real data accumulates.
3. Free web service spins down after 15 minutes idle with ~1 minute cold start on the next request — conflicts directly with the PRD persona (weekly/monthly check-ins) and its NFR of "feedback within a few seconds."
4. Starter-tier build-minute limits aren't clearly published — risk of throttling with no visibility, especially with an unoptimized multi-stage .NET Docker build.
5. Autodeploy-on-push can silently override a manual rollback if a new commit lands mid-incident — a footgun for a solo developer firefighting alone.

### Pre-Mortem — How This Could Fail

Six months in, the developer started on Render's free tier to validate cheaply, not realizing the free Postgres database silently expires 30 days after creation. Absorbed in feature work, they missed the grace-period warning email, and the database — holding two months of real categorized transaction history — was deleted, with no backup since the free tier doesn't include one. Meanwhile, because the app is only used weekly per its own persona, the free web service kept spinning down between visits; the first request after each gap ate a slow cold start, making the tool feel broken at exactly the "let me check my spending" moment it exists for. By the time they moved to Starter plus paid Postgres, trust in the tool's reliability had eroded, and re-importing three banks' worth of history felt like starting over.

### Unknown Unknowns

- Render's free Postgres is a 30-day trial with deletion, not a "forever free" tier — easy to miss when skimming the pricing page.
- The `PORT` binding convention is Render-specific glue code, not ASP.NET Core default behavior — the same gotcha class applies on Railway and Fly.io too.
- Render's CLI has expanded rapidly (roughly 3 to 20+ commands recently); docs and community answers may lag the current CLI version — trust `--help` over search results.
- Starter tier's build-minute allowance isn't published clearly; confirm directly in the dashboard rather than assuming it's unlimited.
- The workspace fee is a separate line item from service pricing (should be $0 on the Hobby workspace for a solo user, but it's easy to overlook when reading the pricing page).

## Operational Story

- **Preview deploys**: Render creates preview environments from pull requests when enabled on a Blueprint (`render.yaml`); each preview gets its own URL and can share or isolate its database depending on Blueprint config. Not required for a solo 3-week MVP with no PR review process, but available if branch-based iteration is adopted later.
- **Secrets**: Environment variables and secrets are set via the Render dashboard, CLI (`render env`), or `render.yaml` (with `sync: false` for values that shouldn't be committed). This includes the Neon connection string, since the database is external to Render. Only the account owner (a single user here) can read them. Rotation is manual: update the variable, which triggers a redeploy.
- **Rollback**: `render.com` dashboard "Rollback" button or the `rollback-deploy` API endpoint reverts to a prior successful deploy. Typical time-to-revert is the time to restart the container with the previous image (well under a minute). Caveat: any database migration applied by the rolled-back-from deploy does not automatically roll back — a schema change needs a manual reverse migration if the rollback crosses a migration boundary.
- **Approval**: Redeploying, restarting, and reading logs are safe for the agent to do unattended. Deleting the Postgres database, changing billing/plan tier, and rotating the primary database credential are treated as human-only actions per this project's production-access posture.
- **Logs**: `render logs --resource <service-id> --tail` streams live logs from the CLI; `--output json` gives structured output for scripting. The official MCP server (`mcp.render.com`) exposes log and metrics tools for structured queries once connected.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Neon's serverless autosuspend adds connection latency (cold-start reconnect) on the first request after idle, compounding Render's own free-tier cold start | Research finding | M | L | Use Neon's connection pooler (pooled connection string) from EF Core; if both Render web service and Neon DB are on free/idle tiers, expect the first request after any gap to be noticeably slower — acceptable for a weekly-use MVP but worth setting expectations |
| Cold start (~1 min) on the free web tier makes the app feel broken on the first weekly/monthly visit | Pre-mortem | H | M | Either accept the cold start for a true $0 MVP, or move to Starter ($7/mo) for an always-on instance once the free trial period ends |
| Dockerfile misconfiguration (wrong `ASPNETCORE_URLS`/port binding) causes deploy failures with unclear errors | Devil's advocate | M | M | Follow Render's documented ASP.NET Core Dockerfile pattern exactly; verify the container binds `0.0.0.0:$PORT` locally with `docker run -e PORT=10000` before first deploy |
| Autodeploy-on-push silently overrides a manual rollback if a new commit lands mid-incident | Devil's advocate | L | M | Pause autodeploy (or work on a separate branch) while actively rolling back a bad deploy |
| Starter-tier build-minute limits are undocumented and could throttle deploys | Unknown unknowns | L | L | Check the current allowance in the Render dashboard before relying on frequent deploys; optimize the Dockerfile with layer caching regardless |
| A database migration isn't automatically reverted by a Render rollback, causing schema/app-version mismatch | Research finding | L | H | Keep migrations backward-compatible where possible; document the manual reverse-migration step before any deploy that changes schema |

## Getting Started

1. Add a multi-stage Dockerfile to `MyFinances/backend` that builds with the `mcr.microsoft.com/dotnet/sdk:10.0` image and runs with `mcr.microsoft.com/dotnet/aspnet:10.0`, copying the published output (`dotnet publish` already builds and copies the frontend into `wwwroot` per the existing `BuildAndCopyFrontend` MSBuild target — no separate frontend build step is needed in the Dockerfile).
2. In the Dockerfile's final stage, set `ENV ASPNETCORE_URLS=http://0.0.0.0:$PORT` (or handle `PORT` in `Program.cs`) since Render injects `PORT` at runtime and ASP.NET Core has no built-in convention for it.
3. Create a free Neon project (neon.tech) and copy its **pooled** connection string (uses PgBouncer — required for a typical EF Core connection-per-request pattern against Neon's serverless Postgres); add `Npgsql.EntityFrameworkCore.PostgreSQL` to the backend and point the `DbContext` at it via configuration, not a hardcoded string.
4. Install the Render CLI and authenticate: `render login`.
5. Create a `render.yaml` Blueprint at the repo root declaring one Docker-based web service (pointing at `MyFinances/backend/Dockerfile`) with the Neon connection string set as a `sync: false` environment variable — no `databases:` block needed since Postgres is external to Render.
6. Deploy with `render blueprint launch` (first deploy) or push to the connected branch for autodeploy; verify with `render logs --resource <service-id> --tail`, confirm EF Core migrations ran against Neon, and confirm the SPA loads from the service's root URL.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration (beyond the minimal pattern needed to satisfy Render's deploy requirement)
- CI/CD pipeline setup
- Production-scale architecture (multi-region, HA, DR)
