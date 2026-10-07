// Risk: import-to-dashboard-roundtrip (user-described; related to test-plan.md #1/#2 — imported
// totals must reach the dashboard correctly). Full browser flow across auth -> API -> DB -> UI:
// a signed-in user with no transactions imports a one-row mBank CSV dated this month, categorizes
// it, and sees it in the history and in the spend chart.
// Seed: seed.spec.ts. The signed-out path is covered there.
import { test, expect } from '@playwright/test';
import { buildMBankCsv } from './support/mbank-csv';

// "This month" is decided by the API in Europe/Warsaw (backend CurrentMonthRange), so build today's date there.
const today = new Date().toLocaleDateString('sv-SE', { timeZone: 'Europe/Warsaw' });

test.describe('import-to-dashboard-roundtrip', () => {
  const token = `${Date.now()}`;
  const description = `E2E import ${token}`;
  const accountName = `E2E mBank ${token}`;
  const accountNumber = `E2E-${token}`;
  let imported = false;

  // Deletes the imported transaction through the UI and asserts it is gone. The account and its
  // import batch stay: the API refuses to delete an account that has import history (409), and
  // global-setup recreates the whole database on every run, so nothing outlives the run.
  test.afterEach(async ({ page }) => {
    if (!imported) return;
    await page.goto('/');
    const row = page.getByRole('listitem').filter({ hasText: description });
    await row.getByRole('button', { name: 'Delete' }).click();
    await row.getByRole('button', { name: 'Confirm delete' }).click();
    await expect(row).toBeHidden();
  });

  test('imported transaction shows up in history and spend chart after categorizing', async ({ page }) => {
    test.info().annotations.push({ type: 'test-data', description });

    // Precondition: the signed-in user starts with no transactions.
    const before = await page.request.get('/api/transactions?skip=0&take=1');
    expect(before).toBeOK();
    expect((await before.json()).items).toHaveLength(0);

    // The import needs an account.
    await page.goto('/settings');
    await page.getByRole('button', { name: 'Add account' }).click();
    await page.getByLabel('Account name').fill(accountName);
    await page.getByLabel('Bank', { exact: true }).selectOption('mBank');
    await page.getByLabel('Account number').fill(accountNumber);
    await page.getByRole('button', { name: 'Add account' }).click();
    await expect(page.getByText(accountName)).toBeVisible();

    // Upload a one-row statement and accept the preview.
    await page.goto('/import');
    await page.getByLabel('Account', { exact: true }).selectOption({ label: `${accountName} — ${accountNumber}` });
    await page.getByLabel('Bank statement (CSV or PDF)').setInputFiles({
      name: 'mbank-e2e.csv',
      mimeType: 'text/csv',
      buffer: buildMBankCsv({ date: today, title: description, amount: -12.34 }),
    });
    await page.getByRole('button', { name: 'Upload' }).click();
    await expect(page.getByText(description)).toBeVisible();
    await expect(page.getByText(/Parsed\s*1\s*row\(s\)/)).toBeVisible();
    await page.getByRole('button', { name: 'Continue' }).click();
    imported = true;
    await expect(page.getByText(/Imported\s*1\s*row\(s\)/)).toBeVisible();

    // Charts only count categorized transactions, so categorize the imported one.
    await page.goto('/categorize');
    await expect(page.getByText(description)).toBeVisible();
    const category = page.getByLabel('Category');
    const categoryName = (await category.getByRole('option').nth(1).textContent())!.trim();
    await category.selectOption({ label: categoryName });
    await page.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByText(/Yay, all done/)).toBeVisible();

    // Dashboard: the transaction is in the history with its category and amount...
    await page.goto('/');
    const row = page.getByRole('listitem').filter({ hasText: description });
    await expect(row).toBeVisible();
    await expect(row).toContainText(categoryName);
    await expect(row).toContainText(/12[.,]34/);

    // ...and the spend chart is populated with that category instead of the empty state.
    await expect(page.getByRole('heading', { name: /Spend by category/ })).toBeVisible();
    await expect(page.getByText(/No categorized spend/)).toBeHidden();
    await expect(page.getByRole('button', { name: new RegExp(`${categoryName}.*12[.,]34`) })).toBeVisible();
  });
});
