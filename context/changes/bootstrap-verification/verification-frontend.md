---
bootstrapped_at: 2026-09-18T11:50:25Z
starter_id: react-router
starter_name: "React Router (formerly Remix)"
project_name: my-finances-frontend
language_family: js
package_manager: npm
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "npm audit --json"
---

## Hand-off

```yaml
starter_id: react-router
package_manager: npm
project_name: my-finances-frontend
hints:
  language_family: js
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: false
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
```

## Why this stack

Companion hand-off to `context/foundation/tech-stack.md` (the `.NET` API hand-off). This solo builder's split stack pairs a .NET API backend with a React/TypeScript SPA frontend. Vite + React was flagged during the original custom-path interview as failing the convention-based gate (no built-in routing/folder conventions), so the pick moved to React Router instead — same React/TypeScript feel, file-based routing conventions, all four agent-friendly gates clear, `verified` bootstrapper confidence — run purely as a client SPA against the .NET API (no SSR data-loading against the .NET backend is assumed at scaffold time). This file exists as a second hand-off because the bootstrapper's schema carries exactly one `starter_id` per file; the primary hand-off's `## Why this stack` paragraph already documents this pairing narratively. Auth (FR-001), deployment target, and CI/CD flow mirror the backend hand-off for consistency. Payments, realtime, AI, and background jobs remain out of scope per the PRD's non-goals.

## Pre-scaffold verification

| Signal      | Value                                          | Severity | Notes                                              |
| ----------- | ----------------------------------------------- | -------- | --------------------------------------------------- |
| npm package | create-react-router v8.4.0 published 2026-09-15 | fresh    | resolved from `cmd_template`                        |
| GitHub repo | not run                                         | n/a      | card `docs_url` is `https://reactrouter.com` — not GitHub |

Recency: create-react-router v8.4.0 published 2026-09-15 (fresh). Proceeding.

## Scaffold log

**Resolved invocation**: `npx create-react-router@latest .bootstrap-scaffold --yes --package-manager npm`
**Strategy**: subdir-then-move
**Exit code**: 0 (template copy succeeded; the CLI's own bundled dependency-install step failed transiently — see note below)
**Files moved**: 11 top-level entries (`.agents/`, `.dockerignore`, `.gitignore`, `app/`, `Dockerfile`, `README.md`, `node_modules/`, `package-lock.json`, `package.json`, `public/`, `react-router.config.ts`, `tsconfig.json`, `vite.config.ts`)
**Conflicts (.scaffold siblings)**: none — `MyFinances/frontend/` was empty
**.gitignore handling**: moved silently (cwd had no pre-existing `.gitignore`)
**.bootstrap-scaffold cleanup**: deleted

Note: the CLI's own bundled `npm install` step reported "Failed to install dependencies" for reasons not surfaced in its output. Ran `npm install` manually inside `.bootstrap-scaffold/` immediately after — it completed cleanly (181 packages, 0 vulnerabilities) — before the move-up. Also ran `npx react-router typegen` post-move to generate the route-derived `+types/*` modules (`tsc --noEmit` fails without this step on a fresh checkout; this is expected React Router v7 behavior, not a scaffold defect) — `tsc --noEmit` passed clean afterward.

## Post-scaffold audit

**Tool**: `npm audit --json`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not applicable — zero findings

## Hints recorded but not acted on

| Hint                    | Value                |
| ----------------------- | --------------------- |
| bootstrapper_confidence | verified              |
| quality_override        | false                 |
| path_taken              | custom                |
| self_check_answers      | typed: true, from_official_starter: true, conventions: true, docs_current: true, can_judge_agent: false |
| team_size               | solo                  |
| deployment_target       | azure-app-service     |
| ci_provider             | github-actions        |
| ci_default_flow         | auto-deploy-on-merge  |
| has_auth                | true                  |
| has_payments            | false                 |
| has_realtime            | false                 |
| has_ai                  | false                 |
| has_background_jobs     | false                 |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- Verified working: `npm run dev` served `http://localhost:5173/` with a 200 response during this run.
- Point the frontend at the .NET API's base URL (`http://localhost:5007` per `MyFinances/backend/Properties/launchSettings.json`) — e.g. via a `.env`/Vite proxy config — since this is a pure client SPA with no built-in backend wiring.
- Review `MyFinances/frontend/.agents/` — the CLI included a "React Router agent skill" bundle; inspect its contents before relying on it.
- `git init` (if you have not already) to start your own repo history — recommended at the `Kurs 10xDev` root or at `MyFinances/`, matching the same choice made for the backend.
