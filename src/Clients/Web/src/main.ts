import { provideZonelessChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { App } from './shell/app';
import { routes } from './shell/routes';
import { requestContextInterceptor } from './core/api';

bootstrapApplication(App, {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([requestContextInterceptor])),
  ],
}).catch(() => {
  document.body.textContent = 'تعذر تحميل التطبيق / Unable to load application';
});
