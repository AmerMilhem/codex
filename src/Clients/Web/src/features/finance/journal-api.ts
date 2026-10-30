import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface DraftLine {
  id: string | null;
  accountId: string;
  debit: string;
  credit: string;
  description: string;
}
export interface DraftRequest {
  postingDate: string | null;
  currency: string;
  reference: string;
  description: string;
  lines: DraftLine[];
}
export interface ReversalRequest {
  postingDate: string;
  reason: string;
}
export interface JournalReversal {
  originalJournalId: string;
  originalJournalNumber: string;
  reversalJournalId: string;
  reversalJournalNumber: string;
  postingDate: string;
  reason: string;
  actorIssuer: string;
  actorSubject: string;
  timestamp: string;
  requestId: string;
  correlationId: string;
}
export interface JournalCorrection {
  id: string;
  originalJournalId: string;
  reversalJournalId: string;
  replacementJournalId: string;
  reason: string;
  postingDate: string;
}
export interface Journal extends DraftRequest {
  id: string;
  status: 'Draft' | 'Posted';
  correction?: JournalCorrection | null;
  reversal?: JournalReversal | null;
  revision: number;
  journalNumber: string | null;
  postedByIssuer: string | null;
  postedBySubject: string | null;
  postedAt: string | null;
  totalDebit: string;
  totalCredit: string;
  difference: string;
  roundingPolicy: string;
  lines: (DraftLine & { id: string; accountCode: string; accountName: string; position: number })[];
}
export interface Account {
  id: string;
  code: string;
  name: string;
  status: string;
  postingAllowed: boolean;
}
export interface JournalAudit {
  reversal?: JournalReversal | null;
  action: string;
  actorIssuer: string;
  actorSubject: string;
  timestamp: string;
  requestId: string;
  correlationId: string;
}
export interface ValidationResult {
  revision: number;
  valid: boolean;
  issues: { errorId: string; code: string; field: string; lineId: string | null }[];
}
export interface Receipt {
  reference: string;
  appliedRevision: number;
}

// Exact six-place scaled integer arithmetic. No Number/parseFloat money calculations.
export function units(value: string): bigint | null {
  if (!/^[0-9]{1,18}(\.[0-9]{1,6})?$/.test(value)) return null;
  const [integer, fraction = ''] = value.split('.');
  return BigInt(integer) * 1000000n + BigInt(fraction.padEnd(6, '0'));
}
export function amount(value: bigint): string {
  const sign = value < 0n ? '-' : '';
  const absolute = value < 0n ? -value : value;
  const fraction = (absolute % 1000000n).toString().padStart(6, '0').replace(/0+$/, '');
  return `${sign}${absolute / 1000000n}${fraction ? '.' + fraction : ''}`;
}
export function totals(lines: DraftLine[]): {
  debit: string;
  credit: string;
  difference: string;
  valid: boolean;
} {
  let debit = 0n,
    credit = 0n;
  for (const line of lines) {
    const d = units(line.debit),
      c = units(line.credit);
    if (d === null || c === null) return { debit: '—', credit: '—', difference: '—', valid: false };
    debit += d;
    credit += c;
  }
  return {
    debit: amount(debit),
    credit: amount(credit),
    difference: amount(debit - credit),
    valid: true,
  };
}
@Injectable({ providedIn: 'root' })
export class JournalApi {
  private http = inject(HttpClient);
  base(tenant: string, company: string) {
    return `/api/v1/tenants/${encodeURIComponent(tenant)}/companies/${encodeURIComponent(company)}/finance`;
  }
  setup(base: string) {
    return this.http.get<{ currency: string; minorUnits: number; accounts: Account[] }>(
      base + '/journal-setup',
    );
  }
  list(base: string, offset = 0) {
    return this.http.get<{ items: Journal[]; hasMore: boolean }>(
      base + `/journals?offset=${offset}&limit=25`,
    );
  }
  get(base: string, id: string) {
    return this.http.get<Journal>(base + '/journals/' + encodeURIComponent(id));
  }
  audit(base: string, id: string) {
    return this.http.get<JournalAudit[]>(base + '/journals/' + encodeURIComponent(id) + '/audit');
  }
  create(base: string, request: DraftRequest) {
    return this.http.post<Receipt>(base + '/journals/drafts', request);
  }
  update(base: string, id: string, revision: number, request: DraftRequest) {
    return this.http.put<Receipt>(
      base + '/journals/' + encodeURIComponent(id) + '/draft',
      request,
      { headers: { 'If-Match': `"${revision}"` } },
    );
  }
  validate(base: string, id: string, revision: number) {
    return this.http.post<ValidationResult>(
      base + '/journals/' + encodeURIComponent(id) + '/validate',
      {},
      { headers: { 'If-Match': `"${revision}"` } },
    );
  }
  post(base: string, id: string, revision: number, key: string) {
    return this.http.post<Receipt>(
      base + '/journals/' + encodeURIComponent(id) + '/post',
      {},
      { headers: { 'If-Match': `"${revision}"`, 'Idempotency-Key': key } },
    );
  }
  reverse(base: string, id: string, revision: number, key: string, request: ReversalRequest) {
    return this.http.post<Receipt>(
      base + '/journals/' + encodeURIComponent(id) + '/reverse',
      request,
      { headers: { 'If-Match': `"${revision}"`, 'Idempotency-Key': key } },
    );
  }
  reconcile(base: string) {
    return this.http.get<{
      passed: boolean;
      postedCount: number;
      totalDebit: string;
      totalCredit: string;
    }>(base + '/journal-reconciliation');
  }
}
