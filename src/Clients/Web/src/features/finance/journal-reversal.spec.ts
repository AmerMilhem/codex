import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthenticationSession } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { Journals } from './journals';
import { Journal, JournalReversal } from './journal-api';
const base = '/api/v1/tenants/t/companies/c/finance';
const posted: Journal = {
  id: 'original',
  status: 'Posted',
  revision: 2,
  postingDate: '2026-01-01',
  currency: 'JOD',
  reference: 'REF',
  description: 'Original',
  journalNumber: 'MJ-2026-000001',
  postedByIssuer: 'tests',
  postedBySubject: 'actor',
  postedAt: '2026-01-01T00:00:00Z',
  totalDebit: '100',
  totalCredit: '100',
  difference: '0',
  roundingPolicy: 'internal-to-even-v1',
  lines: [],
};
const relation: JournalReversal = {
  originalJournalId: 'original',
  originalJournalNumber: 'MJ-2026-000001',
  reversalJournalId: 'reversal',
  reversalJournalNumber: 'MJ-2026-000002',
  postingDate: '2026-02-01',
  reason: 'Correction',
  actorIssuer: 'tests',
  actorSubject: 'actor',
  timestamp: '2026-02-01T00:00:00Z',
  requestId: 'request',
  correlationId: 'correlation',
};
describe('Manual Journal Reversal', () => {
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
            paramMap: of(convertToParamMap({ tenant: 't', company: 'c', id: 'original' })),
          },
        },
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function flush(j: Journal) {
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(base + '/journal-setup').flush({ currency: 'JOD', minorUnits: 3, accounts: [] });
    http.expectOne(base + '/journals/original').flush(j);
    http.expectOne(base + '/journals/original/audit').flush([]);
  }
  function setup(j = posted, permissions = ['Finance.Journal.View', 'Finance.Journal.Reverse']) {
    TestBed.inject(AuthenticationSession).verified('t', 'c', permissions);
    const f = TestBed.createComponent(Journals);
    flush(j);
    return f;
  }
  function prepare(f: ReturnType<typeof setup>) {
    f.componentInstance.reversalOpen = true;
    f.componentInstance.reversalDate = '2026-02-01';
    f.componentInstance.reversalReason = 'Correction';
    f.componentInstance.reversalConfirmed = true;
  }
  it('Post permission does not grant Reverse', async () => {
    const f = setup(posted, ['Finance.Journal.View', 'Finance.Journal.Post']);
    await f.whenStable();
    expect(f.nativeElement.querySelector('#reverse-journal')).toBeNull();
    prepare(f);
    f.componentInstance.reverse(new Event('submit'));
    TestBed.inject(HttpTestingController).expectNone(base + '/journals/original/reverse');
  });
  it('Draft and original/reversal with a relationship are ineligible', async () => {
    const f = setup({ ...posted, status: 'Draft' });
    await f.whenStable();
    expect(f.componentInstance.canReverse).toBe(false);
    f.componentInstance.journal.set({ ...posted, reversal: relation });
    await f.whenStable();
    expect(f.nativeElement.querySelector('#reverse-journal')).toBeNull();
  });
  it('requires explicit confirmation date and reason before sending', () => {
    const f = setup();
    prepare(f);
    f.componentInstance.reversalConfirmed = false;
    f.componentInstance.reverse(new Event('submit'));
    TestBed.inject(HttpTestingController).expectNone(base + '/journals/original/reverse');
    f.componentInstance.reversalConfirmed = true;
    f.componentInstance.reversalReason = ' ';
    f.componentInstance.reverse(new Event('submit'));
    expect(f.componentInstance.error()).not.toBe('');
  });
  it('sends business command with independent date reason precondition and idempotency', async () => {
    const f = setup();
    prepare(f);
    f.componentInstance.reverse(new Event('submit'));
    const r = TestBed.inject(HttpTestingController).expectOne(base + '/journals/original/reverse');
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ postingDate: '2026-02-01', reason: 'Correction' });
    expect(r.request.headers.get('If-Match')).toBe('"2"');
    expect(r.request.headers.get('Idempotency-Key')).toBeTruthy();
    r.flush({ reference: 'reversal', appliedRevision: 2 });
    flush({ ...posted, reversal: relation });
    await f.whenStable();
    expect(f.nativeElement.querySelector('#reversal-relationship').textContent).toContain(
      'Correction',
    );
    expect(f.nativeElement.querySelector('#reverse-journal')).toBeNull();
  });
  it('response loss retains exact key and frozen payload for retry', async () => {
    const f = setup();
    prepare(f);
    f.componentInstance.reverse(new Event('submit'));
    const first = TestBed.inject(HttpTestingController).expectOne(
      base + '/journals/original/reverse',
    );
    const key = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('error'));
    await f.whenStable();
    expect(f.nativeElement.querySelector('#reversal-date').disabled).toBe(true);
    expect(f.nativeElement.querySelector('#reversal-reason').disabled).toBe(true);
    f.componentInstance.reverse(new Event('submit'));
    const retry = TestBed.inject(HttpTestingController).expectOne(
      base + '/journals/original/reverse',
    );
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual(first.request.body);
    retry.flush({ reference: 'reversal', appliedRevision: 2 });
    flush({ ...posted, reversal: relation });
  });
  it('closed target error permits changing date and does not mutate the original', async () => {
    const f = setup();
    prepare(f);
    f.componentInstance.reverse(new Event('submit'));
    TestBed.inject(HttpTestingController)
      .expectOne(base + '/journals/original/reverse')
      .flush({ code: 'finance.reversal_period_closed' }, { status: 422, statusText: 'Closed' });
    await f.whenStable();
    expect(f.componentInstance.pendingReversal()).toBeNull();
    expect(f.componentInstance.journal()).toEqual(posted);
    expect(f.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'فترة العكس مغلقة',
    );
  });
  it('bidirectional links use existing journal route and expose no edit controls', async () => {
    const f = setup({ ...posted, reversal: relation });
    await f.whenStable();
    expect(f.nativeElement.querySelector('#original-link').getAttribute('href')).toBe(
      '/finance/t/c/journals/original',
    );
    expect(f.nativeElement.querySelector('#reversal-link').getAttribute('href')).toBe(
      '/finance/t/c/journals/reversal',
    );
    expect(f.nativeElement.querySelector('#save-draft')).toBeNull();
  });
  it('Arabic English and RTL LTR preserve reason and independent reversal date', async () => {
    const f = setup({ ...posted, reversal: relation });
    await f.whenStable();
    expect(document.documentElement.dir).toBe('rtl');
    expect(f.nativeElement.querySelector('#reversal-relationship').textContent).toContain(
      '2026-02-01',
    );
    TestBed.inject(I18n).toggle();
    await f.whenStable();
    expect(document.documentElement.dir).toBe('ltr');
    expect(f.nativeElement.querySelector('#reversal-relationship').textContent).toContain(
      'Reversal relationship',
    );
    expect(f.nativeElement.querySelector('#reversal-relationship').textContent).toContain(
      'Correction',
    );
  });
});
