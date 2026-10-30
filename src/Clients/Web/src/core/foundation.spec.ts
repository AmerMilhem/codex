import { Component, signal, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ApiClient, ApiErrors, requestContextInterceptor } from './api';
import { AuthenticationSession, Authorization, CanDirective } from './auth';
import { I18n } from './i18n';

describe('Foundation contracts', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(withInterceptors([requestContextInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  it('sets Arabic RTL and English LTR and persists the selection', () => {
    const i18n = TestBed.inject(I18n);
    expect(document.documentElement.dir).toBe('rtl');
    i18n.toggle();
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');
    expect(localStorage.getItem('erp.language')).toBe('en');
  });
  it('does not grant permissions without an authenticated session', () => {
    const session = TestBed.inject(AuthenticationSession);
    session.permissions.set(new Set(['read']));
    expect(TestBed.inject(Authorization).can('read')).toBe(false);
    session.authenticated.set(true);
    expect(TestBed.inject(Authorization).can('read')).toBe(true);
  });
  it('removes restricted content until an explicit permission is present', async () => {
    @Component({
      standalone: true,
      imports: [CanDirective],
      template: '<span *erpCan="\'read\'">restricted</span>',
    })
    class Fixture {}
    const view = TestBed.createComponent(Fixture);
    await view.whenStable();
    expect(view.nativeElement.textContent).not.toContain('restricted');
    const session = TestBed.inject(AuthenticationSession);
    session.authenticated.set(true);
    session.permissions.set(new Set(['read']));
    await view.whenStable();
    expect(view.nativeElement.textContent).toContain('restricted');
  });
  it('adds a correlation ID to same-origin API calls without inventing authentication', () => {
    TestBed.inject(ApiClient).companyContext('tenant', 'company').subscribe();
    const request = TestBed.inject(HttpTestingController).expectOne(
      '/api/v1/tenants/tenant/companies/company/context',
    );
    expect(request.request.headers.get('X-Correlation-ID')).toMatch(/^[a-f0-9-]{36}$/);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });
  it('uses stable error codes and never server-provided prose', () => {
    const errors = TestBed.inject(ApiErrors);
    const i18n = TestBed.inject(I18n);
    const problem = new HttpErrorResponse({
      status: 403,
      error: { code: 'auth.scope_denied', detail: 'SQL private data' },
    });
    expect(errors.message(problem)).toBe(i18n.t('denied'));
    expect(errors.message(problem)).not.toContain('SQL');
    expect(errors.message(new HttpErrorResponse({ error: { code: 'future.unknown' } }))).toBe(
      i18n.t('failed'),
    );
  });
});
