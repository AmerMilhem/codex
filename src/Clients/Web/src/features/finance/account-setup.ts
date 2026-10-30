import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiErrors } from '../../core/api';
import { Authorization } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { AccountSetupApi, SetupAccount } from './account-setup-api';

@Component({ standalone: true, imports: [RouterLink], templateUrl: './account-setup.html' })
export class AccountSetup {
  readonly i18n = inject(I18n);
  readonly authorization = inject(Authorization);
  private readonly api = inject(AccountSetupApi);
  private readonly errors = inject(ApiErrors);
  private readonly destroy = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  readonly accounts = signal<SetupAccount[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  tenant = '';
  company = '';
  code = '';
  name = '';
  postingAllowed = true;
  private pending: { code: string; name: string; postingAllowed: boolean; key: string } | null =
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
    if (!this.authorization.can('Finance.Account.View')) {
      this.accounts.set([]);
      return;
    }
    this.busy.set(true);
    this.api
      .list(this.tenant, this.company)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (rows) => {
          this.accounts.set(rows);
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
    if (this.busy() || !this.authorization.can('Finance.Account.Create')) return;
    const code = this.code.trim().toUpperCase();
    const name = this.name.trim();
    if (!/^[A-Z0-9][A-Z0-9._-]{0,31}$/.test(code)) {
      this.error.set(this.i18n.t('accountCodeInvalid'));
      return;
    }
    if (!name || name.length > 200) {
      this.error.set(this.i18n.t('accountNameInvalid'));
      return;
    }
    const attempt = this.pending ?? {
      code,
      name,
      postingAllowed: this.postingAllowed,
      key: crypto.randomUUID(),
    };
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
        attempt.postingAllowed,
        attempt.key,
      )
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.pending = null;
          this.code = '';
          this.name = '';
          this.postingAllowed = true;
          this.busy.set(false);
          this.notice.set(this.i18n.t('accountCreated'));
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
