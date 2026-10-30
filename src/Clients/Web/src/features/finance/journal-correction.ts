import { Component, inject, signal, DestroyRef } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin } from 'rxjs';
import { Authorization } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { ApiErrors } from '../../core/api';
import { JournalApi, Journal, DraftLine, Account, Receipt, totals } from './journal-api';
@Component({ standalone: true, imports: [RouterLink], templateUrl: './journal-correction.html' })
export class JournalCorrection {
  readonly i18n = inject(I18n);
  readonly auth = inject(Authorization);
  private errors = inject(ApiErrors);
  private api = inject(JournalApi);
  private http = inject(HttpClient);
  private router = inject(Router);
  private destroy = inject(DestroyRef);
  private params = inject(ActivatedRoute).snapshot.paramMap;
  tenant = this.params.get('tenant')!;
  company = this.params.get('company')!;
  id = this.params.get('id')!;
  base = this.api.base(this.tenant, this.company);
  original = signal<Journal | null>(null);
  accounts = signal<Account[]>([]);
  busy = signal(false);
  error = signal('');
  lines: DraftLine[] = [];
  date = '';
  reason = '';
  confirmed = false;
  pending: {
    key: string;
    request: { postingDate: string; reason: string; lines: DraftLine[] };
  } | null = null;
  constructor() {
    if (!this.auth.can('Finance.Journal.View') || !this.auth.can('Finance.Journal.Correct')) {
      this.error.set(this.i18n.t('denied'));
      return;
    }
    forkJoin({ journal: this.api.get(this.base, this.id), setup: this.api.setup(this.base) })
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: ({ journal, setup }) => {
          this.original.set(journal);
          this.accounts.set(setup.accounts);
          this.lines = journal.lines.map((x) => ({
            id: null,
            accountId: x.accountId,
            debit: x.debit,
            credit: x.credit,
            description: x.description,
          }));
        },
        error: (e) => this.error.set(this.errors.message(e)),
      });
  }
  get eligible() {
    const j = this.original();
    return (
      j?.status === 'Posted' &&
      !j.reversal &&
      !j.correction &&
      this.auth.can('Finance.Journal.Correct')
    );
  }
  get total() {
    return totals(this.lines);
  }
  add() {
    this.lines.push({ id: null, accountId: '', debit: '0', credit: '0', description: '' });
  }
  submit(e: Event) {
    e.preventDefault();
    if (this.busy() || !this.eligible || !this.confirmed) return;
    if (!this.pending) {
      if (!this.date || !this.reason.trim() || !this.total.valid || this.total.difference !== '0') {
        this.error.set(this.i18n.t('invalid'));
        return;
      }
      this.pending = {
        key: crypto.randomUUID(),
        request: {
          postingDate: this.date,
          reason: this.reason.trim(),
          lines: this.lines.map((x) => ({ ...x })),
        },
      };
    }
    this.busy.set(true);
    this.error.set('');
    this.http
      .post<Receipt>(`${this.base}/journals/${this.id}/correct`, this.pending.request, {
        headers: {
          'If-Match': `"${this.original()!.revision}"`,
          'Idempotency-Key': this.pending.key,
        },
      })
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (r) => {
          this.busy.set(false);
          void this.router.navigate([
            '/finance',
            this.tenant,
            this.company,
            'journals',
            r.reference,
          ]);
        },
        error: (e: HttpErrorResponse) => {
          this.busy.set(false);
          if (e.status > 0 && e.status < 500) this.pending = null;
          this.error.set(this.errors.message(e));
        },
      });
  }
}
