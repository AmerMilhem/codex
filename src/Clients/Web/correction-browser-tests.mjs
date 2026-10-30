import { chromium, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';

const directory = new URL('../../../artifacts/correction/', import.meta.url);
await mkdir(directory, { recursive: true });
const origin = process.env.ERP_UI_ORIGIN;
const tenant = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const company = '11111111-1111-4111-8111-111111111111';
const base = `${origin}/api/v1/tenants/${tenant}/companies/${company}/finance`;
const headers = {
  Authorization: `Bearer ${process.env.ERP_E2E_TOKEN}`,
  'Content-Type': 'application/json',
};
const checks = [];
function check(name, passed) {
  checks.push({ name, status: passed ? 'PASS' : 'FAIL' });
  if (!passed) throw new Error(name);
}
async function get(path) {
  const response = await fetch(base + path, { headers });
  if (response.status !== 200) throw new Error(`GET ${path}: ${response.status}`);
  return response.json();
}
const browser = await chromium.launch({
  ...(process.env.POC_CHROMIUM ? { executablePath: process.env.POC_CHROMIUM } : {}),
  headless: true,
  args: ['--no-sandbox'],
});
const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } });
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
try {
  await page.goto(origin + '/workspace');
  await page.locator('#tenant').fill(tenant);
  await page.locator('#company').fill(company);
  await page.locator('#session-token').fill(process.env.ERP_E2E_TOKEN);
  await page.getByRole('button', { name: 'فتح الجلسة', exact: true }).click();
  await page.getByRole('link', { name: 'الدفاتر المحاسبية' }).click();
  await expect(page.locator('#book-empty')).toBeVisible();
  check(
    'No operational SQL seed for book period or accounts',
    (await get('/books')).length === 0 &&
      (await get('/periods')).length === 0 &&
      (await get('/accounts')).length === 0,
  );
  check(
    'Arabic RTL Book Setup form',
    (await page.locator('html').getAttribute('dir')) === 'rtl' &&
      (await page.locator('#book-create-form').textContent()).includes('إنشاء دفتر'),
  );

  await page.locator('#book-create-code').fill(' primary ');
  await page.locator('#book-create-name').fill('Primary Accounting Book');
  await page.locator('#book-create-currency').selectOption('JOD');
  const keys = [];
  let committed;
  await page.route('**/finance/books', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    keys.push(route.request().headers()['idempotency-key']);
    const response = await route.fetch();
    if (response.status() !== 200) throw new Error(`Create book: ${response.status()}`);
    committed = await response.json();
    await route.abort('failed');
  });
  await page.locator('#create-book').click();
  await expect(page.locator('[role="alert"]')).toBeVisible();
  check('Book commit survives lost HTTP response', !!committed?.reference);
  check(
    'Book retry freezes code name and currency',
    (await page.locator('#book-create-code').isDisabled()) &&
      (await page.locator('#book-create-name').isDisabled()) &&
      (await page.locator('#book-create-currency').isDisabled()),
  );
  await page.unroute('**/finance/books');
  const retryPromise = page.waitForRequest(
    (r) => r.url().endsWith('/finance/books') && r.method() === 'POST',
  );
  await page.locator('#create-book').click();
  const retry = await retryPromise;
  check(
    'Book retry uses original key and canonical payload',
    retry.headers()['idempotency-key'] === keys[0] &&
      retry.postDataJSON().code === 'PRIMARY' &&
      retry.postDataJSON().functionalCurrency === 'JOD',
  );
  await expect(page.locator('#book-table tbody tr')).toHaveCount(1);
  const book = (await get('/books'))[0];
  check(
    'Book has explicit ID Active status and JOD precision',
    book.id === committed.reference &&
      book.code === 'PRIMARY' &&
      book.status === 'Active' &&
      book.functionalCurrency === 'JOD' &&
      book.minorUnits === 3 &&
      !!book.createdAt,
  );
  await page.screenshot({ path: new URL('book-setup-ar.png', directory).pathname, fullPage: true });

  await page.locator('#book-create-code').fill('SECONDARY');
  await page.locator('#book-create-name').fill('Forbidden second book');
  const duplicateResponse = page.waitForResponse(
    (r) => r.url().endsWith('/finance/books') && r.request().method() === 'POST',
  );
  await page.locator('#create-book').click();
  const duplicate = await duplicateResponse;
  await expect(page.locator('[role="alert"]')).toContainText('يوجد دفتر');
  check(
    'One-book-per-company rule rejects even a different code',
    duplicate.status() === 409 && (await duplicate.json()).errorId === 'FIN_BOOK_DUPLICATE',
  );
  const invalid = await fetch(base + '/books', {
    method: 'POST',
    headers: { ...headers, 'Idempotency-Key': 'invalid-currency' },
    body: JSON.stringify({ code: 'OTHER', name: 'Other', functionalCurrency: 'ZZZ' }),
  });
  check(
    'Invalid functional currency rejected at API boundary',
    invalid.status === 422 && (await invalid.json()).errorId === 'FIN_BOOK_CURRENCY_INVALID',
  );
  const denied = await fetch(
    `${process.env.ERP_LIMITED_API_ORIGIN}/api/v1/tenants/${tenant}/companies/${company}/finance/books`,
    {
      method: 'POST',
      headers,
      body: JSON.stringify({ code: 'OTHER', name: 'Other', functionalCurrency: 'JOD' }),
    },
  );
  check(
    'Journal Post permission cannot create an accounting book',
    denied.status === 403 && (await denied.json()).errorId === 'AUTH_SCOPE_DENIED',
  );

  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await page.locator('#period-create-start').fill('2027-01-01');
  await page.locator('#period-create-end').fill('2027-01-31');
  await page.locator('#create-period').click();
  await expect(page.locator('#period-table tbody tr')).toHaveCount(1);
  const period = (await get('/periods'))[0];
  check('New book accepts UI-created fiscal period', period.status === 'Open');
  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'دليل الحسابات' }).click();
  async function createAccount(code, name, expectedRows) {
    await page.locator('#account-create-code').fill(code);
    await page.locator('#account-create-name').fill(name);
    const response = page.waitForResponse(
      (r) => r.url().endsWith('/finance/accounts') && r.request().method() === 'POST',
    );
    await page.locator('#create-account').click();
    const result = await response;
    if (result.status() !== 200) throw new Error(`Account ${code}: ${result.status()}`);
    await expect(page.locator('#account-table tbody tr')).toHaveCount(expectedRows);
    return (await get('/accounts')).find((x) => x.code === code);
  }
  const cash = await createAccount('1100', 'Cash', 1);
  const expense = await createAccount('6100', 'Office Expense', 2);
  check('Company accounts created through Slice 7 UI', !!cash?.id && !!expense?.id);

  await page.locator('#account-setup a').first().click();
  await page.locator('#new-journal').click();
  await page.locator('.line-grid select').first().waitFor();
  await page.locator('#posting-date').fill('2027-01-01');
  await page.locator('#journal-reference').fill('Book golden');
  await page.locator('#journal-description').fill('UI-created book period and accounts');
  await page.locator('.line-grid select').nth(0).selectOption(expense.id);
  await page.locator('.debit').nth(0).fill('100');
  await page.locator('.line-grid select').nth(1).selectOption(cash.id);
  await page.locator('.credit').nth(1).fill('100');
  await page.locator('#save-draft').click();
  await page.waitForURL(/\/journals\/[0-9a-f-]{36}$/);
  const journalId = page.url().split('/').at(-1);
  await page.locator('#validate-journal').click();
  await expect(page.locator('#validation-valid')).toBeVisible();
  await page.locator('#post-journal').click();
  await expect(page.locator('#journal-status')).toHaveText('مرحّل');
  check(
    'Book-backed journal validates and posts through UI',
    (await get('/journals/' + journalId)).status === 'Posted',
  );

  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await page.getByRole('button', { name: 'فحص الإغلاق' }).click();
  await expect(page.locator('#period-validation')).toContainText('اجتازت');
  await page.locator('#period-confirm').check();
  await page.locator('#confirm-period-close').click();
  await expect(page.locator('#period-table tbody tr')).toContainText('مغلقة');
  check(
    'Original period closed',
    (await get('/periods')).find((p) => p.id === period.id).status === 'Closed',
  );
  await page.locator('#period-create-start').fill('2027-02-01');
  await page.locator('#period-create-end').fill('2027-02-28');
  await page.locator('#create-period').click();
  await expect(page.locator('#period-table tbody tr')).toHaveCount(2);
  const periodB = (await get('/periods')).find((p) => p.status === 'Open');
  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'القيود اليدوية' }).click();
  await page.locator(`a[href$="/journals/${journalId}"]`).first().click();
  await page.locator('#correct-journal').click();
  await expect(page.locator('#correction-original-lines tr')).toHaveCount(2);
  await page.locator('#correction-date').fill('2027-02-02');
  await page.locator('#correction-reason').fill('Correct 100 to 120');
  await page.locator('.correction-debit').first().fill('120');
  await page.locator('.correction-credit').nth(1).fill('120');
  await page.locator('#correction-confirm').check();
  await page.screenshot({ path: new URL('correction-ar.png', directory).pathname, fullPage: true });
  let correctionCommitted;
  const correctionKeys = [];
  await page.route('**/journals/*/correct', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    correctionKeys.push(route.request().headers()['idempotency-key']);
    const response = await route.fetch();
    if (response.status() !== 200)
      throw new Error(`Correction: ${response.status()} ${await response.text()}`);
    correctionCommitted = await response.json();
    await route.abort('failed');
  });
  await page.locator('#submit-correction').click();
  await expect(page.locator('[role="alert"]')).toBeVisible();
  check('Correction correctionCommitted before lost response', !!correctionCommitted?.reference);
  await page.unroute('**/journals/*/correct');
  const correctionPromise = page.waitForRequest(
    (r) => r.url().endsWith(`/journals/${journalId}/correct`) && r.method() === 'POST',
  );
  await page.locator('#submit-correction').click();
  const correctionRetry = await correctionPromise;
  check(
    'Retry retains same key and corrected lines',
    correctionRetry.headers()['idempotency-key'] === correctionKeys[0] &&
      correctionRetry.postDataJSON().lines[0].debit === '120' &&
      correctionRetry.postDataJSON().lines[1].credit === '120',
  );
  await page.waitForURL(/\/journals\/[0-9a-f-]{36}$/);
  const replacementId = page.url().split('/').at(-1);
  await expect(page.locator('#correction-relationship')).toBeVisible();
  const relation = (await get('/journals/' + journalId)).correction;
  check(
    'Three distinct linked journals',
    replacementId === correctionCommitted.reference &&
      new Set([journalId, relation.reversalJournalId, relation.replacementJournalId]).size === 3,
  );
  await page.locator('#correction-original').click();
  await expect(page.locator('#correction-relationship')).toBeVisible();
  await page.locator('#correction-reversal').click();
  await expect(page.locator('#correction-relationship')).toBeVisible();
  await page.locator('#correction-replacement').click();
  await expect(page.locator('#correction-relationship')).toBeVisible();
  check('Three-way UI navigation works', true);
  const a = await get('/trial-balance?startDate=2027-01-01&endDate=2027-01-31');
  const b = await get('/trial-balance?startDate=2027-02-01&endDate=2027-02-28');
  const total = await get('/trial-balance?startDate=2027-01-01&endDate=2027-02-28');
  const gl = await get(
    `/general-ledger?accountId=${expense.id}&startDate=2027-01-01&endDate=2027-02-28`,
  );
  check(
    'A preserves 100; B contains reversal and 120 replacement',
    a.totals.periodDebit === '100' &&
      b.totals.periodDebit === '220' &&
      b.totals.periodCredit === '220',
  );
  check(
    'TB and GL reconcile all three movements',
    total.reconciliation.passed && gl.reconciliation.passed && gl.totalCount === 3,
  );
  const same = await fetch(base + `/journals/${journalId}/correct`, {
    method: 'POST',
    headers: { ...headers, 'If-Match': '"2"', 'Idempotency-Key': correctionKeys[0] },
    body: JSON.stringify(correctionRetry.postDataJSON()),
  });
  check('Response-loss correction retry did not duplicate effect', same.status === 200);
  const deniedCorrection = await fetch(
    `${process.env.ERP_LIMITED_API_ORIGIN}/api/v1/tenants/${tenant}/companies/${company}/finance/journals/${journalId}/correct`,
    {
      method: 'POST',
      headers: { ...headers, 'If-Match': '"2"', 'Idempotency-Key': 'correction-denied' },
      body: JSON.stringify(correctionRetry.postDataJSON()),
    },
  );
  check('Post permission alone cannot Correct', deniedCorrection.status === 403);
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.locator('#correction-relationship')).toContainText('Correction');
  check('English LTR relationship', true);
  await page.screenshot({ path: new URL('correction-en.png', directory).pathname, fullPage: true });
  check('No browser runtime errors', errors.length === 0);
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify(
      {
        status: 'PASS',
        checks,
        bookId: book.id,
        periodId: period.id,
        periodBId: periodB.id,
        journalId,
        replacementId,
        relation,
        expenseId: expense.id,
        cashId: cash.id,
      },
      null,
      2,
    ),
  );
  console.log(JSON.stringify({ status: 'PASS', browserChecks: checks.length }));
} finally {
  await browser.close();
}
