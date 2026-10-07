# Test stack

## E2E

<!-- Written by /10x-e2e-setup. Re-run it to change this section; other skills only read it. -->

- runner: Playwright Test, @playwright/test 1.63.0
- config: MyFinances/frontend/playwright.config.ts (run every command from MyFinances/frontend)
- single-spec command: npx playwright test tests/e2e/seed.spec.ts
- full-suite command: npx playwright test
- base URL: http://localhost:5007
- port: 5007 (detected from MyFinances/backend/Properties/launchSettings.json; detected default 5007, override with E2E_PORT). The throwaway Postgres container uses 55432 (override with E2E_DB_PORT). A dev API running on 5007 would be silently reused, so stop it first.
- web server command: npm run build && dotnet build ../backend && dotnet run --project ../backend --no-build --no-launch-profile (API serves build/client through ASPNETCORE_WEBROOT, Production env, PORT=$E2E_PORT, ConnectionStrings__Default and Auth__AllowedEmail from the config's env, never the Neon user-secrets); reuseExistingServer outside CI. tests/e2e/global-setup.ts starts a fresh postgres:17-alpine container (Docker required), runs `dotnet ef database update --no-build`, and removes the container afterwards.
- auth setup project: setup (tests/e2e/auth.setup.ts) registers the gated test user through /register on the fresh database, credentials from E2E_USERNAME / E2E_PASSWORD in MyFinances/frontend/.env
- storageState: playwright/.auth/user.json (gitignored, under MyFinances/frontend)
- seed: MyFinances/frontend/tests/e2e/seed.spec.ts — protects #3 (browser-level slice: a signed-out visitor is redirected to /login and gets no transaction data; two-user ownership stays integration, Phase 3)
- browser CLI: playwright-cli, command skill at .claude/skills/playwright-cli/SKILL.md
- updated: 2026-10-02
