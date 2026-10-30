import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { Authorization } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { ApiErrors } from '../../core/api';
import { TrialBalanceApi, TrialBalanceReport } from './trial-balance-api';

@Component({ standalone: true, imports: [RouterLink], templateUrl: './trial-balance.html' })
export class TrialBalance {
  readonly i18n = inject(I18n);
  readonly authorization = inject(Authorization);
  private api = inject(TrialBalanceApi);
  private errors = inject(ApiErrors);
  private destroy = inject(DestroyRef);
  private route = inject(ActivatedRoute);
  private current?: Subscription;
  tenant = '';
  company = '';
  startDate = `${new Date().getFullYear()}-01-01`;
  endDate = `${new Date().getFullYear()}-${String(new Date().getMonth() + 1).padStart(2, '0')}-${String(new Date().getDate()).padStart(2, '0')}`;
  readonly report = signal<TrialBalanceReport | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  constructor() {
    const query = this.route.snapshot?.queryParamMap;
    this.startDate = query?.get('startDate') || this.startDate;
    this.endDate = query?.get('endDate') || this.endDate;
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.tenant = params.get('tenant')!;
      this.company = params.get('company')!;
      this.changed();
    });
  }
  changed(): void {
    this.current?.unsubscribe();
    this.busy.set(false);
    this.report.set(null);
    this.error.set('');
  }
  run(event: Event): void {
    event.preventDefault();
    this.current?.unsubscribe();
    this.report.set(null);
    this.error.set('');
    if (!this.authorization.can('Finance.Journal.View')) {
      this.error.set(this.i18n.t('denied'));
      return;
    }
    if (!this.startDate || !this.endDate || this.startDate > this.endDate) {
      this.error.set(this.i18n.t('reportInvalidRange'));
      return;
    }
    this.busy.set(true);
    this.current = this.api
      .run(this.tenant, this.company, this.startDate, this.endDate)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.busy.set(false);
        },
        error: (error) => {
          this.error.set(this.errors.message(error));
          this.busy.set(false);
        },
      });
  }
}
