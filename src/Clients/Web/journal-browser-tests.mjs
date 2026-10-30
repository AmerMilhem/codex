import { chromium, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
const directory = new URL('../../../artifacts/journal/', import.meta.url);
await mkdir(directory, { recursive: true });
const browser = await chromium.launch({
  ...(process.env.POC_CHROMIUM ? { executablePath: process.env.POC_CHROMIUM } : {}),
  headless: true,
  args: ['--no-sandbox'],
});
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const checks = [],
  errors = [];
page.on('pageerror', (error) => errors.push(error.message));
function check(name, value) {
  checks.push({ name, status: value ? 'PASS' : 'FAIL' });
  if (!value) throw new Error(name);
}
const tenant = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  company = '11111111-1111-4111-8111-111111111111';
const origin = process.env.ERP_UI_ORIGIN,
  base = `${origin}/api/v1/tenants/${tenant}/companies/${company}/finance`;
const headers = { Authorization: `Bearer ${process.env.ERP_E2E_TOKEN}` };
let journalId = '',
  originalKey = '',
  originalRevision = '';
try {
  await page.goto(origin + '/workspace');
  await page.locator('#tenant').fill(tenant);
  await page.locator('#company').fill(company);
  await page.locator('#session-token').fill(process.env.ERP_E2E_TOKEN);
  await page.getByRole('button', { name: 'فتح الجلسة', exact: true }).click();
  await page.getByRole('link', { name: 'القيود اليدوية', exact: true }).click();
  await page.locator('#new-journal').click();
  await page.locator('.line-grid select').first().waitFor();
  check(
    'Journal styles load under strict CSP',
    await page.evaluate(
      () => getComputedStyle(document.querySelector('header')).display === 'flex',
    ),
  );
  check(
    'Arabic RTL journal form',
    (await page.locator('html').getAttribute('dir')) === 'rtl' &&
      (await page.locator('h1').textContent()) === 'القيود اليدوية',
  );
  await page.locator('#posting-date').fill('2026-10-18');
  await page.locator('#journal-reference').fill('Browser golden');
  await page.locator('#journal-description').fill('مصروف نقدي / Cash expense');
  await page
    .locator('.line-grid select')
    .nth(0)
    .selectOption('55555555-5555-4555-8555-555555555555');
  await page.locator('.debit').nth(0).fill('100');
  await page
    .locator('.line-grid select')
    .nth(1)
    .selectOption('66666666-6666-4666-8666-666666666666');
  await page.locator('.credit').nth(1).fill('100');
  await expect(page.locator('#difference')).toHaveText('0');
  check(
    'Exact UI totals',
    (await page.locator('#total-debit').textContent()) === '100' &&
      (await page.locator('#total-credit').textContent()) === '100',
  );
  await page.screenshot({
    path: new URL('journal-draft-rtl.png', directory).pathname,
    fullPage: true,
  });
  const creating = page.waitForResponse((r) => r.url().endsWith('/journals/drafts'));
  await page.locator('#save-draft').click();
  const created = await creating;
  check('Real API creates draft', created.status() === 200);
  await page.waitForURL(/\/journals\/[0-9a-f-]{36}$/);
  journalId = page.url().split('/').at(-1);
  await page.waitForURL(`**/journals/${journalId}`);
  await expect(page.locator('#save-draft')).toBeDisabled();
  await page.locator('#journal-reference').fill('Browser golden edited');
  await page.locator('#save-draft').click();
  await expect(page.locator('#save-draft')).toBeDisabled();
  check(
    'Draft edits round trip',
    (await page.locator('#journal-reference').inputValue()) === 'Browser golden edited',
  );
  await page.locator('#validate-journal').click();
  await page.locator('#validation-valid').waitFor();
  check(
    'Validate without posting',
    (await page.locator('#journal-status').textContent()) === 'مسودة',
  );
  let intercepted = false;
  await page.route('**/journals/*/post', async (route) => {
    const request = route.request();
    if (!intercepted) {
      intercepted = true;
      originalKey = request.headers()['idempotency-key'];
      originalRevision = request.headers()['if-match'];
      const response = await route.fetch();
      check('First posting commits before response loss', response.status() === 200);
      await route.abort('timedout');
    } else {
      check(
        'Retry preserves idempotency key and revision',
        request.headers()['idempotency-key'] === originalKey &&
          request.headers()['if-match'] === originalRevision,
      );
      await route.continue();
    }
  });
  await page.locator('#post-journal').click();
  await expect(page.locator('#post-journal')).toHaveText('إعادة محاولة الترحيل');
  check('Uncertain result locks draft editing', await page.locator('#posting-date').isDisabled());
  await page.locator('#post-journal').click();
  await expect(page.locator('#journal-status')).toHaveText('مرحّل');
  check(
    'Posted UI is immutable',
    (await page.locator('#save-draft').count()) === 0 &&
      (await page.locator('#post-journal').count()) === 0 &&
      (await page.locator('#posting-date').isDisabled()) &&
      (await page.getByText('حُفظت المسودة.', { exact: true }).count()) === 0,
  );
  check(
    'Posted number and actor displayed',
    /MJ-2026-\d{6}/.test(await page.locator('#journal-number').textContent()) &&
      (await page.locator('main').textContent()).includes('journal-browser-actor'),
  );
  check(
    'Posted account labels remain visible after reload',
    (await page.locator('.posted-account').allTextContents()).some(
      (x) => x.includes('5000') && x.includes('Expense'),
    ) &&
      (await page.locator('.posted-account').allTextContents()).some(
        (x) => x.includes('1000') && x.includes('Cash'),
      ),
  );
  const audit = await (await fetch(`${base}/journals/${journalId}/audit`, { headers })).json();
  check(
    'Traceable audit exists',
    ['JournalCreated', 'JournalUpdated', 'JournalValidated', 'JournalPosted'].every((action) =>
      audit.some((x) => x.action === action && x.requestId && x.correlationId),
    ),
  );
  const replay = await fetch(`${base}/journals/${journalId}/post`, {
    method: 'POST',
    headers: {
      ...headers,
      'Content-Type': 'application/json',
      'Idempotency-Key': originalKey,
      'If-Match': originalRevision,
    },
    body: '{}',
  });
  check(
    'Post replay returns original receipt',
    replay.status === 200 && (await replay.json()).reference === journalId,
  );
  const conflict = await fetch(`${base}/journals/${journalId}/post`, {
    method: 'POST',
    headers: {
      ...headers,
      'Content-Type': 'application/json',
      'Idempotency-Key': originalKey,
      'If-Match': '"999"',
    },
    body: '{}',
  });
  check(
    'Changed request conflicts with same key',
    conflict.status === 409 && (await conflict.json()).errorId === 'API_IDEMPOTENCY_CONFLICT',
  );
  await page.locator('#language').click();
  await expect(page.locator('#journal-status')).toHaveText('Posted');
  check('Posted English LTR', (await page.locator('html').getAttribute('dir')) === 'ltr');
  await page.locator('#language').click();
  await page.setViewportSize({ width: 390, height: 844 });
  check(
    'Posted Arabic RTL mobile layout',
    (await page.locator('html').getAttribute('dir')) === 'rtl' &&
      (await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)),
  );
  await page.screenshot({
    path: new URL('journal-posted-rtl.png', directory).pathname,
    fullPage: true,
  });
  await page.getByRole('link', { name: 'العودة إلى القيود', exact: true }).click();
  await page.locator('#reconcile').click();
  await expect(page.getByText('القيود المرحّلة متزنة.', { exact: true })).toBeVisible();
  check(
    'Posted list and reconciliation',
    (await page.getByRole('link', { name: /MJ-2026-/ }).count()) === 1,
  );
  check('No browser runtime errors', errors.length === 0);
} finally {
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify(
      {
        utc: new Date().toISOString(),
        browser: await browser.version(),
        journalId,
        checks,
        errors,
      },
      null,
      2,
    ),
  );
  await browser.close();
}
