import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { I18n } from '../core/i18n';
import { AuthenticationSession } from '../core/auth';

@Component({
  selector: 'erp-app',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <a class="skip-link" href="#main">{{ i18n.t('context') }}</a>
    <header>
      <a class="brand" routerLink="/"
        ><span class="mark" aria-hidden="true">◈</span>{{ i18n.t('brand') }}</a
      >
      <button id="language" type="button" (click)="i18n.toggle()">{{ i18n.t('language') }}</button>
    </header>
    <div class="layout">
      <nav aria-label="Main">
        <a routerLink="/" routerLinkActive="selected" [routerLinkActiveOptions]="{ exact: true }">{{
          i18n.t('home')
        }}</a
        ><a routerLink="/workspace" routerLinkActive="selected">{{ i18n.t('workspace') }}</a>
      </nav>
      <main id="main">
        <div class="session" role="status">
          <span class="dot" aria-hidden="true"></span
          >{{ i18n.t(session.authenticated() ? 'activeSession' : 'session') }}
        </div>
        <router-outlet />
      </main>
    </div>
    <footer>{{ i18n.t('footer') }}</footer>
  `,
})
export class App {
  readonly i18n = inject(I18n);
  readonly session = inject(AuthenticationSession);
}
