import { Component, inject } from '@angular/core';
import { I18n } from '../core/i18n';
import { CanDirective } from '../core/auth';
import { AuthenticationSession } from '../core/auth';
import { ApiClient, ApiErrors } from '../core/api';
import { signal } from '@angular/core';
import { RouterLink } from '@angular/router';
@Component({
  standalone: true,
  imports: [CanDirective, RouterLink],
  template: `<section class="panel">
    <h1>{{ i18n.t('context') }}</h1>
    <p>{{ i18n.t('pending') }}</p>
    <span *erpCan="'foundation.context.read'" id="authorized-context">{{
      i18n.t('workspace')
    }}</span>
    @if (session.authenticated()) {
      <a
        class="primary"
        [routerLink]="['/finance', session.tenant(), session.company(), 'journals']"
        >{{ i18n.t('journals') }}</a
      >
      <button type="button" (click)="session.signOut()">{{ i18n.t('signOut') }}</button>
      @if (session.permissions().has('Finance.Journal.View')) {
        <a
          class="primary"
          [routerLink]="['/finance', session.tenant(), session.company(), 'trial-balance']"
          >{{ i18n.t('trialBalance') }}</a
        >
      }
      @if (
        session.permissions().has('Finance.Period.View') ||
        session.permissions().has('Finance.Period.Create')
      ) {
        <a
          class="primary"
          [routerLink]="['/finance', session.tenant(), session.company(), 'periods']"
          >{{ i18n.t('fiscalPeriods') }}</a
        >
      }
      @if (
        session.permissions().has('Finance.Account.View') ||
        session.permissions().has('Finance.Account.Create')
      ) {
        <a
          class="primary"
          [routerLink]="['/finance', session.tenant(), session.company(), 'accounts']"
          >{{ i18n.t('chartOfAccounts') }}</a
        >
      }
      @if (
        session.permissions().has('Finance.Book.View') ||
        session.permissions().has('Finance.Book.Create')
      ) {
        <a
          class="primary"
          [routerLink]="['/finance', session.tenant(), session.company(), 'books']"
          >{{ i18n.t('accountingBooks') }}</a
        >
      }
    } @else {
      <h2>{{ i18n.t('localSession') }}</h2>
      <p>{{ i18n.t('localSessionNote') }}</p>
      <form (submit)="connect($event)">
        <label
          >{{ i18n.t('tenant')
          }}<input
            id="tenant"
            required
            [value]="tenant"
            (input)="tenant = $any($event.target).value"
        /></label>
        <label
          >{{ i18n.t('company')
          }}<input
            id="company"
            required
            [value]="company"
            (input)="company = $any($event.target).value"
        /></label>
        <label
          >{{ i18n.t('token')
          }}<input
            id="session-token"
            type="password"
            required
            autocomplete="off"
            [value]="token"
            (input)="token = $any($event.target).value"
        /></label>
        <button type="submit" [disabled]="busy()">{{ i18n.t('connect') }}</button>
      </form>
    }
    @if (error()) {
      <p role="alert">{{ error() }}</p>
    }
  </section>`,
})
export class Workspace {
  readonly i18n = inject(I18n);
  readonly session = inject(AuthenticationSession);
  private api = inject(ApiClient);
  private errors = inject(ApiErrors);
  readonly error = signal('');
  readonly busy = signal(false);
  tenant = '';
  company = '';
  token = '';
  connect(event: Event): void {
    event.preventDefault();
    this.busy.set(true);
    this.error.set('');
    this.session.beginLocalSession(this.token);
    this.token = '';
    this.api.companyContext(this.tenant, this.company).subscribe({
      next: (context) => {
        this.session.verified(context.tenantId, context.companyId, context.permissions);
        this.busy.set(false);
      },
      error: (error) => {
        this.session.signOut();
        this.error.set(this.errors.message(error));
        this.busy.set(false);
      },
    });
  }
}
