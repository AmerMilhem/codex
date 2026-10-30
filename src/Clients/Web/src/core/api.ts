import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap } from 'rxjs';
import { AuthenticationSession } from './auth';
import { I18n, MessageKey } from './i18n';

export interface ApiProblem {
  code: string;
  errorId: string;
  requestId: string;
  correlationId: string;
  traceId: string;
  status: number;
}
export interface CompanyContext {
  tenantId: string;
  companyId: string;
  identityMode: string;
  permissions: string[];
}
export const requestContextInterceptor: HttpInterceptorFn = (request, next) => {
  // Never attach session credentials to a third-party URL.
  if (!request.url.startsWith('/api/')) return next(request);
  const auth = inject(AuthenticationSession);
  const correlation = request.headers.get('X-Correlation-ID') ?? crypto.randomUUID();
  return auth.accessToken().pipe(
    switchMap((token) =>
      next(
        request.clone({
          setHeaders: {
            'X-Correlation-ID': correlation,
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
        }),
      ),
    ),
  );
};
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private http = inject(HttpClient);
  companyContext(tenant: string, company: string) {
    return this.http.get<CompanyContext>(
      `/api/v1/tenants/${encodeURIComponent(tenant)}/companies/${encodeURIComponent(company)}/context`,
    );
  }
}
const errorMessages: Record<string, MessageKey> = {
  'finance.journal_already_corrected': 'correctionNotAllowed',
  'finance.journal_correction_not_allowed': 'correctionNotAllowed',
  'finance.correction_period_closed': 'periodClosed',

  'finance.book_code_invalid': 'bookCodeInvalid',
  'finance.book_name_invalid': 'bookNameInvalid',
  'finance.book_currency_invalid': 'bookCurrencyInvalid',
  'finance.book_duplicate': 'bookDuplicate',
  'finance.account_code_invalid': 'accountCodeInvalid',
  'finance.account_name_invalid': 'accountNameInvalid',
  'finance.account_duplicate': 'accountDuplicate',
  'finance.period_invalid_dates': 'periodInvalidDates',
  'finance.period_duplicate': 'periodDuplicate',
  'finance.period_overlap': 'periodOverlap',
  'finance.period_already_closed': 'periodAlreadyClosed',
  'finance.period_close_not_allowed': 'periodCloseNotAllowed',
  'finance.period_reconciliation_failed': 'periodReconciliationFailed',
  'finance.period_concurrency_conflict': 'periodConcurrencyConflict',
  'finance.period_invalid_state': 'periodInvalidState',
  'finance.journal_not_posted': 'journalNotPosted',
  'finance.journal_already_reversed': 'alreadyReversed',
  'finance.journal_reversal_not_allowed': 'reversalNotAllowed',
  'finance.reversal_date_invalid': 'reversalDateInvalid',
  'finance.reversal_period_closed': 'reversalPeriodClosed',
  'finance.report_invalid_range': 'reportInvalidRange',
  'auth.scope_denied': 'denied',
  'auth.required': 'session',
  'scope.conflicting_context': 'invalid',
  'request.invalid_shape': 'invalid',
  'concurrency.revision_mismatch': 'conflict',
  'resource.not_found': 'notfound',
  'idempotency.key_reused': 'duplicate',
  'finance.period_closed': 'periodClosed',
  'finance.period_invalid': 'periodInvalid',
  'finance.journal_unbalanced': 'unbalanced',
  'finance.account_inactive': 'accountInactive',
  'finance.account_not_postable': 'accountNotPostable',
  'finance.account_invalid': 'accountInvalid',
  'finance.journal_invalid_line': 'lineInvalid',
  'finance.journal_already_posted': 'alreadyPosted',
  'finance.currency_invalid': 'currencyInvalid',
  'finance.posting_date_required': 'dateRequired',
  'finance.book_not_configured': 'bookMissing',
};
@Injectable({ providedIn: 'root' })
export class ApiErrors {
  private i18n = inject(I18n);
  fromCode(code: string): string {
    return this.i18n.t(errorMessages[code] ?? 'failed');
  }
  message(error: HttpErrorResponse): string {
    const code: unknown = error.error?.code;
    return this.i18n.t(typeof code === 'string' ? (errorMessages[code] ?? 'failed') : 'failed');
  }
}
