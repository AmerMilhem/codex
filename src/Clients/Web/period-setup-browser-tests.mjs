import { chromium, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';

const directory = new URL('../../../artifacts/period-setup/', import.meta.url);
await mkdir(directory, { recursive: true });
const origin = process.env.ERP_UI_ORIGIN;
const tenant = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const company = '11111111-1111-4111-8111-111111111111';
const expense = '55555555-5555-4555-8555-555555555555';
const cash = '66666666-6666-4666-8666-666666666666';
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
async function draftAndPost(reference, key) {
  const draft = await fetch(base + '/journals/drafts', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      postingDate: '2027-01-01',
      currency: 'JOD',
      reference,
      description: reference,
      lines: [
        { id: null, accountId: expense, debit: '100', credit: '0', description: 'Expense' },
        { id: null, accountId: cash, debit: '0', credit: '100', description: 'Cash' },
      ],
    }),
  });
  if (draft.status !== 200) throw new Error(`Draft: ${draft.status}`);
  const receipt = await draft.json();
  const post = await fetch(`${base}/journals/${receipt.reference}/post`, {
    method: 'POST',
    headers: { ...headers, 'If-Match': `"${receipt.appliedRevision}"`, 'Idempotency-Key': key },
    body: '{}',
  });
  return { receipt, post };
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
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await expect(page.locator('#period-empty')).toBeVisible();
  check('Company begins without SQL-seeded periods', (await get('/periods')).length === 0);
  check(
    'Arabic RTL setup form',
    (await page.locator('html').getAttribute('dir')) === 'rtl' &&
      (await page.locator('#period-create-form').textContent()).includes('إنشاء'),
  );
  await page.locator('#period-create-start').fill('2027-01-01');
  await page.locator('#period-create-end').fill('2027-01-31');
  const keys = [];
  let committed;
  await page.route('**/finance/periods', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    keys.push(route.request().headers()['idempotency-key']);
    const response = await route.fetch();
    if (response.status() !== 200) throw new Error(`Create: ${response.status()}`);
    committed = await response.json();
    await route.abort('failed');
  });
  await page.locator('#create-period').click();
  await expect(page.locator('[role="alert"]')).toBeVisible();
  check('Response lost after real creation commit', !!committed?.reference);
  check(
    'Uncertain create freezes both dates',
    (await page.locator('#period-create-start').isDisabled()) &&
      (await page.locator('#period-create-end').isDisabled()),
  );
  await page.unroute('**/finance/periods');
  const retry = page.waitForRequest(
    (request) => request.url().endsWith('/finance/periods') && request.method() === 'POST',
  );
  await page.locator('#create-period').click();
  const request = await retry;
  check(
    'Retry reuses key and exact dates',
    request.headers()['idempotency-key'] === keys[0] &&
      request.postDataJSON().startDate === '2027-01-01' &&
      request.postDataJSON().endDate === '2027-01-31',
  );
  await expect(page.locator('#period-table tbody tr')).toHaveCount(1);
  const period = (await get('/periods'))[0];
  check(
    'New period is Open with actor/time',
    period.id === committed.reference &&
      period.status === 'Open' &&
      period.revision === 1 &&
      !!period.createdAt &&
      !!period.createdBySubject,
  );
  await page.screenshot({
    path: new URL('period-setup-ar.png', directory).pathname,
    fullPage: true,
  });
  await page.locator('#period-create-start').fill('2027-02-01');
  await page.locator('#period-create-end').fill('2027-01-31');
  await page.locator('#create-period').click();
  await expect(page.locator('[role="alert"]')).toContainText('اختر تاريخ بداية ونهاية صالحين');
  check(
    'Invalid order is explained in Arabic without creating a period',
    (await get('/periods')).length === 1,
  );
  async function rejectedCreate(startDate, endDate, errorId, message) {
    await page.locator('#period-create-start').fill(startDate);
    await page.locator('#period-create-end').fill(endDate);
    const response = page.waitForResponse(
      (r) => r.url().endsWith('/finance/periods') && r.request().method() === 'POST',
    );
    await page.locator('#create-period').click();
    const result = await response;
    await expect(page.locator('[role="alert"]')).toContainText(message);
    return result.status() === 409 && (await result.json()).errorId === errorId;
  }
  check(
    'Exact duplicate is rejected with Arabic UI message',
    await rejectedCreate('2027-01-01', '2027-01-31', 'FIN_PERIOD_DUPLICATE', 'توجد فترة مالية'),
  );
  check(
    'Mid-range overlap is rejected',
    await rejectedCreate('2027-01-15', '2027-02-15', 'FIN_PERIOD_OVERLAP', 'تتداخل هذه التواريخ'),
  );
  check(
    'Inclusive end boundary overlap is rejected',
    await rejectedCreate('2027-01-31', '2027-02-28', 'FIN_PERIOD_OVERLAP', 'تتداخل هذه التواريخ'),
  );
  const unauthorized = await fetch(
    `${process.env.ERP_LIMITED_API_ORIGIN}/api/v1/tenants/${tenant}/companies/${company}/finance/periods`,
    {
      method: 'POST',
      headers,
      body: JSON.stringify({ startDate: '2027-02-01', endDate: '2027-02-28' }),
    },
  );
  check(
    'Authenticated user lacking Create is denied at API boundary',
    unauthorized.status === 403 && (await unauthorized.json()).errorId === 'AUTH_SCOPE_DENIED',
  );
  await page.locator('#fiscal-periods a').first().click();
  await page.locator('#new-journal').click();
  await page.locator('.line-grid select').first().waitFor();
  await page.locator('#posting-date').fill('2027-01-01');
  await page.locator('#journal-reference').fill('Setup golden');
  await page.locator('#journal-description').fill('Created-period journal');
  await page.locator('.line-grid select').nth(0).selectOption(expense);
  await page.locator('.debit').nth(0).fill('100');
  await page.locator('.line-grid select').nth(1).selectOption(cash);
  await page.locator('.credit').nth(1).fill('100');
  await expect(page.locator('#difference')).toHaveText('0');
  await page.locator('#save-draft').click();
  await page.waitForURL(/\/journals\/[0-9a-f-]{36}$/);
  const journalId = page.url().split('/').at(-1);
  await page.locator('#validate-journal').click();
  await expect(page.locator('#validation-valid')).toBeVisible();
  check(
    'Journal validates in newly created period without posting',
    (await page.locator('#journal-status').textContent()) === 'مسودة',
  );
  await page.locator('#post-journal').click();
  await expect(page.locator('#journal-status')).toHaveText('مرحّل');
  const original = await get('/journals/' + journalId);
  check(
    'Created period accepts UI posting',
    original.status === 'Posted' && original.postingDate === '2027-01-01',
  );
  await page.locator('#reverse-journal').click();
  await page.locator('#reversal-date').fill('2027-01-02');
  await page.locator('#reversal-reason').fill('Setup golden correction');
  await page.locator('#reversal-confirm').check();
  await page.locator('#confirm-reversal').click();
  await expect(page.locator('#reversal-relationship')).toBeVisible();
  check(
    'Created Open period accepts UI reversal',
    (await page.locator('#reversal-relationship').textContent()).includes('2027-01-02'),
  );
  await page.locator('#trial-balance-link').click();
  await page.locator('#tb-start').fill('2027-01-01');
  await page.locator('#tb-end').fill('2027-01-31');
  await page.locator('#tb-run').click();
  await expect(page.locator('#tb-reconciled')).toBeVisible();
  check(
    'Trial Balance UI reconciles posting and reversal',
    (await page.locator('#trial-balance-table').textContent()).includes('200'),
  );
  await page.locator('[data-account-code="5000"] a').click();
  await expect(page.locator('#gl-reconciled')).toBeVisible();
  check('GL UI shows both posted movements', (await page.locator('.gl-journal').count()) === 2);
  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await page.getByRole('button', { name: 'فحص الإغلاق' }).click();
  await expect(page.locator('#period-validation')).toContainText('اجتازت');
  await page.locator('#period-confirm').check();
  await page.locator('#confirm-period-close').click();
  await expect(page.locator('#period-table tbody tr')).toContainText('مغلقة');
  check(
    'Existing Close command closes created period',
    (await get('/periods'))[0].status === 'Closed',
  );
  const later = await draftAndPost('Blocked after setup close', 'setup-blocked-post');
  check(
    'Closed period rejects later post',
    later.post.status === 422 && (await later.post.json()).errorId === 'FIN_PERIOD_CLOSED',
  );
  const tb = await get('/trial-balance?startDate=2027-01-01&endDate=2027-01-31');
  check(
    'Trial Balance includes both posted effects',
    tb.totals.periodDebit === '200' && tb.totals.periodCredit === '200' && tb.reconciliation.passed,
  );
  const gl = await get(
    `/general-ledger?accountId=${expense}&startDate=2027-01-01&endDate=2027-01-31`,
  );
  check('GL retains posting and reversal', gl.totalCount === 2 && gl.reconciliation.passed);
  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'ميزان المراجعة' }).click();
  await page.locator('#tb-start').fill('2027-01-01');
  await page.locator('#tb-end').fill('2027-01-31');
  await page.locator('#tb-run').click();
  await expect(page.locator('#tb-reconciled')).toBeVisible();
  check(
    'Closed-period report remains readable in UI',
    (await page.locator('#trial-balance-table').textContent()).includes('200'),
  );
  await page.locator('a[href="/workspace"]').click();
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.locator('#period-table')).toContainText('Closed');
  check(
    'English LTR setup/closed list',
    (await page.locator('#period-create-form').textContent()).includes('Create period'),
  );
  await page.locator('#period-create-start').fill('2027-02-01');
  await page.locator('#period-create-end').fill('2027-01-31');
  await page.locator('#create-period').click();
  await expect(page.locator('[role="alert"]')).toContainText('Choose valid dates');
  check(
    'English invalid-date message is understandable',
    (await page.locator('[role="alert"]').textContent()).includes('Choose valid dates'),
  );
  await page.screenshot({
    path: new URL('period-setup-en.png', directory).pathname,
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
        originalJournalId: original.id,
        trialBalance: tb.totals,
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
