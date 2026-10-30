import {
  Directive,
  Injectable,
  TemplateRef,
  ViewContainerRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { Observable, of } from 'rxjs';

// Production OIDC/MFA is pending. Replace this adapter through DI; no token storage or fake login.
@Injectable({ providedIn: 'root' })
export class AuthenticationSession {
  private token: string | null = null;
  readonly authenticated = signal(false);
  readonly permissions = signal<ReadonlySet<string>>(new Set());
  readonly tenant = signal('');
  readonly company = signal('');
  beginLocalSession(token: string): void {
    this.signOut();
    this.token = token;
  }
  verified(tenant: string, company: string, permissions: string[]): void {
    this.tenant.set(tenant);
    this.company.set(company);
    this.permissions.set(new Set(permissions));
    this.authenticated.set(true);
  }
  signOut(): void {
    this.token = null;
    this.authenticated.set(false);
    this.permissions.set(new Set());
    this.tenant.set('');
    this.company.set('');
  }
  accessToken(): Observable<string | null> {
    return of(this.token);
  }
}
@Injectable({ providedIn: 'root' })
export class Authorization {
  private session = inject(AuthenticationSession);
  can(permission: string): boolean {
    return this.session.authenticated() && this.session.permissions().has(permission);
  }
}
@Directive({ selector: '[erpCan]', standalone: true })
export class CanDirective {
  erpCan = input.required<string>();
  private authorization = inject(Authorization);
  private template = inject(TemplateRef<unknown>);
  private container = inject(ViewContainerRef);
  constructor() {
    effect(() => {
      this.container.clear();
      if (this.authorization.can(this.erpCan())) this.container.createEmbeddedView(this.template);
    });
  }
}
