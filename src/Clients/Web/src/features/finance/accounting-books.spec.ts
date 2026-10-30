import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { AccountingBooks } from './accounting-books';
import { AccountingBook } from './accounting-book-api';

const path = '/api/v1/tenants/t/companies/c/finance/books';
const row: AccountingBook = {
  id: 'book',
  tenantId: 't',
  companyId: 'c',
  code: 'PRIMARY',
  name: 'Primary Accounting Book',
  functionalCurrency: 'JOD',
  minorUnits: 3,
  status: 'Active',
  createdAt: null,
  createdByIssuer: null,
  createdBySubject: null,
};

describe('Accounting book setup', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AccountingBooks],
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
  function setup(
    permissions = ['Finance.Book.View', 'Finance.Book.Create'],
    rows: AccountingBook[] = [],
  ) {
    TestBed.inject(AuthenticationSession).verified('t', 'c', permissions);
    const fixture = TestBed.createComponent(AccountingBooks);
    if (permissions.includes('Finance.Book.View'))
      TestBed.inject(HttpTestingController).expectOne(path).flush(rows);
    return fixture;
  }
  it('keeps Book View and Create separate from Journal and Period permissions', async () => {
    const fixture = setup(
      ['Finance.Book.View', 'Finance.Journal.Post', 'Finance.Period.Create'],
      [row],
    );
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#book-table').textContent).toContain('PRIMARY');
    expect(fixture.nativeElement.querySelector('#book-create-form')).toBeNull();
    const creator = setup(['Finance.Book.Create']);
    await creator.whenStable();
    expect(creator.nativeElement.querySelector('#book-create-form')).toBeTruthy();
    expect(creator.nativeElement.querySelector('#book-table')).toBeNull();
  });
  it('creates with canonical code, currency and idempotency key', async () => {
    const fixture = setup();
    fixture.componentInstance.code = ' primary ';
    fixture.componentInstance.name = ' Main Book ';
    fixture.componentInstance.functionalCurrency = 'JOD';
    fixture.componentInstance.create(new Event('submit'));
    const request = TestBed.inject(HttpTestingController).expectOne(path);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      code: 'PRIMARY',
      name: 'Main Book',
      functionalCurrency: 'JOD',
    });
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({ reference: 'book', appliedRevision: 1 });
    TestBed.inject(HttpTestingController).expectOne(path).flush([row]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#book-table').textContent).toContain('JOD');
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('أُنشئ');
  });
  it('explains invalid currency and duplicate in Arabic', async () => {
    const fixture = setup();
    fixture.componentInstance.code = 'PRIMARY';
    fixture.componentInstance.name = 'Book';
    fixture.componentInstance.functionalCurrency = 'ZZZ';
    fixture.componentInstance.create(new Event('submit'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain(
      'عملة وظيفية',
    );
    fixture.componentInstance.functionalCurrency = 'JOD';
    fixture.componentInstance.create(new Event('submit'));
    TestBed.inject(HttpTestingController)
      .expectOne(path)
      .flush({ code: 'finance.book_duplicate' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('يوجد دفتر');
  });
  it('retries the exact key and payload after a lost response', async () => {
    const fixture = setup();
    fixture.componentInstance.code = 'PRIMARY';
    fixture.componentInstance.name = 'Book';
    fixture.componentInstance.create(new Event('submit'));
    const first = TestBed.inject(HttpTestingController).expectOne(path);
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('offline'), { status: 0 });
    await fixture.whenStable();
    expect(fixture.componentInstance.retryPending).toBe(true);
    fixture.componentInstance.code = 'OTHER';
    fixture.componentInstance.create(new Event('submit'));
    const retry = TestBed.inject(HttpTestingController).expectOne(path);
    expect(retry.request.body.code).toBe('PRIMARY');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush({ reference: 'book', appliedRevision: 1 });
    TestBed.inject(HttpTestingController).expectOne(path).flush([row]);
  });
  it('shows the same book in Arabic RTL and English LTR', async () => {
    const fixture = setup(undefined, [row]);
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('rtl');
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('الدفاتر');
    TestBed.inject(I18n).set('en');
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Accounting books');
    expect(fixture.nativeElement.querySelector('#book-table').textContent).toContain('PRIMARY');
  });
});
