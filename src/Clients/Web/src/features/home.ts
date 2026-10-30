import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n } from '../core/i18n';
@Component({
  standalone: true,
  imports: [RouterLink],
  template: `<section class="welcome">
    <p class="eyebrow">ERP WORKSPACE</p>
    <h1>{{ i18n.t('welcome') }}</h1>
    <p>{{ i18n.t('description') }}</p>
    <a class="primary" routerLink="/workspace"
      >{{ i18n.t('open') }} <span aria-hidden="true">↗</span></a
    >
  </section>`,
})
export class Home {
  readonly i18n = inject(I18n);
}
