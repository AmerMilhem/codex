import { chromium, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';

const directory = new URL('../../../artifacts/account-setup/', import.meta.url);
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
  await page.getByRole('link', { name: 'دليل الحسابات' }).click();
  await expect(page.locator('#account-empty')).toBeVisible();
  check(
    'Company begins without SQL-seeded accounts or fiscal periods',
    (await get('/accounts')).length === 0 && (await get('/periods')).length === 0,
  );
  check(
    'Arabic RTL account creation form',
    (await page.locator('html').getAttribute('dir')) === 'rtl' &&
      (await page.locator('#account-create-form').textContent()).includes('إنشاء حساب'),
  );

  await page.locator('#account-create-code').fill('6100');
  await page.locator('#account-create-name').fill('Office Expense');
  const keys = [];
  let committed;
  await page.route('**/finance/accounts', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    keys.push(route.request().headers()['idempotency-key']);
    const response = await route.fetch();
    if (response.status() !== 200) throw new Error(`Create account: ${response.status()}`);
    committed = await response.json();
    await route.abort('failed');
  });
  await page.locator('#create-account').click();
  await expect(page.locator('[role="alert"]')).toBeVisible();
  check('Lost response follows committed account creation', !!committed?.reference);
  check(
    'Retry freezes code name and posting flag',
    (await page.locator('#account-create-code').isDisabled()) &&
      (await page.locator('#account-create-name').isDisabled()) &&
      (await page.locator('#account-create-postable').isDisabled()),
  );
  await page.unroute('**/finance/accounts');
  const retryPromise = page.waitForRequest(
    (r) => r.url().endsWith('/finance/accounts') && r.method() === 'POST',
  );
  await page.locator('#create-account').click();
  const retry = await retryPromise;
  check(
    'Account retry keeps idempotency key and exact payload',
    retry.headers()['idempotency-key'] === keys[0] &&
      retry.postDataJSON().code === '6100' &&
      retry.postDataJSON().name === 'Office Expense' &&
      retry.postDataJSON().postingAllowed === true,
  );
  await expect(page.locator('#account-table tbody tr')).toHaveCount(1);
  const expense = (await get('/accounts')).find((x) => x.code === '6100');
  check(
    'Expense account starts Active with creator and one identity',
    expense?.id === committed.reference &&
      expense.status === 'Active' &&
      expense.postingAllowed &&
      !!expense.createdAt &&
      !!expense.createdBySubject,
  );

  async function create(code, name, postingAllowed = true) {
    await page.locator('#account-create-code').fill(code);
    await page.locator('#account-create-name').fill(name);
    if (postingAllowed) await page.locator('#account-create-postable').check();
    else await page.locator('#account-create-postable').uncheck();
    const response = page.waitForResponse(
      (r) => r.url().endsWith('/finance/accounts') && r.request().method() === 'POST',
    );
    await page.locator('#create-account').click();
    const result = await response;
    if (result.status() !== 200) throw new Error(`Create ${code}: ${result.status()}`);
    await expect(page.locator('#account-table tbody tr')).toHaveCount(postingAllowed ? 2 : 3);
    return (await get('/accounts')).find((x) => x.code === code);
  }
  const cash = await create('1100', 'Cash');
  const header = await create('1000', 'Assets Header', false);
  check(
    'Cash is postable and header is Active but non-postable',
    cash?.status === 'Active' &&
      cash.postingAllowed &&
      header?.status === 'Active' &&
      !header.postingAllowed,
  );
  await page.screenshot({
    path: new URL('account-setup-ar.png', directory).pathname,
    fullPage: true,
  });

  await page.locator('#account-create-code').fill('A B');
  await page.locator('#account-create-name').fill('Invalid');
  await page.locator('#create-account').click();
  await expect(page.locator('[role="alert"]')).toContainText('رمزًا');
  check(
    'Invalid code is explained in Arabic without request',
    (await get('/accounts')).length === 3,
  );
  await page.locator('#account-create-code').fill('6100');
  await page.locator('#account-create-name').fill('Duplicate');
  const duplicateResponse = page.waitForResponse(
    (r) => r.url().endsWith('/finance/accounts') && r.request().method() === 'POST',
  );
  await page.locator('#create-account').click();
  const duplicate = await duplicateResponse;
  await expect(page.locator('[role="alert"]')).toContainText('يوجد حساب');
  check(
    'Duplicate code rejected at UI/API boundary',
    duplicate.status() === 409 && (await duplicate.json()).errorId === 'FIN_ACCOUNT_DUPLICATE',
  );
  const denied = await fetch(
    `${process.env.ERP_LIMITED_API_ORIGIN}/api/v1/tenants/${tenant}/companies/${company}/finance/accounts`,
    {
      method: 'POST',
      headers,
      body: JSON.stringify({ code: '7000', name: 'Denied', postingAllowed: true }),
    },
  );
  check(
    'Account Create remains independent from Journal Post permission',
    denied.status === 403 && (await denied.json()).errorId === 'AUTH_SCOPE_DENIED',
  );

  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await expect(page.locator('#period-empty')).toBeVisible();
  await page.locator('#period-create-start').fill('2027-01-01');
  await page.locator('#period-create-end').fill('2027-01-31');
  await page.locator('#create-period').click();
  await expect(page.locator('#period-table tbody tr')).toHaveCount(1);
  const period = (await get('/periods'))[0];
  check(
    'Fiscal period created through Slice 6 UI',
    period.status === 'Open' && period.startDate === '2027-01-01',
  );
  await page.locator('#fiscal-periods a').first().click();
  await page.locator('#new-journal').click();
  await page.locator('.line-grid select').first().waitFor();
  await page.locator('#posting-date').fill('2027-01-01');
  await page.locator('#journal-reference').fill('Accounts golden');
  await page.locator('#journal-description').fill('UI-created accounts');
  await page.locator('.line-grid select').nth(0).selectOption(expense.id);
  await page.locator('.debit').nth(0).fill('100');
  await page.locator('.line-grid select').nth(1).selectOption(cash.id);
  await page.locator('.credit').nth(1).fill('100');
  await expect(page.locator('#difference')).toHaveText('0');
  await page.locator('#save-draft').click();
  await page.waitForURL(/\/journals\/[0-9a-f-]{36}$/);
  const journalId = page.url().split('/').at(-1);
  await page.locator('#validate-journal').click();
  await expect(page.locator('#validation-valid')).toBeVisible();
  await page.locator('#post-journal').click();
  await expect(page.locator('#journal-status')).toHaveText('مرحّل');
  const journal = await get('/journals/' + journalId);
  check(
    'Posted Journal displays both UI-created account codes',
    journal.status === 'Posted' &&
      journal.lines.map((x) => x.accountCode).join(',') === '6100,1100',
  );
  await page.locator('#trial-balance-link').click();
  await page.locator('#tb-start').fill('2027-01-01');
  await page.locator('#tb-end').fill('2027-01-31');
  await page.locator('#tb-run').click();
  await expect(page.locator('#tb-reconciled')).toBeVisible();
  check(
    'Trial Balance shows both new accounts and balances',
    (await page.locator('#trial-balance-table').textContent()).includes('6100') &&
      (await page.locator('#trial-balance-table').textContent()).includes('1100'),
  );
  await page.locator('[data-account-code="6100"] a').click();
  await expect(page.locator('#gl-reconciled')).toBeVisible();
  check('GL detail reconciles expense account', (await page.locator('.gl-journal').count()) === 1);
  const trial = await get('/trial-balance?startDate=2027-01-01&endDate=2027-01-31');
  const gl = await get(
    `/general-ledger?accountId=${cash.id}&startDate=2027-01-01&endDate=2027-01-31`,
  );
  check(
    'Cash GL and trial balance agree',
    gl.periodCredit === '100' &&
      gl.closingBalance === '-100' &&
      gl.reconciliation.passed &&
      trial.reconciliation.passed,
  );

  const draft = await fetch(base + '/journals/drafts', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      postingDate: '2027-01-02',
      currency: 'JOD',
      reference: 'No header posting',
      description: 'Must reject non-postable',
      lines: [
        { id: null, accountId: header.id, debit: '10', credit: '0', description: '' },
        { id: null, accountId: cash.id, debit: '0', credit: '10', description: '' },
      ],
    }),
  });
  if (draft.status !== 200) throw new Error(`Header draft: ${draft.status}`);
  const receipt = await draft.json();
  const post = await fetch(`${base}/journals/${receipt.reference}/post`, {
    method: 'POST',
    headers: {
      ...headers,
      'If-Match': `"${receipt.appliedRevision}"`,
      'Idempotency-Key': 'header-post',
    },
    body: '{}',
  });
  check(
    'Server rejects non-postable account at Post',
    post.status === 422 && (await post.json()).errorId === 'FIN_ACCOUNT_NOT_POSTABLE',
  );

  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'دليل الحسابات' }).click();
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.locator('#account-table')).toContainText('Office Expense');
  check('English LTR account list has same accounts', (await get('/accounts')).length === 3);
  await page.locator('#account-create-code').fill('6100');
  await page.locator('#account-create-name').fill('Duplicate');
  const englishResponse = page.waitForResponse(
    (r) => r.url().endsWith('/finance/accounts') && r.request().method() === 'POST',
  );
  await page.locator('#create-account').click();
  await englishResponse;
  await expect(page.locator('[role="alert"]')).toContainText('already exists');
  check('English duplicate error remains understandable', true);
  await page.screenshot({
    path: new URL('account-setup-en.png', directory).pathname,
    fullPage: true,
  });
  check('No browser runtime errors', errors.length === 0);
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify(
      {
        status: 'PASS',
        checks,
        periodId: period.id,
        journalId,
        expenseId: expense.id,
        cashId: cash.id,
        headerId: header.id,
        trialBalance: trial.totals,
        generalLedger: { totalCount: gl.totalCount },
      },
      null,
      2,
    ),
  );
  console.log(JSON.stringify({ status: 'PASS', browserChecks: checks.length }));
} finally {
  await browser.close();
}
