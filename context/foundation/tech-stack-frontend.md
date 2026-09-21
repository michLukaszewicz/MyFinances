---
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
---

## Why this stack

Companion hand-off to `context/foundation/tech-stack.md` (the `.NET` API hand-off). This solo builder's split stack pairs a .NET API backend with a React/TypeScript SPA frontend. Vite + React was flagged during the original custom-path interview as failing the convention-based gate (no built-in routing/folder conventions), so the pick moved to React Router instead — same React/TypeScript feel, file-based routing conventions, all four agent-friendly gates clear, `verified` bootstrapper confidence — run purely as a client SPA against the .NET API (no SSR data-loading against the .NET backend is assumed at scaffold time). This file exists as a second hand-off because the bootstrapper's schema carries exactly one `starter_id` per file; the primary hand-off's `## Why this stack` paragraph already documents this pairing narratively. Auth (FR-001), deployment target, and CI/CD flow mirror the backend hand-off for consistency. Payments, realtime, AI, and background jobs remain out of scope per the PRD's non-goals.
