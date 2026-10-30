import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { Journals } from './journals';
import { Journal, totals } from './journal-api';

const base = '/api/v1/tenants/tenant/companies/company/finance';
const draft: Journal = {
  id: 'journal',
  status: 'Draft',
  revision: 7,
  postingDate: '2026-10-18',
  currency: 'JOD',
  reference: 'test',
  description: '',
  journalNumber: null,
  postedByIssuer: null,
  postedBySubject: null,
  postedAt: null,
  totalDebit: '100',
  totalCredit: '100',
  difference: '0',
  roundingPolicy: 'internal-to-even-v1',
  lines: [
    {
      id: 'line-1',
      accountId: 'expense',
      debit: '100',
      credit: '0',
      description: '',
      accountCode: '5000',
      accountName: 'Expense',
      position: 1,
    },
    {
      id: 'line-2',
      accountId: 'cash',
      debit: '0',
      credit: '100',
      description: '',
      accountCode: '1000',
      accountName: 'Cash',
      position: 2,
    },
  ],
};
describe('Manual journal', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [Journals],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(
              convertToParamMap({ tenant: 'tenant', company: 'company', id: 'journal' }),
            ),
          },
        },
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function load(
    journal = draft,
    permissions = ['Finance.Journal.View', 'Finance.Journal.Edit', 'Finance.Journal.Post'],
  ) {
    TestBed.inject(AuthenticationSession).verified('tenant', 'company', permissions);
    const fixture = TestBed.createComponent(Journals);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(base + '/journal-setup').flush({
      currency: 'JOD',
      minorUnits: 3,
      accounts: [
        { id: 'expense', code: '5000', name: 'Expense' },
        { id: 'cash', code: '1000', name: 'Cash' },
      ],
    });
    http.expectOne(base + '/journals/journal').flush(journal);
    http.expectOne(base + '/journals/journal/audit').flush([]);
    return fixture;
  }
  it('sums large decimal strings without losing a minor unit', () => {
    expect(
      totals([
        {
          id: null,
          accountId: '',
          debit: '999999999999999999.998',
          credit: '999999999999999999.999',
          description: '',
        },
        { id: null, accountId: '', debit: '0.001', credit: '0', description: '' },
      ]),
    ).toEqual({
      debit: '999999999999999999.999',
      credit: '999999999999999999.999',
      difference: '0',
      valid: true,
    });
  });
  it('rejects invalid precision and never displays a guessed numeric total', () => {
    expect(
      totals([{ id: null, accountId: '', debit: '0.0000001', credit: '0', description: '' }]).valid,
    ).toBe(false);
  });
  it('hides posting from an editor without the separate Post permission', async () => {
    const fixture = load(draft, ['Finance.Journal.View', 'Finance.Journal.Edit']);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#save-draft')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#post-journal')).toBeNull();
  });
  it('makes posted fields read only and removes edit and post actions', async () => {
    const fixture = load({
      ...draft,
      status: 'Posted',
      journalNumber: 'MJ-2026-000001',
      postedBySubject: 'actor',
      postedAt: '2026-10-18T14:00:00Z',
    });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('fieldset').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('#save-draft')).toBeNull();
    expect(fixture.nativeElement.querySelector('#post-journal')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('MJ-2026-000001');
    expect(fixture.nativeElement.querySelector('.posted-account').textContent).toContain('5000');
  });
  it('retries an uncertain posting with the same key and original revision', async () => {
    const fixture = load();
    fixture.componentInstance.post();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne(base + '/journals/journal/post');
    const key = first.request.headers.get('Idempotency-Key');
    expect(first.request.headers.get('If-Match')).toBe('"7"');
    first.flush({}, { status: 503, statusText: 'Response lost' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('fieldset').disabled).toBe(true);
    fixture.componentInstance.post();
    const retry = http.expectOne(base + '/journals/journal/post');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.headers.get('If-Match')).toBe('"7"');
    retry.flush({}, { status: 422, statusText: 'Rejected' });
  });
  it('renders the journal in Arabic RTL and English LTR', async () => {
    const fixture = load();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('القيود اليدوية');
    expect(document.documentElement.dir).toBe('rtl');
    TestBed.inject(I18n).toggle();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('h1').textContent).toBe('Manual journals');
    expect(document.documentElement.dir).toBe('ltr');
  });
});
