import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { GeneralLedger } from './general-ledger';
import { GeneralLedgerReport } from './general-ledger-api';
const report: GeneralLedgerReport = {
  tenantId: 't',
  companyId: 'c',
  accountId: 'cash',
  accountCode: '1000',
  accountName: 'Cash',
  startDate: '2026-01-01',
  endDate: '2026-01-31',
  currency: 'JOD',
  minorUnits: 3,
  openingScope: 'company-book-lifetime',
  generatedAt: '2026-10-20T00:00:00Z',
  offset: 0,
  limit: 1,
  totalCount: 2,
  openingBalance: '100',
  periodDebit: '40',
  periodCredit: '25',
  closingBalance: '115',
  pageOpeningBalance: '100',
  movements: [
    {
      lineId: 'line',
      journalId: 'journal',
      position: 1,
      postingDate: '2026-01-01',
      journalNumber: 'MJ-2026-1',
      reference: 'GL',
      description: 'Cash',
      journalDescription: 'Golden',
      debit: '40',
      credit: '0',
      runningBalance: '140',
    },
  ],
  reconciliation: { passed: true, checks: [] },
};
describe('General Ledger read-only report', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [GeneralLedger],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ tenant: 't', company: 'c' })),
            queryParamMap: of(
              convertToParamMap({
                accountId: 'cash',
                startDate: '2026-01-01',
                endDate: '2026-01-31',
                limit: '1',
              }),
            ),
          },
        },
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function setup(allowed = true) {
    if (allowed) TestBed.inject(AuthenticationSession).verified('t', 'c', ['Finance.Journal.View']);
    const f = TestBed.createComponent(GeneralLedger);
    if (allowed)
      TestBed.inject(HttpTestingController)
        .expectOne('/api/v1/tenants/t/companies/c/finance/journal-setup')
        .flush({
          accounts: [
            { id: 'cash', code: '1000', name: 'Cash', status: 'Active', postingAllowed: true },
          ],
          currency: 'JOD',
        });
    return f;
  }
  function request() {
    return TestBed.inject(HttpTestingController).expectOne(
      '/api/v1/tenants/t/companies/c/finance/general-ledger?accountId=cash&startDate=2026-01-01&endDate=2026-01-31&offset=0&limit=1',
    );
  }
  it('GET carries scope account dates pagination without command headers', () => {
    setup();
    const r = request();
    expect(r.request.method).toBe('GET');
    expect(r.request.headers.has('Idempotency-Key')).toBe(false);
    r.flush(report);
  });
  it('Arabic RTL and English LTR render identical server exact money', async () => {
    const f = setup();
    request().flush({ ...report, closingBalance: '2000000000000000000.018' });
    await f.whenStable();
    expect(f.nativeElement.querySelector('h1').textContent).toContain('دفتر الأستاذ');
    expect(document.documentElement.dir).toBe('rtl');
    expect(f.nativeElement.querySelector('#gl-closing').textContent).toBe(
      '2000000000000000000.018',
    );
    TestBed.inject(I18n).toggle();
    await f.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(f.nativeElement.querySelector('h1').textContent).toContain('General Ledger');
    expect(f.nativeElement.querySelector('#gl-closing').textContent).toBe(
      '2000000000000000000.018',
    );
  });
  it('journal navigation preserves scope account dates and page and has no edit controls', async () => {
    const f = setup();
    request().flush(report);
    await f.whenStable();
    const link = f.nativeElement.querySelector('.gl-journal');
    expect(link.getAttribute('href')).toContain(
      '/finance/t/c/journals/journal?glAccount=cash&startDate=2026-01-01&endDate=2026-01-31&offset=0&limit=1',
    );
    expect(
      f.nativeElement.querySelectorAll('#general-ledger-table input,#general-ledger-table button')
        .length,
    ).toBe(0);
    expect(f.nativeElement.querySelector('#gl-prev').disabled).toBe(true);
    expect(f.nativeElement.querySelector('#gl-next').disabled).toBe(false);
  });
  it('uses server-provided middle-page running balance and full period totals', async () => {
    const f = setup();
    request().flush({
      ...report,
      offset: 1,
      pageOpeningBalance: '140',
      movements: [{ ...report.movements[0], debit: '0', credit: '25', runningBalance: '115' }],
    });
    await f.whenStable();
    expect(f.nativeElement.querySelector('.gl-running').textContent).toBe('115');
    expect(f.nativeElement.querySelector('#gl-debit').textContent).toBe('40');
    expect(f.nativeElement.querySelector('#gl-next').disabled).toBe(true);
  });
  it('loading and empty states preserve opening-only balance', async () => {
    const f = setup();
    const r = request();
    await f.whenStable();
    expect(f.nativeElement.querySelector('#gl-loading')).not.toBeNull();
    r.flush({
      ...report,
      totalCount: 0,
      movements: [],
      periodDebit: '0',
      periodCredit: '0',
      closingBalance: '100',
    });
    await f.whenStable();
    expect(f.nativeElement.querySelector('#gl-empty')).not.toBeNull();
    expect(f.nativeElement.querySelector('#gl-closing').textContent).toBe('100');
  });
  it('reconciliation failure shows differences and retains source figures', async () => {
    const f = setup();
    request().flush({
      ...report,
      reconciliation: {
        passed: false,
        checks: [{ code: 'GL_TB_CLOSING', passed: false, expected: '115', actual: '114' }],
      },
    });
    await f.whenStable();
    expect(f.nativeElement.querySelector('#gl-warning').textContent).toContain('GL_TB_CLOSING');
    expect(f.nativeElement.querySelector('#general-ledger-table')).not.toBeNull();
  });
  it('server errors clear busy state and cannot leave stale results', async () => {
    const f = setup();
    request().flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not found' });
    await f.whenStable();
    expect(f.nativeElement.querySelector('#gl-error')).not.toBeNull();
    expect(f.componentInstance.report()).toBeNull();
    expect(f.componentInstance.busy()).toBe(false);
  });
  it('permission required before setup or report reads', async () => {
    const f = setup(false);
    TestBed.inject(HttpTestingController).expectNone((r) => r.url.includes('/finance/'));
    await f.whenStable();
    expect(f.nativeElement.querySelector('#gl-run').disabled).toBe(true);
  });
  it('changing selection cancels stale query and rejects invalid ranges', () => {
    const f = setup();
    const r = request();
    f.componentInstance.accountId = 'other';
    f.componentInstance.changed();
    expect(r.cancelled).toBe(true);
    expect(f.componentInstance.report()).toBeNull();
    f.componentInstance.startDate = '2026-02-01';
    f.componentInstance.load();
    TestBed.inject(HttpTestingController).expectNone((r) => r.url.includes('general-ledger'));
    expect(f.componentInstance.error()).not.toBe('');
  });
});
