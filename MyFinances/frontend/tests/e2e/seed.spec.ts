// Seed test — the exemplar every generated E2E test copies (role locators, waits for
// state, self-contained, named after its risk).
// Provenance: risk #3 (test-plan.md) — a user reads another user's data. Browser-level slice only:
// a signed-out visitor never reaches protected data. The two-user ownership part is integration (Phase 3).
import { test, expect } from '@playwright/test';

// This risk IS the signed-out path, so opt out of the saved session.
test.use({ storageState: { cookies: [], origins: [] } });

test('signed-out visitor is sent to login and gets no transaction data (auth-gate-roundtrip)', async ({ page }) => {
  const username = process.env.E2E_USERNAME!;
  const password = process.env.E2E_PASSWORD!;

  // The API refuses an anonymous read outright, not just the page.
  const anonymousRead = await page.request.get('/api/transactions?skip=0&take=1');
  expect(anonymousRead.status()).toBe(401);

  // The protected page bounces to login.
  await page.goto('/import');
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('heading', { name: 'Log in' })).toBeVisible();

  // Signing in is idempotent, so retry fill + submit until the SPA has hydrated and the submit lands.
  await expect(async () => {
    await page.getByLabel('Email').fill('');
    await page.getByLabel('Email').fill(username);
    await page.getByLabel('Password', { exact: true }).fill('');
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Log in' }).click();
    await page.waitForURL((url) => url.pathname === '/', { timeout: 5_000 });
  }).toPass();

  // Only a signed-in user sees the app navigation.
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();

  // Nothing to clean up: no data created, and signing the shared user out would revoke
  // nothing here but is skipped on purpose (other specs load the saved session).
});
