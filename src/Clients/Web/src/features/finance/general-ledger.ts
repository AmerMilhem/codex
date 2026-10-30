import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, Subscription } from 'rxjs';
import { Authorization } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { ApiErrors } from '../../core/api';
import { JournalApi, Account } from './journal-api';
import { GeneralLedgerApi, GeneralLedgerReport } from './general-ledger-api';
@Component({ standalone: true, imports: [RouterLink], templateUrl: './general-ledger.html' })
export class GeneralLedger {
  readonly i18n = inject(I18n);
  readonly authorization = inject(Authorization);
  private api = inject(GeneralLedgerApi);
  private journals = inject(JournalApi);
  private errors = inject(ApiErrors);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroy = inject(DestroyRef);
  private current?: Subscription;
  private setup?: Subscription;
  tenant = '';
  company = '';
  accountId = '';
  startDate = '';
  endDate = '';
  offset = 0;
  limit = 50;
  readonly accounts = signal<Account[]>([]);
  readonly report = signal<GeneralLedgerReport | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  constructor() {
    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed())
      .subscribe(([p, q]) => {
        this.changed();
        this.setup?.unsubscribe();
        this.accounts.set([]);
        this.tenant = p.get('tenant')!;
        this.company = p.get('company')!;
        this.accountId = q.get('accountId') ?? '';
        this.startDate = q.get('startDate') ?? '';
        this.endDate = q.get('endDate') ?? '';
        this.offset = Number(q.get('offset') ?? 0);
        this.limit = Number(q.get('limit') ?? 50);
        if (!this.authorization.can('Finance.Journal.View')) {
          this.error.set(this.i18n.t('denied'));
          return;
        }
        this.setup = this.journals
          .setup(this.journals.base(this.tenant, this.company))
          .pipe(takeUntilDestroyed(this.destroy))
          .subscribe({
            next: (s) => this.accounts.set(s.accounts),
            error: (e) => this.error.set(this.errors.message(e)),
          });
        if (this.accountId && this.startDate && this.endDate) this.load();
      });
  }
  changed() {
    this.current?.unsubscribe();
    this.report.set(null);
    this.busy.set(false);
    this.error.set('');
  }
  run(event: Event) {
    event.preventDefault();
    this.offset = 0;
    this.navigate();
  }
  page(offset: number) {
    this.offset = offset;
    this.navigate();
  }
  private navigate() {
    // Reload explicitly even when Refresh leaves the URL unchanged.
    void this.router
      .navigate([], {
        relativeTo: this.route,
        queryParams: {
          accountId: this.accountId,
          startDate: this.startDate,
          endDate: this.endDate,
          offset: this.offset,
          limit: this.limit,
        },
      })
      .then((changed) => {
        if (!changed) this.load();
      });
  }
  load() {
    this.changed();
    if (!this.authorization.can('Finance.Journal.View')) {
      this.error.set(this.i18n.t('denied'));
      return;
    }
    if (
      !this.accountId ||
      !this.startDate ||
      !this.endDate ||
      this.startDate > this.endDate ||
      !Number.isSafeInteger(this.offset) ||
      this.offset < 0 ||
      !Number.isInteger(this.limit) ||
      this.limit < 1 ||
      this.limit > 200
    ) {
      this.error.set(this.i18n.t('invalid'));
      return;
    }
    this.busy.set(true);
    this.current = this.api
      .run(
        this.tenant,
        this.company,
        this.accountId,
        this.startDate,
        this.endDate,
        this.offset,
        this.limit,
      )
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (r) => {
          this.report.set(r);
          this.busy.set(false);
        },
        error: (e) => {
          this.error.set(this.errors.message(e));
          this.busy.set(false);
        },
      });
  }
}
