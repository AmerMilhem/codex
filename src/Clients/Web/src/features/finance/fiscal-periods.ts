import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Authorization } from '../../core/auth';
import { ApiErrors } from '../../core/api';
import { I18n } from '../../core/i18n';
import { FiscalPeriod, FiscalPeriodApi, PeriodClosePreview } from './fiscal-period-api';

@Component({ standalone: true, imports: [RouterLink], templateUrl: './fiscal-periods.html' })
export class FiscalPeriods {
  readonly i18n = inject(I18n);
  readonly authorization = inject(Authorization);
  private readonly api = inject(FiscalPeriodApi);
  private readonly errors = inject(ApiErrors);
  private readonly destroy = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  readonly periods = signal<FiscalPeriod[]>([]);
  readonly preview = signal<PeriodClosePreview | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly active = signal<string | null>(null);
  tenant = '';
  company = '';
  comment = '';
  confirmed = false;
  startDate = '';
  endDate = '';
  private createPending: { startDate: string; endDate: string; key: string } | null = null;
  private pending: { id: string; revision: number; key: string; comment: string | null } | null =
    null;
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.tenant = params.get('tenant') ?? '';
      this.company = params.get('company') ?? '';
      this.active.set(null);
      this.preview.set(null);
      this.pending = null;
      this.createPending = null;
      this.refresh();
    });
  }
  get retryPending(): boolean {
    return this.pending !== null;
  }
  get createRetryPending(): boolean {
    return this.createPending !== null;
  }
  create(event: Event): void {
    event.preventDefault();
    if (this.busy() || !this.authorization.can('Finance.Period.Create')) return;
    if (!this.startDate || !this.endDate || this.startDate > this.endDate) {
      this.error.set(this.i18n.t('periodInvalidDates'));
      return;
    }
    const attempt = this.createPending ?? {
      startDate: this.startDate,
      endDate: this.endDate,
      key: crypto.randomUUID(),
    };
    this.createPending = attempt;
    this.busy.set(true);
    this.error.set('');
    this.api
      .create(this.tenant, this.company, attempt.startDate, attempt.endDate, attempt.key)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.createPending = null;
          this.startDate = '';
          this.endDate = '';
          this.busy.set(false);
          this.refresh();
        },
        error: (error) => {
          if (error.status !== 0) this.createPending = null;
          this.error.set(this.errors.message(error));
          this.busy.set(false);
        },
      });
  }
  refresh(): void {
    if (!this.authorization.can('Finance.Period.View')) {
      this.error.set(this.i18n.t('denied'));
      return;
    }
    this.busy.set(true);
    this.api
      .list(this.tenant, this.company)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (periods) => {
          this.periods.set(periods);
          this.error.set('');
          this.busy.set(false);
        },
        error: (error) => {
          this.error.set(this.errors.message(error));
          this.busy.set(false);
        },
      });
  }
  inspect(period: FiscalPeriod): void {
    if (period.status !== 'Open' || !this.authorization.can('Finance.Period.Close') || this.busy())
      return;
    this.active.set(period.id);
    this.preview.set(null);
    this.error.set('');
    this.comment = '';
    this.confirmed = false;
    this.pending = null;
    this.busy.set(true);
    this.api
      .preview(this.tenant, this.company, period.id)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (preview) => {
          this.preview.set(preview);
          this.busy.set(false);
        },
        error: (error) => {
          this.error.set(this.errors.message(error));
          this.busy.set(false);
        },
      });
  }
  close(event: Event): void {
    event.preventDefault();
    const preview = this.preview();
    if (
      !preview?.passed ||
      !this.confirmed ||
      this.busy() ||
      !this.authorization.can('Finance.Period.Close')
    )
      return;
    if (this.comment.length > 1000) {
      this.error.set(this.i18n.t('invalid'));
      return;
    }
    const attempt = this.pending ?? {
      id: preview.period.id,
      revision: preview.period.revision,
      key: crypto.randomUUID(),
      comment: this.comment.trim() || null,
    };
    this.pending = attempt;
    this.busy.set(true);
    this.error.set('');
    this.api
      .close(this.tenant, this.company, attempt.id, attempt.revision, attempt.key, attempt.comment)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.pending = null;
          this.preview.set(null);
          this.active.set(null);
          this.busy.set(false);
          this.refresh();
        },
        error: (error) => {
          if (error.status !== 0) this.pending = null;
          this.error.set(this.errors.message(error));
          this.busy.set(false);
        },
      });
  }
}
