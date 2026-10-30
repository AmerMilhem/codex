import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { JournalApi, Receipt } from './journal-api';

export interface AccountingBook {
  id: string;
  tenantId: string;
  companyId: string;
  code: string;
  name: string;
  functionalCurrency: string;
  minorUnits: number;
  status: 'Active';
  createdAt: string | null;
  createdByIssuer: string | null;
  createdBySubject: string | null;
}
@Injectable({ providedIn: 'root' })
export class AccountingBookApi {
  private readonly http = inject(HttpClient);
  private readonly journals = inject(JournalApi);
  private base(tenant: string, company: string) {
    return this.journals.base(tenant, company) + '/books';
  }
  list(tenant: string, company: string) {
    return this.http.get<AccountingBook[]>(this.base(tenant, company));
  }
  create(
    tenant: string,
    company: string,
    code: string,
    name: string,
    functionalCurrency: string,
    key: string,
  ) {
    return this.http.post<Receipt>(
      this.base(tenant, company),
      { code, name, functionalCurrency },
      { headers: { 'Idempotency-Key': key } },
    );
  }
}
