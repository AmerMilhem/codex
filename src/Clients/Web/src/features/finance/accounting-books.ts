import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiErrors } from '../../core/api';
import { Authorization } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { AccountingBook, AccountingBookApi } from './accounting-book-api';

@Component({ standalone: true, imports: [RouterLink], templateUrl: './accounting-books.html' })
export class AccountingBooks {
  readonly i18n = inject(I18n);
  readonly authorization = inject(Authorization);
  private readonly api = inject(AccountingBookApi);
  private readonly errors = inject(ApiErrors);
  private readonly destroy = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  readonly books = signal<AccountingBook[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  tenant = '';
  company = '';
  code = '';
  name = '';
  functionalCurrency = 'JOD';
  private pending: { code: string; name: string; functionalCurrency: string; key: string } | null =
    null;
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.tenant = params.get('tenant') ?? '';
      this.company = params.get('company') ?? '';
      this.pending = null;
      this.refresh();
    });
  }
  get retryPending(): boolean {
    return this.pending !== null;
  }
  refresh(): void {
    if (!this.authorization.can('Finance.Book.View')) {
      this.books.set([]);
      return;
    }
    this.busy.set(true);
    this.api
      .list(this.tenant, this.company)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (rows) => {
          this.books.set(rows);
          this.error.set('');
          this.busy.set(false);
        },
        error: (e) => {
          this.error.set(this.errors.message(e));
          this.busy.set(false);
        },
      });
  }
  create(event: Event): void {
    event.preventDefault();
    if (this.busy() || !this.authorization.can('Finance.Book.Create')) return;
    const code = this.code.trim().toUpperCase();
    const name = this.name.trim();
    const functionalCurrency = this.functionalCurrency.trim().toUpperCase();
    if (!/^[A-Z0-9][A-Z0-9._-]{0,31}$/.test(code)) {
      this.error.set(this.i18n.t('bookCodeInvalid'));
      return;
    }
    if (!name || name.length > 200) {
      this.error.set(this.i18n.t('bookNameInvalid'));
      return;
    }
    if (
      !['JOD', 'USD', 'EUR', 'GBP', 'SAR', 'AED', 'KWD', 'BHD', 'OMR', 'JPY'].includes(
        functionalCurrency,
      )
    ) {
      this.error.set(this.i18n.t('bookCurrencyInvalid'));
      return;
    }
    const attempt = this.pending ?? { code, name, functionalCurrency, key: crypto.randomUUID() };
    this.pending = attempt;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .create(
        this.tenant,
        this.company,
        attempt.code,
        attempt.name,
        attempt.functionalCurrency,
        attempt.key,
      )
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.pending = null;
          this.code = '';
          this.name = '';
          this.functionalCurrency = 'JOD';
          this.busy.set(false);
          this.notice.set(this.i18n.t('bookCreated'));
          this.refresh();
        },
        error: (e) => {
          if (e.status !== 0) this.pending = null;
          this.error.set(this.errors.message(e));
          this.busy.set(false);
        },
      });
  }
}
