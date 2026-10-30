import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
const directory = new URL('../../../artifacts/web/', import.meta.url);
await mkdir(directory, { recursive: true });
const browser = await chromium.launch({
  ...(process.env.POC_CHROMIUM ? { executablePath: process.env.POC_CHROMIUM } : {}),
  headless: true,
  args: ['--no-sandbox'],
});
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const checks = [];
const errors = [];
page.on('pageerror', (error) => errors.push(error.message));
function check(name, value) {
  checks.push({ name, status: value ? 'PASS' : 'FAIL' });
  if (!value) throw new Error(name);
}
try {
  await page.goto('http://127.0.0.1:4208');
  await page.getByRole('heading', { level: 1 }).waitFor();
  check(
    'Styles load under strict CSP',
    await page.evaluate(
      () => getComputedStyle(document.querySelector('header')).display === 'flex',
    ),
  );
  check(
    'Arabic RTL home shell',
    (await page.locator('html').getAttribute('dir')) === 'rtl' &&
      (await page.locator('h1').textContent()) === 'مساحة واحدة، عمل منظم',
  );
  await page.locator('#language').click();
  await page.waitForFunction(() => document.documentElement.dir === 'ltr');
  check(
    'English LTR',
    (await page.locator('h1').textContent()) === 'One workspace. Organized work.',
  );
  await page.getByRole('link', { name: 'Open workspace' }).click();
  await page.waitForURL('**/workspace');
  check('Lazy workspace routing', (await page.locator('h1').textContent()) === 'Working context');
  check('Restricted UI absent', (await page.locator('#authorized-context').count()) === 0);
  await page.reload();
  await page.getByRole('heading', { level: 1 }).waitFor();
  check('Language survives reload', (await page.locator('html').getAttribute('lang')) === 'en');
  await page.locator('#language').click();
  await page.waitForFunction(() => document.documentElement.dir === 'rtl');
  check('Arabic RTL workspace', (await page.locator('h1').textContent()) === 'سياق العمل');
  await page.setViewportSize({ width: 390, height: 844 });
  check(
    'Mobile-width shell fits',
    await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),
  );
  await page.screenshot({ path: new URL('shell-rtl.png', directory).pathname, fullPage: true });
  check('No runtime errors', errors.length === 0);
} finally {
  await writeFile(
    new URL('browser-results.json', directory),
    JSON.stringify(
      { utc: new Date().toISOString(), browser: await browser.version(), checks, errors },
      null,
      2,
    ),
  );
  await browser.close();
}
