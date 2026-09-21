---
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
---

## Why this stack

A solo builder shipping a personal-finance MVP in 3 after-hours weeks explicitly asked for a split stack: a .NET API backend with a Vite/React/TypeScript-style frontend. No vetted recommended default exists for a polyglot `(web-app, dotnet+js)` combination, so this went through the full custom-path interview. .NET (ASP.NET Core webapi) is the backend `starter_id`, clearing all four agent-friendly gates with `verified` bootstrapper confidence. For the frontend, Vite + React was flagged as failing the convention-based gate (no built-in routing/folder conventions), so the pick moved to React Router instead — same React/TypeScript feel, file-based routing conventions, all four gates clear, `verified` bootstrapper confidence — run purely as a client SPA against the .NET API. Because the hand-off schema carries one primary `starter_id`, `dotnet` drives the record and the React Router frontend pairing is documented here for manual/second-pass scaffolding. Auth (FR-001) is in scope; payments, realtime, AI, and background jobs are out per the PRD's non-goals. Deployment defaults to Azure App Service (the card's first default, left to the recommendation); CI runs on GitHub Actions with auto-deploy-on-merge. The five-point self-check came back with one gap (judging agent-consistency confidence), below the two-gap nudge threshold, so no Socratic pivot fired.
