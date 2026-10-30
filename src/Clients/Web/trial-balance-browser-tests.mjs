import { chromium, expect } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
const directory = new URL('../../../artifacts/trial-balance/', import.meta.url);
await mkdir(directory, { recursive: true });
const recheck = process.env.ERP_TRIAL_RECHECK_ONLY === 'true';
const previous = recheck
  ? JSON.parse(await readFile(new URL('browser-results.json', directory), 'utf8'))
  : null;
const checks = previous?.checks ?? [],
  errors = previous?.errors ?? [],
  snapshots = previous?.snapshots ?? {};
const browser = await chromium.launch({
  ...(process.env.POC_CHROMIUM ? { executablePath: process.env.POC_CHROMIUM } : {}),
  headless: true,
  args: ['--no-sandbox'],
});
const page = await browser.newPage({ viewport: { width: 1680, height: 1100 } });
page.on('pageerror', (e) => errors.push(e.message));
const pageFinanceMethods = [];
page.on('request', (request) => {
  if (request.url().includes('/finance/')) pageFinanceMethods.push(request.method());
});
const origin = process.env.ERP_UI_ORIGIN;
const tenant = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  company = '11111111-1111-4111-8111-111111111111';
const expense = '55555555-5555-4555-8555-555555555555',
  cash = '66666666-6666-4666-8666-666666666666',
  revenue = '77777777-7777-4777-8777-777777777777';
const base = `${origin}/api/v1/tenants/${tenant}/companies/${company}/finance`;
const headers = {
  Authorization: `Bearer ${process.env.ERP_E2E_TOKEN}`,
  'Content-Type': 'application/json',
};
function check(name, ok) {
  checks.push({ name, status: ok ? 'PASS' : 'FAIL' });
  if (!ok) throw new Error(name);
}
async function journal(date, debitAccount, creditAccount, value, post = true) {
  const created = await fetch(base + '/journals/drafts', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      postingDate: date,
      currency: 'JOD',
      reference: 'TB golden',
      description: '',
      lines: [
        { id: null, accountId: debitAccount, debit: value, credit: '0', description: '' },
        { id: null, accountId: creditAccount, debit: '0', credit: value, description: '' },
      ],
    }),
  });
  if (created.status !== 200) throw new Error('Golden fixture draft creation failed');
  const receipt = await created.json();
  if (post) {
    const response = await fetch(`${base}/journals/${receipt.reference}/post`, {
      method: 'POST',
      headers: {
        ...headers,
        'If-Match': `"${receipt.appliedRevision}"`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      body: '{}',
    });
    if (response.status !== 200) throw new Error('Golden fixture posting failed');
  }
}
async function run(start, end) {
  await page.locator('#tb-start').fill(start);
  await page.locator('#tb-end').fill(end);
  const response = page.waitForResponse((r) => r.url().includes('/trial-balance?'));
  await page.locator('#tb-run').click();
  const result = await response;
  if (result.status() !== 200) throw new Error('Report request failed');
  await page.locator('#tb-reconciled').waitFor();
  // Verify controls through an additional real read; browser response bodies can be evicted during rapid renders.
  // UI figures are independently asserted below. No response or financial data is mocked.
  const control = await fetch(
    `${base}/trial-balance?${new URLSearchParams({ startDate: start, endDate: end })}`,
    { headers },
  );
  if (control.status !== 200) throw new Error('Independent acceptance report read failed');
  return await control.json();
}
async function cells(selector) {
  return await page.locator(selector + ' td[dir="ltr"]').allTextContents();
}
try {
  if (!recheck) {
    await journal('2026-01-01', expense, cash, '100');
    await journal('2026-01-31', cash, revenue, '40');
    await journal('2026-01-20', expense, cash, '999', false);
  }
  await page.goto(origin + '/workspace');
  await page.locator('#tenant').fill(tenant);
  await page.locator('#company').fill(company);
  await page.locator('#session-token').fill(process.env.ERP_E2E_TOKEN);
  await page.getByRole('button', { name: 'فتح الجلسة', exact: true }).click();
  await page.getByRole('link', { name: 'ميزان المراجعة', exact: true }).click();
  await expect(page.locator('h1')).toHaveText('ميزان المراجعة');
  if (!recheck) {
    check(
      'Arabic RTL report and CSP styles',
      (await page.locator('html').getAttribute('dir')) === 'rtl' &&
        (await page.evaluate(
          () => getComputedStyle(document.querySelector('header')).display === 'flex',
        )),
    );
    const january = await run('2026-01-01', '2026-01-31');
    snapshots.january = january;
    check(
      'Draft excluded and posted journals included',
      january.ledgerControl.postedJournalCount === 2 && january.ledgerControl.postedLineCount === 4,
    );
    check(
      'Golden grand movement and closing totals',
      JSON.stringify(await cells('#tb-grand-totals')) ===
        JSON.stringify(['0', '0', '140', '140', '100', '100']),
    );
    check(
      'Expense aggregation',
      JSON.stringify(await cells('[data-account-code="5000"]')) ===
        JSON.stringify(['0', '0', '100', '0', '100', '0']),
    );
    check(
      'Cash aggregation',
      JSON.stringify(await cells('[data-account-code="1000"]')) ===
        JSON.stringify(['0', '0', '40', '100', '0', '60']),
    );
    check(
      'Revenue aggregation',
      JSON.stringify(await cells('[data-account-code="4000"]')) ===
        JSON.stringify(['0', '0', '0', '40', '0', '40']),
    );
    check(
      'Independent reconciliation passes',
      january.reconciliation.passed && january.reconciliation.checks.every((c) => c.passed),
    );
    await page.screenshot({
      path: new URL('trial-balance-ar.png', directory).pathname,
      fullPage: true,
    });
    const arabicNumbers = await cells('#trial-balance-table');
    await page.locator('#language').click();
    await expect(page.locator('h1')).toHaveText('Trial balance');
    check(
      'English LTR preserves exact figures',
      (await page.locator('html').getAttribute('dir')) === 'ltr' &&
        JSON.stringify(await cells('#trial-balance-table')) === JSON.stringify(arabicNumbers),
    );
    await page.screenshot({
      path: new URL('trial-balance-en.png', directory).pathname,
      fullPage: true,
    });
    await journal('2026-02-01', expense, cash, '20');
    const unchanged = await run('2026-01-01', '2026-01-31');
    check(
      'Later fiscal period does not affect earlier report',
      JSON.stringify(unchanged.totals) === JSON.stringify(january.totals),
    );
    const february = await run('2026-02-01', '2026-02-28');
    snapshots.february = february;
    check(
      'Next period opening movement closing',
      JSON.stringify(await cells('#tb-grand-totals')) ===
        JSON.stringify(['100', '100', '20', '20', '120', '120']),
    );
    await journal('2025-12-31', expense, cash, '10');
    const prior = await run('2026-01-01', '2026-01-31');
    snapshots.withPriorYear = prior;
    check(
      'Prior fiscal year contributes to opening',
      JSON.stringify(await cells('#tb-grand-totals')) ===
        JSON.stringify(['10', '10', '140', '140', '110', '110']),
    );
    const empty = await run('2024-01-01', '2024-01-31');
    check(
      'Empty range has zero totals and visible empty state',
      empty.accounts.length === 0 &&
        (await page.locator('#tb-empty').count()) === 1 &&
        (await cells('#tb-grand-totals')).every((x) => x === '0'),
    );
    await run('2026-01-01', '2026-01-31');
    await page.locator('#language').click();
    await page.setViewportSize({ width: 390, height: 844 });
    check(
      'Arabic RTL mobile report contains horizontal grid',
      (await page.locator('html').getAttribute('dir')) === 'rtl' &&
        (await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)),
    );
    await page.screenshot({
      path: new URL('trial-balance-mobile-ar.png', directory).pathname,
      fullPage: true,
    });
    check(
      'Report offers no accounting mutations; account links target read-only GL',
      (await page.locator('#trial-balance-table input, #trial-balance-table button').count()) ===
        0 && (await page.locator('#trial-balance-table a[href*="general-ledger"]').count()) === 3,
    );
    const foreign = await fetch(
      base.replace(company, '22222222-2222-4222-8222-222222222222') +
        '/trial-balance?startDate=2026-01-01&endDate=2026-01-31',
      { headers },
    );
    check('Unauthorized company rejected by server', foreign.status === 403);
    const foreignTenant = await fetch(
      base.replace(tenant, 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb') +
        '/trial-balance?startDate=2026-01-01&endDate=2026-01-31',
      { headers },
    );
    check('Unauthorized tenant rejected by server', foreignTenant.status === 403);
  } else {
    const closed = await run('2026-01-01', '2026-01-31');
    check(
      'Closed fiscal period is readable',
      closed.reconciliation.passed && closed.totals.periodDebit === '140',
    );
    const again = await run('2026-01-01', '2026-01-31');
    check(
      'Refreshing closed-period report preserves all figures',
      JSON.stringify(closed.accounts) === JSON.stringify(again.accounts) &&
        JSON.stringify(closed.totals) === JSON.stringify(again.totals),
    );
  }
  check(
    recheck
      ? 'Closed-period screen sends only GET requests'
      : 'Report screen sends only GET requests',
    pageFinanceMethods.length > 0 && pageFinanceMethods.every((method) => method === 'GET'),
  );
  check(
    recheck ? 'No runtime errors after closed-period refresh' : 'No report runtime errors',
    errors.length === 0,
  );
} finally {
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify(
      {
        utc: new Date().toISOString(),
        browser: await browser.version(),
        checks,
        errors,
        snapshots,
      },
      null,
      2,
    ),
  );
  await browser.close();
}
