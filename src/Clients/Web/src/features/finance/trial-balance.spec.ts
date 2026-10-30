import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { TrialBalance } from './trial-balance';
import { TrialBalanceReport } from './trial-balance-api';

const zero = {
  openingDebit: '0',
  openingCredit: '0',
  periodDebit: '0',
  periodCredit: '0',
  closingDebit: '0',
  closingCredit: '0',
};
const report: TrialBalanceReport = {
  tenantId: 'tenant',
  companyId: 'company',
  startDate: '2026-01-01',
  endDate: '2026-01-31',
  currency: 'JOD',
  minorUnits: 3,
  openingScope: 'company-book-lifetime',
  generatedAt: '2026-10-18T00:00:00Z',
  accounts: [
    {
      accountId: 'expense',
      accountCode: '5000',
      accountName: 'Expense',
      amounts: { ...zero, periodDebit: '100', closingDebit: '100' },
    },
    {
      accountId: 'cash',
      accountCode: '1000',
      accountName: 'Cash',
      amounts: { ...zero, periodCredit: '100', closingCredit: '100' },
    },
  ],
  totals: {
    ...zero,
    periodDebit: '100',
    periodCredit: '100',
    closingDebit: '100',
    closingCredit: '100',
  },
  ledgerControl: {
    totals: zero,
    openingLedgerDebit: '0',
    openingLedgerCredit: '0',
    closingLedgerDebit: '100',
    closingLedgerCredit: '100',
    postedJournalCount: 1,
    postedLineCount: 2,
    invalidJournalCount: 0,
  },
  reconciliation: { passed: true, checks: [] },
};
describe('Trial balance read-only report', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [TrialBalance],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ tenant: 'tenant', company: 'company' })) },
        },
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function setup(allowed = true) {
    if (allowed)
      TestBed.inject(AuthenticationSession).verified('tenant', 'company', ['Finance.Journal.View']);
    const fixture = TestBed.createComponent(TrialBalance);
    fixture.componentInstance.startDate = '2026-01-01';
    fixture.componentInstance.endDate = '2026-01-31';
    return fixture;
  }
  function request() {
    return TestBed.inject(HttpTestingController).expectOne(
      '/api/v1/tenants/tenant/companies/company/finance/trial-balance?startDate=2026-01-01&endDate=2026-01-31',
    );
  }
  it('uses GET with explicit scope/date selectors and no command headers', () => {
    const view = setup();
    view.componentInstance.run(new Event('submit'));
    const r = request();
    expect(r.request.method).toBe('GET');
    expect(r.request.body).toBeNull();
    expect(r.request.headers.has('Idempotency-Key')).toBe(false);
    expect(r.request.headers.has('If-Match')).toBe(false);
    r.flush(report);
  });
  it('renders Arabic RTL and English LTR with identical exact financial strings', async () => {
    const view = setup();
    view.componentInstance.run(new Event('submit'));
    request().flush(report);
    await view.whenStable();
    const values = () =>
      Array.from(view.nativeElement.querySelectorAll('td[dir="ltr"]')).map(
        (x: unknown) => (x as HTMLElement).textContent,
      );
    const arabic = values();
    expect(view.nativeElement.querySelector('h1').textContent).toBe('ميزان المراجعة');
    expect(document.documentElement.dir).toBe('rtl');
    TestBed.inject(I18n).toggle();
    await view.whenStable();
    expect(view.nativeElement.querySelector('h1').textContent).toBe('Trial balance');
    expect(document.documentElement.dir).toBe('ltr');
    expect(values()).toEqual(arabic);
  });
  it('shows loading then empty balanced results with grand totals', async () => {
    const view = setup();
    view.componentInstance.run(new Event('submit'));
    await view.whenStable();
    expect(view.nativeElement.querySelector('#tb-loading')).not.toBeNull();
    request().flush({ ...report, accounts: [], totals: zero });
    await view.whenStable();
    expect(view.nativeElement.querySelector('#tb-empty')).not.toBeNull();
    expect(view.nativeElement.querySelector('#tb-grand-totals').textContent).toContain('0');
  });
  it('shows a reconciliation failure with its exact differences without hiding the figures', async () => {
    const view = setup();
    view.componentInstance.run(new Event('submit'));
    request().flush({
      ...report,
      reconciliation: {
        passed: false,
        checks: [{ code: 'TB_PERIOD_DEBIT_MATCH', passed: false, expected: '101', actual: '100' }],
      },
    });
    await view.whenStable();
    expect(view.nativeElement.querySelector('#tb-warning').textContent).toContain(
      'TB_PERIOD_DEBIT_MATCH',
    );
    expect(view.nativeElement.querySelector('#trial-balance-table')).not.toBeNull();
    expect(view.nativeElement.querySelector('#tb-reconciled')).toBeNull();
  });
  it('handles server errors without retaining a misleading previous report', async () => {
    const view = setup();
    view.componentInstance.report.set(report);
    view.componentInstance.run(new Event('submit'));
    request().flush(
      { code: 'finance.report_invalid_range' },
      { status: 400, statusText: 'Invalid range' },
    );
    await view.whenStable();
    expect(view.nativeElement.querySelector('#tb-error').textContent).toContain('نطاق تواريخ');
    expect(view.nativeElement.querySelector('#trial-balance-table')).toBeNull();
  });
  it('denies users without financial View permission and rejects invalid date ranges', async () => {
    const view = setup(false);
    view.componentInstance.run(new Event('submit'));
    TestBed.inject(HttpTestingController).expectNone((r) => r.url.includes('trial-balance'));
    await view.whenStable();
    expect(view.nativeElement.querySelector('#tb-run').disabled).toBe(true);
    TestBed.inject(AuthenticationSession).verified('tenant', 'company', ['Finance.Journal.View']);
    view.componentInstance.startDate = '2026-02-01';
    view.componentInstance.run(new Event('submit'));
    expect(view.componentInstance.error()).toContain('نطاق تواريخ');
  });
  it('cancels an obsolete request when dates change so old data cannot appear under a new range', () => {
    const view = setup();
    view.componentInstance.run(new Event('submit'));
    const original = request();
    view.componentInstance.startDate = '2026-02-01';
    view.componentInstance.changed();
    expect(original.cancelled).toBe(true);
    expect(view.componentInstance.report()).toBeNull();
    expect(view.componentInstance.busy()).toBe(false);
  });
  it('has no financial edit or post action and provides scoped read-only GL navigation', async () => {
    const view = setup();
    view.componentInstance.run(new Event('submit'));
    request().flush(report);
    await view.whenStable();
    expect(
      view.nativeElement.querySelectorAll('#trial-balance-table input, #trial-balance-table button')
        .length,
    ).toBe(0);
    expect(
      view.nativeElement.querySelector('#trial-balance-table a').getAttribute('href'),
    ).toContain('general-ledger?accountId=expense&startDate=2026-01-01&endDate=2026-01-31');
  });
});
