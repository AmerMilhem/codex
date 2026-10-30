import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { JournalApi } from './journal-api';

export interface TrialBalanceAmounts {
  openingDebit: string;
  openingCredit: string;
  periodDebit: string;
  periodCredit: string;
  closingDebit: string;
  closingCredit: string;
}
export interface TrialBalanceReport {
  tenantId: string;
  companyId: string;
  startDate: string;
  endDate: string;
  currency: string;
  minorUnits: number;
  openingScope: string;
  generatedAt: string;
  accounts: {
    accountId: string;
    accountCode: string;
    accountName: string;
    amounts: TrialBalanceAmounts;
  }[];
  totals: TrialBalanceAmounts;
  ledgerControl: {
    totals: TrialBalanceAmounts;
    openingLedgerDebit: string;
    openingLedgerCredit: string;
    closingLedgerDebit: string;
    closingLedgerCredit: string;
    postedJournalCount: number;
    postedLineCount: number;
    invalidJournalCount: number;
  };
  reconciliation: {
    passed: boolean;
    checks: { code: string; passed: boolean; expected: string; actual: string }[];
  };
}
@Injectable({ providedIn: 'root' })
export class TrialBalanceApi {
  private http = inject(HttpClient);
  private journals = inject(JournalApi);
  run(tenant: string, company: string, startDate: string, endDate: string) {
    return this.http.get<TrialBalanceReport>(
      this.journals.base(tenant, company) + '/trial-balance',
      {
        params: new HttpParams().set('startDate', startDate).set('endDate', endDate),
      },
    );
  }
}
