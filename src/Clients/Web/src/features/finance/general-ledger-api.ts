import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { JournalApi } from './journal-api';
import { TrialBalanceReport } from './trial-balance-api';
export interface GeneralLedgerReport {
  tenantId: string;
  companyId: string;
  accountId: string;
  accountCode: string;
  accountName: string;
  startDate: string;
  endDate: string;
  currency: string;
  minorUnits: number;
  openingScope: string;
  generatedAt: string;
  offset: number;
  limit: number;
  totalCount: number;
  openingBalance: string;
  periodDebit: string;
  periodCredit: string;
  closingBalance: string;
  pageOpeningBalance: string;
  movements: {
    lineId: string;
    journalId: string;
    position: number;
    postingDate: string;
    journalNumber: string;
    reference: string;
    description: string;
    journalDescription: string;
    debit: string;
    credit: string;
    runningBalance: string;
  }[];
  reconciliation: TrialBalanceReport['reconciliation'];
}
@Injectable({ providedIn: 'root' })
export class GeneralLedgerApi {
  private http = inject(HttpClient);
  private journals = inject(JournalApi);
  run(
    tenant: string,
    company: string,
    accountId: string,
    startDate: string,
    endDate: string,
    offset: number,
    limit: number,
  ) {
    return this.http.get<GeneralLedgerReport>(
      `${this.journals.base(tenant, company)}/general-ledger`,
      {
        params: new HttpParams()
          .set('accountId', accountId)
          .set('startDate', startDate)
          .set('endDate', endDate)
          .set('offset', offset)
          .set('limit', limit),
      },
    );
  }
}
