import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

// Local secrets (E2E_USERNAME, E2E_PASSWORD) come from the gitignored .env.
// In CI the file is absent and the variables come from the job.
if (existsSync('.env')) process.loadEnvFile('.env');

// 5007 was detected from backend/Properties/launchSettings.json (the API port).
// E2E_PORT overrides it when that port is taken on this machine.
const PORT = Number(process.env.E2E_PORT ?? 5007);
const baseURL = `http://localhost:${PORT}`;

// Throwaway Postgres container started by tests/e2e/global-setup.ts. The API is pointed
// at it below, so the E2E run never touches the Neon database from user-secrets.
export const DB_PORT = Number(process.env.E2E_DB_PORT ?? 55432);
export const DB_CONNECTION = `Host=localhost;Port=${DB_PORT};Database=myfinances_e2e;Username=postgres;Password=e2e`;

export default defineConfig({
  testDir: './tests/e2e',
  globalSetup: './tests/e2e/global-setup.ts',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /.*\.setup\.ts/ },
    {
      name: 'chromium',
      testIgnore: /.*\.setup\.ts/,
      use: { ...devices['Desktop Chrome'], storageState: 'playwright/.auth/user.json' },
      dependencies: ['setup'],
    },
  ],
  webServer: {
    // Production-like: the SPA is built, then served by the API itself (single origin,
    // as in production) through ASPNETCORE_WEBROOT instead of the publish-time wwwroot copy.
    // Build and run are separate steps so the later `dotnet ef` call can use --no-build.
    command: `npm run build && dotnet build ../backend && dotnet run --project ../backend --no-build --no-launch-profile`,
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 300_000,
    env: {
      PORT: String(PORT),
      ASPNETCORE_ENVIRONMENT: 'Production',
      ASPNETCORE_WEBROOT: resolve('build/client'),
      ConnectionStrings__Default: DB_CONNECTION,
      Auth__AllowedEmail: process.env.E2E_USERNAME ?? '',
    },
  },
});
