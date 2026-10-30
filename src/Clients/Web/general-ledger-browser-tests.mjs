import { chromium, expect } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
const directory = new URL('../../../artifacts/general-ledger/', import.meta.url);
await mkdir(directory, { recursive: true });
const recheck = process.env.ERP_GL_RECHECK_ONLY === 'true';
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
const methods = [];
page.on('request', (r) => {
  if (r.url().includes('/finance/')) methods.push(r.method());
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
async function journal(date, d, c, amount, post = true) {
  const created = await fetch(base + '/journals/drafts', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      postingDate: date,
      currency: 'JOD',
      reference: 'GL golden',
      description: 'General ledger acceptance',
      lines: [
        { id: null, accountId: d, debit: amount, credit: '0', description: 'Debit line' },
        { id: null, accountId: c, debit: '0', credit: amount, description: 'Credit line' },
      ],
    }),
  });
  if (created.status !== 200) throw new Error('Draft setup failed');
  const receipt = await created.json();
  if (post) {
    const r = await fetch(`${base}/journals/${receipt.reference}/post`, {
      method: 'POST',
      headers: {
        ...headers,
        'If-Match': `"${receipt.appliedRevision}"`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      body: '{}',
    });
    if (r.status !== 200) throw new Error('Posting setup failed');
  }
  return receipt.reference;
}
async function read(offset = 0, limit = 50) {
  const r = await fetch(
    `${base}/general-ledger?${new URLSearchParams({ accountId: cash, startDate: '2026-01-01', endDate: '2026-01-31', offset: String(offset), limit: String(limit) })}`,
    { headers },
  );
  if (r.status !== 200) throw new Error('GL read failed');
  return r.json();
}
async function refresh() {
  await page.locator('#gl-run').click();
  await expect(page.locator('#gl-reconciled')).toBeVisible();
}
try {
  if (!recheck) {
    await journal('2025-12-31', cash, revenue, '100');
    snapshots.firstJournal = await journal('2026-01-01', cash, revenue, '40');
    snapshots.secondJournal = await journal('2026-01-31', expense, cash, '25');
    await journal('2026-01-15', cash, revenue, '999', false);
    await journal('2026-02-01', cash, revenue, '800');
  }
  await page.goto(origin + '/workspace');
  await page.locator('#tenant').fill(tenant);
  await page.locator('#company').fill(company);
  await page.locator('#session-token').fill(process.env.ERP_E2E_TOKEN);
  await page.getByRole('button', { name: 'فتح الجلسة', exact: true }).click();
  await page.getByRole('link', { name: 'ميزان المراجعة', exact: true }).click();
  await page.locator('#tb-start').fill('2026-01-01');
  await page.locator('#tb-end').fill('2026-01-31');
  await page.locator('#tb-run').click();
  await expect(page.locator('#tb-reconciled')).toBeVisible();
  check(
    recheck
      ? 'Closed period Trial Balance remains readable'
      : 'Trial Balance cash closing is 115 debit',
    (await page.locator('[data-account-code="1000"] td[dir="ltr"]').allTextContents()).join('|') ===
      '100|0|40|25|115|0',
  );
  await page.locator('[data-account-code="1000"] a').click();
  await expect(page.locator('#gl-reconciled')).toBeVisible();
  await expect(page.locator('#gl-opening')).toHaveText('100');
  await expect(page.locator('#gl-closing')).toHaveText('115');
  if (!recheck) {
    check(
      'Trial Balance account drill-down preserves company account dates',
      page
        .url()
        .includes(
          `/finance/${tenant}/${company}/general-ledger?accountId=${cash}&startDate=2026-01-01&endDate=2026-01-31`,
        ),
    );
    check(
      'Arabic RTL GL screen',
      (await page.locator('html').getAttribute('dir')) === 'rtl' &&
        (await page.locator('h1').textContent()).includes('دفتر الأستاذ'),
    );
    const golden = await read();
    snapshots.golden = golden;
    check(
      'Posted only; draft and after-end excluded',
      golden.totalCount === 2 &&
        golden.movements.map((x) => x.journalId).join('|') ===
          [snapshots.firstJournal, snapshots.secondJournal].join('|'),
    );
    check(
      'Opening movement running closing Golden Scenario',
      golden.openingBalance === '100' &&
        golden.periodDebit === '40' &&
        golden.periodCredit === '25' &&
        golden.closingBalance === '115' &&
        golden.movements.map((x) => x.runningBalance).join('|') === '140|115',
    );
    check(
      'Independent Trial Balance reconciliation',
      golden.reconciliation.passed && golden.reconciliation.checks.every((c) => c.passed),
    );
    check(
      'UI shows server running balances',
      (await page.locator('.gl-running').allTextContents()).join('|') === '140|115',
    );
    await page.screenshot({
      path: new URL('general-ledger-ar.png', directory).pathname,
      fullPage: true,
    });
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await expect(page.locator('h1')).toHaveText('General Ledger Detail');
    check(
      'English LTR retains identical financial figures',
      (await page.locator('html').getAttribute('dir')) === 'ltr' &&
        (await page.locator('.gl-running').allTextContents()).join('|') === '140|115' &&
        (await page.locator('#gl-closing').textContent()) === '115',
    );
    await page.screenshot({
      path: new URL('general-ledger-en.png', directory).pathname,
      fullPage: true,
    });
    await page.locator('#gl-size').selectOption('1');
    await refresh();
    await expect(page.locator('.gl-running')).toHaveText('140');
    await page.locator('#gl-next').click();
    await expect(page.locator('.gl-running')).toHaveText('115');
    check(
      'Last page running includes first page',
      (await page.locator('#gl-next').isDisabled()) &&
        (await page.locator('#gl-debit').textContent()) === '40' &&
        (await page.locator('#gl-credit').textContent()) === '25',
    );
    await page.locator('#gl-prev').click();
    await expect(page.locator('.gl-running')).toHaveText('140');
    check(
      'First page returns same opening/running',
      (await page.locator('#gl-prev').isDisabled()) &&
        (await page.locator('#gl-opening').textContent()) === '100',
    );
    await page.locator('.gl-journal').click();
    await expect(page.locator('.audit-grid')).toBeVisible();
    check(
      'GL opens existing Posted Journal with audit correlation',
      page.url().includes(`/journals/${snapshots.firstJournal}?glAccount=${cash}`) &&
        (await page.locator('.audit-grid').textContent()).includes('JournalPosted') &&
        (await page.locator('.audit-grid').textContent()).includes('general-ledger-browser-actor'),
    );
    check(
      'Posted Journal has no mutation buttons',
      (await page.locator('#save-draft,#post-journal,#validate-journal').count()) === 0,
    );
    await page.locator('#journal-gl-back').click();
    await expect(page.locator('.gl-running')).toHaveText('140');
    check(
      'Journal return preserves account dates and pagination',
      page.url().includes('offset=0&limit=1'),
    );
    const p1 = await read(0, 1),
      p2 = await read(1, 1),
      full = await read(0, 200);
    check(
      'Different page sizes retain exact movements and totals',
      JSON.stringify([...p1.movements, ...p2.movements]) === JSON.stringify(full.movements) &&
        p2.pageOpeningBalance === '140' &&
        p1.closingBalance === full.closingBalance,
    );
    await page.locator('#gl-start').fill('2024-01-01');
    await page.locator('#gl-end').fill('2024-01-31');
    await refresh();
    await expect(page.locator('#gl-empty')).toBeVisible();
    check(
      'Empty account period returns zeros',
      (await page.locator('#gl-closing').textContent()) === '0',
    );
    await page.locator('#gl-start').fill('2026-01-01');
    await page.locator('#gl-end').fill('2026-01-31');
    await refresh();
    await page.getByRole('button', { name: 'العربية', exact: true }).click();
    await page.setViewportSize({ width: 390, height: 844 });
    check(
      'Arabic RTL mobile has contained horizontal scrolling',
      (await page.locator('html').getAttribute('dir')) === 'rtl' &&
        (await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)),
    );
    await page.screenshot({
      path: new URL('general-ledger-mobile-ar.png', directory).pathname,
      fullPage: true,
    });
    const foreign = await fetch(
      base.replace(company, '22222222-2222-4222-8222-222222222222') +
        `/general-ledger?accountId=${cash}&startDate=2026-01-01&endDate=2026-01-31`,
      { headers },
    );
    check('Foreign company rejected', foreign.status === 403);
    const invalid = await fetch(
      base +
        `/general-ledger?accountId=${crypto.randomUUID()}&startDate=2026-01-01&endDate=2026-01-31`,
      { headers },
    );
    check('Modified account identifier cannot disclose movements', invalid.status === 404);
  } else {
    await refresh();
    check(
      'Closed period GL remains readable',
      (await page.locator('#gl-closing').textContent()) === '115',
    );
  }
  check(
    recheck
      ? 'Closed-period report requests are GET only'
      : 'Report and drill-down browser requests are GET only',
    methods.every((m) => m === 'GET'),
  );
  check(
    recheck ? 'Closed-period browser has no runtime errors' : 'Browser has no runtime errors',
    errors.length === 0,
  );
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify({ status: 'PASS', checks, errors, snapshots }, null, 2),
  );
  console.log(JSON.stringify({ status: 'PASS', checks: checks.length }));
} finally {
  await browser.close();
}
