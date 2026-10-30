import { chromium, expect } from '@playwright/test';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
const directory = new URL('../../../artifacts/reversal/', import.meta.url);
await mkdir(directory, { recursive: true });
const origin = process.env.ERP_UI_ORIGIN,
  tenant = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  company = '11111111-1111-4111-8111-111111111111',
  expense = '55555555-5555-4555-8555-555555555555',
  cash = '66666666-6666-4666-8666-666666666666';
const base = `${origin}/api/v1/tenants/${tenant}/companies/${company}/finance`,
  headers = {
    Authorization: `Bearer ${process.env.ERP_E2E_TOKEN}`,
    'Content-Type': 'application/json',
  };
async function get(path) {
  const r = await fetch(base + path, { headers });
  if (r.status !== 200) throw new Error(`GET ${path}: ${r.status}`);
  return r.json();
}
if (process.env.ERP_REVERSAL_PHASE === 'prepare') {
  const draft = await fetch(base + '/journals/drafts', {
    method: 'POST',
    headers,
    body: JSON.stringify({
      postingDate: '2026-01-01',
      currency: 'JOD',
      reference: 'Reversal golden',
      description: 'Original remains immutable',
      lines: [
        { id: null, accountId: expense, debit: '100', credit: '0', description: 'Expense' },
        { id: null, accountId: cash, debit: '0', credit: '100', description: 'Cash' },
      ],
    }),
  });
  if (draft.status !== 200) throw new Error('Draft failed');
  const receipt = await draft.json();
  const post = await fetch(`${base}/journals/${receipt.reference}/post`, {
    method: 'POST',
    headers: {
      ...headers,
      'If-Match': `"${receipt.appliedRevision}"`,
      'Idempotency-Key': 'golden-original-post',
    },
    body: '{}',
  });
  if (post.status !== 200) throw new Error('Posting failed');
  await writeFile(
    new URL('setup.json', directory),
    JSON.stringify(
      { originalId: receipt.reference, original: await get('/journals/' + receipt.reference) },
      null,
      2,
    ),
  );
  console.log('Original posted in open period A.');
} else {
  const setup = JSON.parse(await readFile(new URL('setup.json', directory), 'utf8'));
  const checks = [],
    errors = [],
    snapshots = {};
  function check(name, ok) {
    checks.push({ name, status: ok ? 'PASS' : 'FAIL' });
    if (!ok) throw new Error(name);
  }
  const browser = await chromium.launch({
    ...(process.env.POC_CHROMIUM ? { executablePath: process.env.POC_CHROMIUM } : {}),
    headless: true,
    args: ['--no-sandbox'],
  });
  const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } });
  page.on('pageerror', (e) => errors.push(e.message));
  try {
    await page.goto(origin + '/workspace');
    await page.locator('#tenant').fill(tenant);
    await page.locator('#company').fill(company);
    await page.locator('#session-token').fill(process.env.ERP_E2E_TOKEN);
    await page.getByRole('button', { name: 'فتح الجلسة', exact: true }).click();
    await page.getByRole('link', { name: 'القيود اليدوية', exact: true }).click();
    await page.getByRole('link', { name: setup.original.journalNumber, exact: true }).click();
    await expect(page.locator('#reverse-journal')).toBeVisible();
    check(
      'Arabic RTL eligible reversal action',
      (await page.locator('html').getAttribute('dir')) === 'rtl',
    );
    await page.locator('#reverse-journal').click();
    await page.locator('#reversal-date').fill('2026-02-01');
    await page.locator('#reversal-reason').fill('Correction');
    check(
      'Explicit confirmation is required',
      await page.locator('#confirm-reversal').isDisabled(),
    );
    await page.locator('#reversal-confirm').check();
    const seenKeys = [];
    let committed;
    await page.route('**/reverse', async (route) => {
      seenKeys.push(route.request().headers()['idempotency-key']);
      const r = await route.fetch();
      if (r.status() !== 200) throw new Error('Reversal did not commit');
      committed = await r.json();
      await route.abort('failed');
    });
    await page.locator('#confirm-reversal').click();
    await expect(page.locator('.error')).toBeVisible();
    check('Response lost only after server commit', !!committed?.reference);
    check(
      'Pending retry freezes date and reason',
      (await page.locator('#reversal-date').isDisabled()) &&
        (await page.locator('#reversal-reason').isDisabled()),
    );
    const committedOriginal = await get('/journals/' + setup.originalId);
    check(
      'Relationship exists despite lost response',
      committedOriginal.reversal.reversalJournalId === committed.reference,
    );
    await page.unroute('**/reverse');
    const retry = page.waitForRequest((r) => r.url().endsWith('/reverse'));
    await page.locator('#confirm-reversal').click();
    const retryRequest = await retry;
    await expect(page.locator('#reversal-relationship')).toBeVisible();
    check(
      'UI retry reuses identical key and payload',
      retryRequest.headers()['idempotency-key'] === seenKeys[0] &&
        retryRequest.postDataJSON().postingDate === '2026-02-01' &&
        retryRequest.postDataJSON().reason === 'Correction',
    );
    const original = await get('/journals/' + setup.originalId),
      reversal = await get('/journals/' + committed.reference);
    snapshots.original = original;
    snapshots.reversal = reversal;
    const old = { ...original, reversal: null };
    check(
      'Original unchanged including status date revision lines',
      JSON.stringify(old) === JSON.stringify({ ...setup.original, reversal: null }),
    );
    check(
      'Independent posted reversal and number',
      reversal.id !== original.id &&
        reversal.journalNumber !== original.journalNumber &&
        reversal.status === 'Posted' &&
        original.status === 'Posted',
    );
    check(
      'Exact opposite stored lines',
      original.lines.every(
        (l, i) =>
          l.accountId === reversal.lines[i].accountId &&
          l.debit === reversal.lines[i].credit &&
          l.credit === reversal.lines[i].debit,
      ),
    );
    check(
      'Reverse action disappears after success',
      (await page.locator('#reverse-journal').count()) === 0,
    );
    check(
      'Reason and independent date shown',
      (await page.locator('#reversal-relationship').textContent()).includes('Correction') &&
        (await page.locator('#reversal-relationship').textContent()).includes('2026-02-01'),
    );
    await page.screenshot({ path: new URL('reversal-ar.png', directory).pathname, fullPage: true });
    await page.locator('#reversal-link').click();
    await expect(page.locator('#journal-status')).toHaveText('مرحّل');
    check(
      'Original to reversal uses existing immutable screen',
      page.url().endsWith('/journals/' + reversal.id) &&
        (await page.locator('#reverse-journal,#save-draft,#post-journal').count()) === 0,
    );
    await page.locator('#original-link').click();
    await expect(page.locator('.audit-grid')).toBeVisible();
    check(
      'Reversal to original and audit reason correlation',
      (await page.locator('.audit-grid').textContent()).includes('JournalReversalRequested') &&
        (await page.locator('.audit-grid').textContent()).includes('JournalReversed') &&
        (await page.locator('.audit-grid').textContent()).includes('Correction'),
    );
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('#reversal-relationship')).toContainText('Reversal relationship');
    check(
      'English LTR relationship',
      (await page.locator('html').getAttribute('dir')) === 'ltr' &&
        (await page.locator('#reversal-relationship').textContent()).includes(
          'Reversal relationship',
        ),
    );
    await page.screenshot({ path: new URL('reversal-en.png', directory).pathname, fullPage: true });
    async function tb(start, end) {
      return get(`/trial-balance?startDate=${start}&endDate=${end}`);
    }
    const a = await tb('2026-01-01', '2026-01-31'),
      b = await tb('2026-02-01', '2026-02-28'),
      combined = await tb('2026-01-01', '2026-02-28');
    snapshots.trialBalance = { a, b, combined };
    const expenseA = a.accounts.find((x) => x.accountId === expense).amounts,
      expenseB = b.accounts.find((x) => x.accountId === expense).amounts;
    check(
      'Closed period A contains original movement only',
      expenseA.periodDebit === '100' &&
        expenseA.periodCredit === '0' &&
        expenseA.closingDebit === '100',
    );
    check(
      'Open period B contains reversal with original opening',
      expenseB.openingDebit === '100' &&
        expenseB.periodDebit === '0' &&
        expenseB.periodCredit === '100' &&
        expenseB.closingDebit === '0',
    );
    check(
      'Combined balances net zero without hiding either journal',
      combined.totals.periodDebit === '200' &&
        combined.totals.periodCredit === '200' &&
        combined.totals.closingDebit === '0' &&
        combined.totals.closingCredit === '0' &&
        combined.ledgerControl.postedJournalCount === 2 &&
        combined.reconciliation.passed,
    );
    const gl = await get(
      `/general-ledger?accountId=${expense}&startDate=2026-01-01&endDate=2026-02-28`,
    );
    snapshots.generalLedger = gl;
    check(
      'GL preserves both movements and running 100 then 0',
      gl.totalCount === 2 &&
        gl.movements.map((x) => x.runningBalance).join('|') === '100|0' &&
        gl.reconciliation.passed,
    );
    await page.locator('#trial-balance-link').click();
    await page.locator('#tb-start').fill('2026-01-01');
    await page.locator('#tb-end').fill('2026-02-28');
    await page.locator('#tb-run').click();
    await expect(page.locator('#tb-reconciled')).toBeVisible();
    await page.locator('[data-account-code="5000"] a').click();
    await expect(page.locator('#gl-reconciled')).toBeVisible();
    check(
      'GL UI shows both independently traceable journal numbers',
      (await page.locator('.gl-journal').allTextContents()).join('|') ===
        [original.journalNumber, reversal.journalNumber].join('|'),
    );
    const different = await fetch(`${base}/journals/${original.id}/reverse`, {
      method: 'POST',
      headers: { ...headers, 'If-Match': '"2"', 'Idempotency-Key': seenKeys[0] },
      body: JSON.stringify({ postingDate: '2026-02-02', reason: 'Changed' }),
    });
    check(
      'Same key different date and reason conflict',
      different.status === 409 && (await different.json()).errorId === 'API_IDEMPOTENCY_CONFLICT',
    );
    const again = await fetch(`${base}/journals/${original.id}/reverse`, {
      method: 'POST',
      headers: { ...headers, 'If-Match': '"2"', 'Idempotency-Key': 'different-key' },
      body: JSON.stringify({ postingDate: '2026-02-01', reason: 'Correction' }),
    });
    check(
      'New key cannot double reverse',
      again.status === 409 && (await again.json()).errorId === 'FIN_JOURNAL_ALREADY_REVERSED',
    );
    check('No browser runtime errors', errors.length === 0);
    await writeFile(
      new URL('browser-results.json', directory),
      JSON.stringify({ status: 'PASS', checks, errors, snapshots }, null, 2),
    );
    console.log(JSON.stringify({ status: 'PASS', checks: checks.length }));
  } finally {
    await browser.close();
  }
}
