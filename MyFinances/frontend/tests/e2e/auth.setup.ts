import { test as setup, expect } from '@playwright/test';

const authFile = 'playwright/.auth/user.json';

setup('register the test user and save the session', async ({ page }) => {
  const username = process.env.E2E_USERNAME;
  const password = process.env.E2E_PASSWORD;
  if (!username || !password) {
    throw new Error('Set E2E_USERNAME and E2E_PASSWORD (see context/foundation/test-stack.md, ## E2E)');
  }

  // global-setup starts an empty database every run, so the gated registration
  // (Auth__AllowedEmail = E2E_USERNAME) creates the user and signs them in.
  await page.goto('/register');
  // The SPA drops input typed before it hydrates. Registering twice would fail, so retry
  // only the typing until the controlled inputs hold the values, then submit once.
  await expect(async () => {
    await page.getByLabel('Email').fill('');
    await page.getByLabel('Email').fill(username);
    await page.getByLabel('Password', { exact: true }).fill('');
    await page.getByLabel('Password', { exact: true }).fill(password);
    await expect(page.getByLabel('Email')).toHaveValue(username);
  }).toPass();
  await page.getByRole('button', { name: 'Register' }).click();

  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();

  await page.context().storageState({ path: authFile });
});
