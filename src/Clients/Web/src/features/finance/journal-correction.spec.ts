import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { JournalCorrection } from './journal-correction';
import { Journal } from './journal-api';
const base = '/api/v1/tenants/t/companies/c/finance';
const original: Journal = {
  id: 'original',
  status: 'Posted',
  revision: 2,
  postingDate: '2026-01-01',
  currency: 'JOD',
  reference: 'R',
  description: 'Original',
  journalNumber: 'MJ-1',
  postedByIssuer: 'tests',
  postedBySubject: 'actor',
  postedAt: '2026-01-01T00:00:00Z',
  totalDebit: '100',
  totalCredit: '100',
  difference: '0',
  roundingPolicy: 'internal-to-even-v1',
  lines: [
    {
      id: '1',
      position: 1,
      accountId: 'e',
      accountCode: '6100',
      accountName: 'Expense',
      debit: '100',
      credit: '0',
      description: '',
    },
    {
      id: '2',
      position: 2,
      accountId: 'c',
      accountCode: '1100',
      accountName: 'Cash',
      debit: '0',
      credit: '100',
      description: '',
    },
  ],
};
describe('Manual journal correction', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [JournalCorrection],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([
          { path: 'finance/:tenant/:company/journals/:id', component: JournalCorrection },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ tenant: 't', company: 'c', id: 'original' }),
            },
          },
        },
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function setup(permissions = ['Finance.Journal.View', 'Finance.Journal.Correct']) {
    TestBed.inject(AuthenticationSession).verified('t', 'c', permissions);
    const f = TestBed.createComponent(JournalCorrection);
    if (
      permissions.includes('Finance.Journal.View') &&
      permissions.includes('Finance.Journal.Correct')
    ) {
      const h = TestBed.inject(HttpTestingController);
      h.expectOne(base + '/journals/original').flush(original);
      h.expectOne(base + '/journal-setup').flush({
        currency: 'JOD',
        minorUnits: 3,
        accounts: [
          { id: 'e', code: '6100', name: 'Expense', status: 'Active', postingAllowed: true },
          { id: 'c', code: '1100', name: 'Cash', status: 'Active', postingAllowed: true },
        ],
      });
    }
    return f;
  }
  it('shows immutable original, prefilled copy, Arabic RTL and English LTR', async () => {
    const f = setup();
    await f.whenStable();
    expect(f.nativeElement.querySelector('#correction-original-lines').textContent).toContain(
      '100',
    );
    expect(f.componentInstance.lines[0].debit).toBe('100');
    expect(document.documentElement.dir).toBe('rtl');
    TestBed.inject(I18n).set('en');
    await f.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(f.nativeElement.querySelector('h1').textContent).toContain('Correct journal');
  });
  it('posts 120/120 replacement intent with independent key', async () => {
    const f = setup();
    await f.whenStable();
    f.componentInstance.date = '2026-02-01';
    f.componentInstance.reason = 'Correct';
    f.componentInstance.lines[0].debit = '120';
    f.componentInstance.lines[1].credit = '120';
    f.componentInstance.confirmed = true;
    f.componentInstance.submit(new Event('submit'));
    const r = TestBed.inject(HttpTestingController).expectOne(base + '/journals/original/correct');
    expect(r.request.headers.get('If-Match')).toBe('"2"');
    expect(r.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(r.request.body.lines[0].debit).toBe('120');
    r.flush({ reference: 'replacement', appliedRevision: 2 });
  });
  it('requires Correct independently of Post and Reverse', async () => {
    const f = setup(['Finance.Journal.View', 'Finance.Journal.Post', 'Finance.Journal.Reverse']);
    await f.whenStable();
    expect(f.componentInstance.eligible).toBeFalsy();
    expect(f.nativeElement.querySelector('#correction-form')).toBeNull();
  });
});
