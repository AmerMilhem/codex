import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { JournalApi, Receipt } from './journal-api';

export interface FiscalPeriod {
  id: string;
  tenantId: string;
  companyId: string;
  fiscalYear: number;
  startDate: string;
  endDate: string;
  status: 'Open' | 'Closed';
  revision: number;
  closedAt: string | null;
  closedByIssuer: string | null;
  closedBySubject: string | null;
  closeComment: string | null;
  createdAt?: string | null;
  createdByIssuer?: string | null;
  createdBySubject?: string | null;
}
export interface PeriodCloseCheck {
  code: string;
  passed: boolean;
  expected: string;
  actual: string;
}
export interface PeriodClosePreview {
  period: FiscalPeriod;
  passed: boolean;
  periodDebit: string;
  periodCredit: string;
  checks: PeriodCloseCheck[];
}

@Injectable({ providedIn: 'root' })
export class FiscalPeriodApi {
  private readonly http = inject(HttpClient);
  private readonly journals = inject(JournalApi);
  base(tenant: string, company: string): string {
    return this.journals.base(tenant, company);
  }
  list(tenant: string, company: string) {
    return this.http.get<FiscalPeriod[]>(this.base(tenant, company) + '/periods');
  }
  create(tenant: string, company: string, startDate: string, endDate: string, key: string) {
    return this.http.post<Receipt>(
      this.base(tenant, company) + '/periods',
      { startDate, endDate },
      { headers: { 'Idempotency-Key': key } },
    );
  }
  preview(tenant: string, company: string, id: string) {
    return this.http.get<PeriodClosePreview>(
      this.base(tenant, company) + '/periods/' + encodeURIComponent(id) + '/pre-close',
    );
  }
  close(
    tenant: string,
    company: string,
    id: string,
    revision: number,
    key: string,
    comment: string | null,
  ) {
    return this.http.post<Receipt>(
      this.base(tenant, company) + '/periods/' + encodeURIComponent(id) + '/close',
      { comment },
      { headers: { 'If-Match': `"${revision}"`, 'Idempotency-Key': key } },
    );
  }
}
