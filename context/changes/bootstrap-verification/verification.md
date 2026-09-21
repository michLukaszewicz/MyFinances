---
bootstrapped_at: 2026-09-18T11:22:13Z
starter_id: dotnet
starter_name: .NET (ASP.NET Core webapi)
project_name: my-finances
language_family: multi
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "null"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: my-finances
hints:
  language_family: multi
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

A solo builder shipping a personal-finance MVP in 3 after-hours weeks explicitly asked for a split stack: a .NET API backend with a Vite/React/TypeScript-style frontend. No vetted recommended default exists for a polyglot `(web-app, dotnet+js)` combination, so this went through the full custom-path interview. .NET (ASP.NET Core webapi) is the backend `starter_id`, clearing all four agent-friendly gates with `verified` bootstrapper confidence. For the frontend, Vite + React was flagged as failing the convention-based gate (no built-in routing/folder conventions), so the pick moved to React Router instead — same React/TypeScript feel, file-based routing conventions, all four gates clear, `verified` bootstrapper confidence — run purely as a client SPA against the .NET API. Because the hand-off schema carries one primary `starter_id`, `dotnet` drives the record and the React Router frontend pairing is documented here for manual/second-pass scaffolding. Auth (FR-001) is in scope; payments, realtime, AI, and background jobs are out per the PRD's non-goals. Deployment defaults to Azure App Service (the card's first default, left to the recommendation); CI runs on GitHub Actions with auto-deploy-on-merge. The five-point self-check came back with one gap (judging agent-consistency confidence), below the two-gap nudge threshold, so no Socratic pivot fired.

## Pre-scaffold verification

| Signal      | Value      | Severity | Notes                                                               |
| ----------- | ---------- | -------- | -------------------------------------------------------------------- |
| npm package | not run    | n/a      | non-JS starter (`language_family: dotnet` on the registry card)      |
| GitHub repo | not run    | n/a      | `docs_url` is `https://learn.microsoft.com/aspnet/core` — not GitHub |

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (`.bootstrap-scaffold.csproj`, `.bootstrap-scaffold.http`, `Program.cs`, `Properties/launchSettings.json`, `appsettings.Development.json`, `appsettings.json`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold
**.bootstrap-scaffold cleanup**: deleted

Note: the registry `cmd_template` passes `-n {name}` to `dotnet new`, and for this starter `{name}` resolves to the temp directory token `.bootstrap-scaffold` under the `subdir-then-move` strategy. Unlike JS-family `create-*` CLIs (where `{name}` only names an output directory), .NET's `-n` also sets the internal project/namespace name — so the moved-up `.csproj` was initially named `.bootstrap-scaffold.csproj` with `RootNamespace` set to `_bootstrap_scaffold`. This is a known mismatch between the schema's uniform `{name}` substitution rule and .NET's naming semantics.

**Post-scaffold manual reorg (user-requested, same session)**: the user asked for the whole project under `MyFinances/` with separate `backend/` and `frontend/` subfolders. Applied manually, outside the bootstrapper's automated conflict-matrix flow:
- Moved all 6 scaffolded files from cwd into `MyFinances/backend/`.
- Renamed `.bootstrap-scaffold.csproj` → `MyFinances/backend/Backend.csproj`; renamed `.bootstrap-scaffold.http` → `MyFinances/backend/Backend.http`.
- Fixed `RootNamespace` in the `.csproj` from `_bootstrap_scaffold` to `MyFinances.Backend`.
- Added `MyFinances/backend/.gitignore` (`bin/`, `obj/`) — the scaffold shipped with no `.gitignore`.
- Created empty `MyFinances/frontend/` for the not-yet-scaffolded React Router frontend.
- Verified with `dotnet build` from `MyFinances/backend/` — build succeeded, 0 errors.

## Post-scaffold audit

**Tool**: skipped — no built-in audit tool for `multi` (automated Step 3 dispatch).
**Manual signal found instead**: `dotnet build` (run to verify the post-reorg move) surfaced a NuGet restore-time advisory:

#### HIGH findings

- **Microsoft.OpenApi 2.0.0** (NU1903) — known high-severity vulnerability. Advisory: https://github.com/advisories/GHSA-v5pm-xwqc-g5wc. Pulled in transitively via `Microsoft.AspNetCore.OpenApi` in the webapi template. Fix: check for a patched `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` version and bump.

**Recommended external tool**: `dotnet list package --vulnerable` for the .NET API (now at `MyFinances/backend/`), `npm audit` for the React Router frontend once it is scaffolded into `MyFinances/frontend/`.

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
- `git init` (if you have not already) to start your own repo history — recommended at the `Kurs 10xDev` root or at `MyFinances/`, whichever is your intended repo boundary.
- The hand-off's `## Why this stack` documents a paired React Router frontend that this run does not scaffold (the hand-off schema carries only one `starter_id`). Run the frontend starter's CLI manually (`npx create-react-router@latest MyFinances/frontend --yes --package-manager npm`) — note `MyFinances/frontend/` already exists as an empty placeholder from the manual reorg.
- Patch the HIGH-severity `Microsoft.OpenApi` advisory found via `dotnet build` (see Post-scaffold audit above) — check for a patched version and bump the `Microsoft.AspNetCore.OpenApi` package reference in `MyFinances/backend/Backend.csproj`.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep (none were created this run).
