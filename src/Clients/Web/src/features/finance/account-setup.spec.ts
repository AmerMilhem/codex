import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { AccountSetup } from './account-setup';
import { SetupAccount } from './account-setup-api';

const path = '/api/v1/tenants/t/companies/c/finance/accounts';
const row: SetupAccount = {
  id: 'account',
  code: '6100',
  name: 'Office Expense',
  status: 'Active',
  postingAllowed: true,
  createdAt: null,
  createdByIssuer: null,
  createdBySubject: null,
};

describe('Chart of accounts setup', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AccountSetup],
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
  function setup(permissions = ['Finance.Account.View', 'Finance.Account.Create'], rows = [row]) {
    TestBed.inject(AuthenticationSession).verified('t', 'c', permissions);
    const fixture = TestBed.createComponent(AccountSetup);
    if (permissions.includes('Finance.Account.View'))
      TestBed.inject(HttpTestingController).expectOne(path).flush(rows);
    return fixture;
  }
  it('shows the company account list and independent creation permission', async () => {
    const fixture = setup(['Finance.Account.View', 'Finance.Journal.Post']);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#account-table').textContent).toContain('6100');
    expect(fixture.nativeElement.querySelector('#account-create-form')).toBeNull();
  });
  it('allows Create without granting account list access', async () => {
    const fixture = setup(['Finance.Account.Create']);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#account-create-form')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#account-table')).toBeNull();
    TestBed.inject(HttpTestingController).expectNone(path);
  });
  it('creates with normalized code, explicit PostingAllowed and a stable key', async () => {
    const fixture = setup();
    fixture.componentInstance.code = ' ac-101 ';
    fixture.componentInstance.name = ' Cash ';
    fixture.componentInstance.postingAllowed = false;
    fixture.componentInstance.create(new Event('submit'));
    const request = TestBed.inject(HttpTestingController).expectOne(path);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ code: 'AC-101', name: 'Cash', postingAllowed: false });
    expect(request.request.headers.get('Idempotency-Key')).toBeTruthy();
    request.flush({ reference: 'new', appliedRevision: 1 });
    TestBed.inject(HttpTestingController)
      .expectOne(path)
      .flush([row, { ...row, id: 'new', code: 'AC-101', name: 'Cash', postingAllowed: false }]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#account-table').textContent).toContain('AC-101');
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain('أُنشئ');
  });
  it('rejects invalid code/name locally and maps duplicate server error in Arabic', async () => {
    const fixture = setup();
    fixture.componentInstance.code = 'A B';
    fixture.componentInstance.name = 'Name';
    fixture.componentInstance.create(new Event('submit'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('رمزًا');
    fixture.componentInstance.code = '6100';
    fixture.componentInstance.name = 'Duplicate';
    fixture.componentInstance.create(new Event('submit'));
    TestBed.inject(HttpTestingController)
      .expectOne(path)
      .flush({ code: 'finance.account_duplicate' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('يوجد حساب');
  });
  it('keeps payload and key frozen after response loss', async () => {
    const fixture = setup();
    fixture.componentInstance.code = '6200';
    fixture.componentInstance.name = 'Travel';
    fixture.componentInstance.create(new Event('submit'));
    const first = TestBed.inject(HttpTestingController).expectOne(path);
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('offline'), { status: 0 });
    await fixture.whenStable();
    expect(fixture.componentInstance.retryPending).toBe(true);
    fixture.componentInstance.code = '9999';
    fixture.componentInstance.create(new Event('submit'));
    const retry = TestBed.inject(HttpTestingController).expectOne(path);
    expect(retry.request.body.code).toBe('6200');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush({ reference: 'new', appliedRevision: 1 });
    TestBed.inject(HttpTestingController).expectOne(path).flush([row]);
  });
  it('renders the same accounts in English LTR and Arabic RTL', async () => {
    const fixture = setup();
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('rtl');
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('دليل');
    TestBed.inject(I18n).set('en');
    await fixture.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Chart of accounts');
    expect(fixture.nativeElement.querySelector('#account-table').textContent).toContain('6100');
  });
});
