import { chromium, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';

const directory = new URL('../../../artifacts/period-close/', import.meta.url);
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
async function draftAndPost(reference, date, key) {
  const draft = await fetch(base + '/journals/drafts', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      postingDate: date,
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
  const initial = await draftAndPost('Close golden', '2026-01-01', 'period-golden-post');
  check('Open period permits initial posting', initial.post.status === 200);
  const original = await get('/journals/' + initial.receipt.reference);
  const periods = await get('/periods');
  const period = periods.find((item) => item.startDate === '2026-01-01');
  check('Period begins Open', period?.status === 'Open' && period.revision === 1);
  await page.goto(origin + '/workspace');
  await page.locator('#tenant').fill(tenant);
  await page.locator('#company').fill(company);
  await page.locator('#session-token').fill(process.env.ERP_E2E_TOKEN);
  await page.getByRole('button', { name: 'فتح الجلسة', exact: true }).click();
  await page.getByRole('link', { name: 'الفترات المالية' }).click();
  await expect(page.locator('#period-table tbody tr')).toHaveCount(1);
  check('Arabic RTL period list', (await page.locator('html').getAttribute('dir')) === 'rtl');
  await page.getByRole('button', { name: 'فحص الإغلاق' }).click();
  await expect(page.locator('#period-validation')).toBeVisible();
  check(
    'Pre-close passes with posted ledger',
    (await page.locator('#period-validation').textContent()).includes('اجتازت'),
  );
  check('Explicit confirmation required', await page.locator('#confirm-period-close').isDisabled());
  await page.locator('#period-comment').fill('Golden close');
  await page.locator('#period-confirm').check();
  const keys = [];
  let committed;
  await page.route('**/periods/*/close', async (route) => {
    keys.push(route.request().headers()['idempotency-key']);
    const response = await route.fetch();
    if (response.status() !== 200) throw new Error(`Close: ${response.status()}`);
    committed = await response.json();
    await route.abort('failed');
  });
  await page.locator('#confirm-period-close').click();
  await expect(page.locator('.error,[role="alert"]')).toBeVisible();
  check('Response lost after committed close', committed?.reference === period.id);
  check('Retry freezes comment', await page.locator('#period-comment').isDisabled());
  await page.unroute('**/periods/*/close');
  const retry = page.waitForRequest((request) =>
    request.url().endsWith(`/periods/${period.id}/close`),
  );
  await page.locator('#confirm-period-close').click();
  const retryRequest = await retry;
  check(
    'Retry sends same key and comment',
    retryRequest.headers()['idempotency-key'] === keys[0] &&
      retryRequest.postDataJSON().comment === 'Golden close',
  );
  await expect(page.locator('#period-table tbody tr')).toContainText('مغلقة');
  check(
    'Closed period has no close action',
    (await page.getByRole('button', { name: 'فحص الإغلاق' }).count()) === 0,
  );
  await page.screenshot({
    path: new URL('period-close-ar.png', directory).pathname,
    fullPage: true,
  });
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.locator('#period-table')).toContainText('Closed');
  check(
    'English LTR closed period',
    (await page.locator('#period-table').textContent()).includes('Closed'),
  );
  await page.screenshot({
    path: new URL('period-close-en.png', directory).pathname,
    fullPage: true,
  });
  const closed = (await get('/periods')).find((item) => item.id === period.id);
  check(
    'Actor time comment and revision persisted',
    closed.status === 'Closed' &&
      closed.revision === 2 &&
      !!closed.closedAt &&
      closed.closeComment === 'Golden close',
  );
  const postAfter = await draftAndPost('Blocked by close', '2026-01-02', 'period-blocked-post');
  check(
    'Posting rejected after close',
    postAfter.post.status === 422 && (await postAfter.post.json()).errorId === 'FIN_PERIOD_CLOSED',
  );
  const reversal = await fetch(`${base}/journals/${original.id}/reverse`, {
    method: 'POST',
    headers: {
      ...headers,
      'If-Match': `"${original.revision}"`,
      'Idempotency-Key': 'period-blocked-reversal',
    },
    body: JSON.stringify({ postingDate: '2026-01-03', reason: 'Should fail' }),
  });
  check(
    'Reversal rejected after close',
    reversal.status === 422 && (await reversal.json()).errorId === 'FIN_REVERSAL_PERIOD_CLOSED',
  );
  const tb = await get('/trial-balance?startDate=2026-01-01&endDate=2026-01-31');
  check(
    'Closed-period Trial Balance retains exact totals',
    tb.totals.periodDebit === '100' && tb.totals.periodCredit === '100' && tb.reconciliation.passed,
  );
  const gl = await get(
    `/general-ledger?accountId=${expense}&startDate=2026-01-01&endDate=2026-01-31`,
  );
  check(
    'Closed-period GL retains original movement',
    gl.totalCount === 1 && gl.movements[0].debit === '100' && gl.reconciliation.passed,
  );
  check('No browser runtime errors', errors.length === 0);
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify(
      {
        status: 'PASS',
        checks,
        periodId: period.id,
        originalJournalId: original.id,
        closed,
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
