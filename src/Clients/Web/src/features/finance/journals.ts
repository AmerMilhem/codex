import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { forkJoin, Subscription } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { I18n } from '../../core/i18n';
import { Authorization } from '../../core/auth';
import { ApiErrors } from '../../core/api';
import {
  Account,
  DraftRequest,
  ReversalRequest,
  Journal,
  JournalApi,
  JournalAudit,
  ValidationResult,
  totals,
} from './journal-api';

@Component({
  standalone: true,
  imports: [RouterLink],
  templateUrl: './journals.html',
})
export class Journals {
  readonly i18n = inject(I18n);
  readonly authorization = inject(Authorization);
  readonly errors = inject(ApiErrors);
  private api = inject(JournalApi);
  private router = inject(Router);
  private destroy = inject(DestroyRef);
  private route = inject(ActivatedRoute);
  private loading?: Subscription;
  tenant = '';
  company = '';
  id: string | null = null;
  readonly journal = signal<Journal | null>(null);
  readonly items = signal<Journal[]>([]);
  readonly accounts = signal<Account[]>([]);
  readonly audit = signal<JournalAudit[]>([]);
  readonly validation = signal<ValidationResult | null>(null);
  readonly busy = signal(false);
  readonly dirty = signal(false);
  readonly error = signal('');
  readonly notice = signal<'savedDraft' | 'reconciled' | 'reconciliationFailed' | null>(null);
  readonly hasMore = signal(false);
  reversalOpen = false;
  reversalDate = '';
  reversalReason = '';
  reversalConfirmed = false;
  readonly pendingReversal = signal<{
    key: string;
    revision: number;
    request: ReversalRequest;
  } | null>(null);
  get canReverse(): boolean {
    return (
      this.posted &&
      !this.journal()?.correction &&
      !this.journal()?.reversal &&
      this.authorization.can('Finance.Journal.Reverse')
    );
  }
  readonly pendingPost = signal<{ key: string; revision: number } | null>(null);
  offset = 0;
  form: DraftRequest = { postingDate: '', currency: '', reference: '', description: '', lines: [] };
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.loading?.unsubscribe();
      this.tenant = params.get('tenant')!;
      this.company = params.get('company')!;
      this.id = params.get('id');
      this.journal.set(null);
      this.pendingPost.set(null);
      this.pendingReversal.set(null);
      this.reversalOpen = false;
      this.reversalConfirmed = false;
      this.reversalDate = '';
      this.reversalReason = '';
      this.validation.set(null);
      this.audit.set([]);
      this.dirty.set(false);
      this.error.set('');
      this.notice.set(null);
      this.load();
    });
  }
  get ledgerReturn() {
    return this.route.snapshot?.queryParamMap.get('glAccount');
  }
  get ledgerParams() {
    const q = this.route.snapshot.queryParamMap;
    return {
      accountId: q.get('glAccount'),
      startDate: q.get('startDate'),
      endDate: q.get('endDate'),
      offset: q.get('offset'),
      limit: q.get('limit'),
    };
  }
  get base(): string {
    return this.api.base(this.tenant, this.company);
  }
  get listRoute(): string[] {
    return ['/finance', this.tenant, this.company, 'journals'];
  }
  get total() {
    return totals(this.form.lines);
  }
  get posted(): boolean {
    return this.journal()?.status === 'Posted';
  }
  get editable(): boolean {
    return (
      !this.posted &&
      !this.pendingPost() &&
      this.authorization.can(this.id === 'new' ? 'Finance.Journal.Create' : 'Finance.Journal.Edit')
    );
  }
  load(): void {
    this.busy.set(true);
    if (!this.id) {
      this.loadList();
      return;
    }
    if (this.id === 'new') {
      this.loading = this.api
        .setup(this.base)
        .pipe(takeUntilDestroyed(this.destroy))
        .subscribe({
          next: (setup) => {
            this.accounts.set(setup.accounts);
            this.form = {
              postingDate: '',
              currency: setup.currency,
              reference: '',
              description: '',
              lines: [],
            };
            this.addLine();
            this.addLine();
            this.dirty.set(true);
            this.busy.set(false);
          },
          error: (e) => this.fail(e),
        });
    } else {
      this.loading = forkJoin({
        setup: this.api.setup(this.base),
        journal: this.api.get(this.base, this.id),
        audit: this.api.audit(this.base, this.id),
      })
        .pipe(takeUntilDestroyed(this.destroy))
        .subscribe({
          next: ({ setup, journal, audit }) => {
            this.accounts.set(setup.accounts);
            this.journal.set(journal);
            this.audit.set(audit);
            this.form = {
              postingDate: journal.postingDate,
              currency: journal.currency,
              reference: journal.reference,
              description: journal.description,
              lines: journal.lines.map((x) => ({
                id: x.id,
                accountId: x.accountId,
                debit: x.debit,
                credit: x.credit,
                description: x.description,
              })),
            };
            this.dirty.set(false);
            this.busy.set(false);
          },
          error: (e) => this.fail(e),
        });
    }
  }
  loadList(): void {
    this.loading = this.api
      .list(this.base, this.offset)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (page) => {
          this.items.set(page.items);
          this.hasMore.set(page.hasMore);
          this.busy.set(false);
        },
        error: (e) => this.fail(e),
      });
  }
  page(delta: number): void {
    this.offset += delta * 25;
    this.busy.set(true);
    this.loadList();
  }
  changed(): void {
    this.dirty.set(true);
    this.validation.set(null);
    this.notice.set(null);
  }
  addLine(): void {
    this.form.lines.push({ id: null, accountId: '', debit: '0', credit: '0', description: '' });
    this.changed();
  }
  removeLine(index: number): void {
    this.form.lines.splice(index, 1);
    this.changed();
  }
  save(event: Event): void {
    event.preventDefault();
    if (!this.editable || this.busy()) return;
    this.error.set('');
    this.busy.set(true);
    this.validation.set(null);
    const request = { ...this.form, postingDate: this.form.postingDate || null };
    const result =
      this.id === 'new'
        ? this.api.create(this.base, request)
        : this.api.update(this.base, this.id!, this.journal()!.revision, request);
    result.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (receipt) => {
        this.notice.set('savedDraft');
        if (this.id === 'new') void this.router.navigate([...this.listRoute, receipt.reference]);
        else this.load();
      },
      error: (e) => this.fail(e),
    });
  }
  validate(): void {
    if (
      !this.journal() ||
      this.dirty() ||
      this.busy() ||
      !this.authorization.can('Finance.Journal.Edit')
    )
      return;
    this.busy.set(true);
    this.error.set('');
    this.api
      .validate(this.base, this.journal()!.id, this.journal()!.revision)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (result) => {
          this.validation.set(result);
          this.busy.set(false);
        },
        error: (e) => this.fail(e),
      });
  }
  post(): void {
    if (
      !this.journal() ||
      this.posted ||
      this.dirty() ||
      this.busy() ||
      !this.authorization.can('Finance.Journal.Post')
    )
      return;
    this.error.set('');
    this.busy.set(true);
    this.notice.set(null);
    const intent = this.pendingPost() ?? {
      key: crypto.randomUUID(),
      revision: this.journal()!.revision,
    };
    this.pendingPost.set(intent);
    this.api
      .post(this.base, this.journal()!.id, intent.revision, intent.key)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.pendingPost.set(null);
          this.validation.set(null);
          this.load();
        },
        error: (e: HttpErrorResponse) => {
          if (e.status > 0 && e.status < 500) this.pendingPost.set(null);
          this.fail(e);
        },
      });
  }
  reverse(event: Event): void {
    event.preventDefault();
    if (!this.canReverse || this.busy() || !this.reversalConfirmed) return;
    if (!this.reversalDate || !this.reversalReason.trim() || this.reversalReason.length > 1000) {
      this.error.set(this.i18n.t('invalid'));
      return;
    }
    const intent = this.pendingReversal() ?? {
      key: crypto.randomUUID(),
      revision: this.journal()!.revision,
      request: { postingDate: this.reversalDate, reason: this.reversalReason },
    };
    this.pendingReversal.set(intent);
    this.busy.set(true);
    this.error.set('');
    this.api
      .reverse(this.base, this.journal()!.id, intent.revision, intent.key, intent.request)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.pendingReversal.set(null);
          this.reversalOpen = false;
          this.reversalConfirmed = false;
          this.load();
        },
        error: (e: HttpErrorResponse) => {
          if (e.status > 0 && e.status < 500) this.pendingReversal.set(null);
          this.fail(e);
        },
      });
  }
  reconcile(): void {
    this.busy.set(true);
    this.error.set('');
    this.api
      .reconcile(this.base)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (r) => {
          this.notice.set(r.passed ? 'reconciled' : 'reconciliationFailed');
          this.busy.set(false);
        },
        error: (e) => this.fail(e),
      });
  }
  private fail(e: HttpErrorResponse): void {
    this.error.set(this.errors.message(e));
    this.busy.set(false);
  }
}
