import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { FiscalPeriods } from './fiscal-periods';
import { FiscalPeriod, PeriodClosePreview } from './fiscal-period-api';

const base = '/api/v1/tenants/t/companies/c/finance';
const open: FiscalPeriod = {
  id: 'period',
  tenantId: 't',
  companyId: 'c',
  fiscalYear: 2026,
  startDate: '2026-01-01',
  endDate: '2026-01-31',
  status: 'Open',
  revision: 1,
  closedAt: null,
  closedByIssuer: null,
  closedBySubject: null,
  closeComment: null,
};
const preview: PeriodClosePreview = {
  period: open,
  passed: true,
  periodDebit: '100',
  periodCredit: '100',
  checks: [{ code: 'TB_PERIOD_BALANCED', passed: true, expected: '100', actual: '100' }],
};

describe('Fiscal period close', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [FiscalPeriods],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ tenant: 't', company: 'c' })) },
        },
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function setup(permissions = ['Finance.Period.View', 'Finance.Period.Close'], rows = [open]) {
    TestBed.inject(AuthenticationSession).verified('t', 'c', permissions);
    const fixture = TestBed.createComponent(FiscalPeriods);
    TestBed.inject(HttpTestingController)
      .expectOne(base + '/periods')
      .flush(rows);
    return fixture;
  }
  function inspect(fixture: ReturnType<typeof setup>, result = preview) {
    fixture.componentInstance.inspect(open);
    const request = TestBed.inject(HttpTestingController).expectOne(
      base + '/periods/period/pre-close',
    );
    expect(request.request.method).toBe('GET');
    request.flush(result);
  }
  it('lists company periods read-only and hides Close without independent permission', async () => {
    const fixture = setup(['Finance.Period.View', 'Finance.Journal.Post']);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-table').textContent).toContain(
      '2026-01-31',
    );
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    fixture.componentInstance.inspect(open);
    TestBed.inject(HttpTestingController).expectNone(base + '/periods/period/pre-close');
  });
  it('requires passing pre-close checks and explicit confirmation', async () => {
    const fixture = setup();
    inspect(fixture, {
      ...preview,
      passed: false,
      checks: [{ code: 'TB_PERIOD_BALANCED', passed: false, expected: '100', actual: '99' }],
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-validation').textContent).toContain('فشلت');
    expect(fixture.nativeElement.querySelector('#confirm-period-close')).toBeNull();
    fixture.componentInstance.close(new Event('submit'));
    TestBed.inject(HttpTestingController).expectNone(base + '/periods/period/close');
  });
  it('sends business Close command with revision key and optional comment, then renders closed metadata', async () => {
    const fixture = setup();
    inspect(fixture);
    fixture.componentInstance.confirmed = true;
    fixture.componentInstance.comment = 'Reviewed';
    fixture.componentInstance.close(new Event('submit'));
    const request = TestBed.inject(HttpTestingController).expectOne(base + '/periods/period/close');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ comment: 'Reviewed' });
    expect(request.request.headers.get('If-Match')).toBe('"1"');
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({ reference: 'period', appliedRevision: 2 });
    TestBed.inject(HttpTestingController)
      .expectOne(base + '/periods')
      .flush([
        {
          ...open,
          status: 'Closed',
          revision: 2,
          closedAt: '2026-01-31T12:00:00Z',
          closedByIssuer: 'tests',
          closedBySubject: 'closer',
        },
      ]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-table').textContent).toContain('closer');
    expect(fixture.nativeElement.querySelector('#period-table button')).toBeNull();
  });
  it('response loss freezes payload and idempotency key for retry', async () => {
    const fixture = setup();
    inspect(fixture);
    fixture.componentInstance.confirmed = true;
    fixture.componentInstance.comment = 'Approved';
    fixture.componentInstance.close(new Event('submit'));
    const first = TestBed.inject(HttpTestingController).expectOne(base + '/periods/period/close');
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-comment').disabled).toBe(true);
    fixture.componentInstance.comment = 'Changed';
    fixture.componentInstance.close(new Event('submit'));
    const retry = TestBed.inject(HttpTestingController).expectOne(base + '/periods/period/close');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ reference: 'period', appliedRevision: 2 });
    TestBed.inject(HttpTestingController)
      .expectOne(base + '/periods')
      .flush([{ ...open, status: 'Closed' }]);
  });
  it('Arabic and English switch RTL/LTR while dates and amounts stay identical', async () => {
    const fixture = setup();
    inspect(fixture);
    await fixture.whenStable();
    const values = () => fixture.nativeElement.querySelector('#period-close-preview').textContent;
    expect(document.documentElement.dir).toBe('rtl');
    expect(values()).toContain('100');
    TestBed.inject(I18n).toggle();
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(values()).toContain('100');
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('Fiscal periods');
  });
  it('shows Create only with its independent permission and sends dates with a key', async () => {
    const fixture = setup(['Finance.Period.View', 'Finance.Period.Create']);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-close-preview')).toBeNull();
    fixture.componentInstance.startDate = '2027-01-01';
    fixture.componentInstance.endDate = '2027-01-31';
    fixture.componentInstance.create(new Event('submit'));
    const req = TestBed.inject(HttpTestingController).expectOne(base + '/periods');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ startDate: '2027-01-01', endDate: '2027-01-31' });
    expect(req.request.headers.get('Idempotency-Key')).toBeTruthy();
    req.flush({ reference: 'new-period', appliedRevision: 1 });
    TestBed.inject(HttpTestingController)
      .expectOne(base + '/periods')
      .flush([
        open,
        {
          ...open,
          id: 'new-period',
          fiscalYear: 2027,
          startDate: '2027-01-01',
          endDate: '2027-01-31',
        },
      ]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-table').textContent).toContain(
      '2027-01-31',
    );
  });
  it('rejects invalid date order and freezes payload/key after response loss', async () => {
    const fixture = setup(['Finance.Period.View', 'Finance.Period.Create']);
    fixture.componentInstance.startDate = '2027-02-01';
    fixture.componentInstance.endDate = '2027-01-31';
    fixture.componentInstance.create(new Event('submit'));
    TestBed.inject(HttpTestingController).expectNone(base + '/periods');
    expect(fixture.componentInstance.error()).toContain('اختر');
    fixture.componentInstance.startDate = '2027-01-01';
    fixture.componentInstance.create(new Event('submit'));
    const first = TestBed.inject(HttpTestingController).expectOne(base + '/periods');
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#period-create-start').disabled).toBe(true);
    fixture.componentInstance.startDate = '2027-01-02';
    fixture.componentInstance.create(new Event('submit'));
    const retry = TestBed.inject(HttpTestingController).expectOne(base + '/periods');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ reference: 'new-period', appliedRevision: 1 });
    TestBed.inject(HttpTestingController)
      .expectOne(base + '/periods')
      .flush([]);
  });
  it('creation form localizes Arabic/English and RTL/LTR', async () => {
    const fixture = setup(['Finance.Period.View', 'Finance.Period.Create']);
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('rtl');
    expect(fixture.nativeElement.querySelector('#period-create-form').textContent).toContain(
      'إنشاء',
    );
    TestBed.inject(I18n).toggle();
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(fixture.nativeElement.querySelector('#period-create-form').textContent).toContain(
      'Create period',
    );
  });
});
