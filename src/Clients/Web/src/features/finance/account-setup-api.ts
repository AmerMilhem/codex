import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { JournalApi, Receipt } from './journal-api';

export interface SetupAccount {
  id: string;
  code: string;
  name: string;
  status: 'Active' | 'Inactive';
  postingAllowed: boolean;
  createdAt: string | null;
  createdByIssuer: string | null;
  createdBySubject: string | null;
}

@Injectable({ providedIn: 'root' })
export class AccountSetupApi {
  private readonly http = inject(HttpClient);
  private readonly journals = inject(JournalApi);
  private base(tenant: string, company: string): string {
    return this.journals.base(tenant, company) + '/accounts';
  }
  list(tenant: string, company: string) {
    return this.http.get<SetupAccount[]>(this.base(tenant, company));
  }
  create(
    tenant: string,
    company: string,
    code: string,
    name: string,
    postingAllowed: boolean,
    key: string,
  ) {
    return this.http.post<Receipt>(
      this.base(tenant, company),
      { code, name, postingAllowed },
      { headers: { 'Idempotency-Key': key } },
    );
  }
}
