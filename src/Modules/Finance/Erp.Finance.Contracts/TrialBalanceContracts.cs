namespace Erp.Finance.Contracts;

// All aggregate amounts remain exact invariant decimal strings, including totals larger than one journal's numeric(24,6).
public sealed record TrialBalanceAmounts(string OpeningDebit, string OpeningCredit, string PeriodDebit, string PeriodCredit,
    string ClosingDebit, string ClosingCredit);
public sealed record TrialBalanceAccount(Guid AccountId, string AccountCode, string AccountName, TrialBalanceAmounts Amounts);
public sealed record TrialBalanceLedgerControl(TrialBalanceAmounts Totals, string OpeningLedgerDebit, string OpeningLedgerCredit,
    string ClosingLedgerDebit, string ClosingLedgerCredit, long PostedJournalCount, long PostedLineCount, long InvalidJournalCount);
public sealed record TrialBalanceCheck(string Code, bool Passed, string Expected, string Actual);
public sealed record TrialBalanceReconciliation(bool Passed, IReadOnlyList<TrialBalanceCheck> Checks);
public sealed record TrialBalanceReport(Guid TenantId, Guid CompanyId, DateOnly StartDate, DateOnly EndDate,
    string Currency, int MinorUnits, string OpeningScope, DateTimeOffset GeneratedAt, IReadOnlyList<TrialBalanceAccount> Accounts,
    TrialBalanceAmounts Totals, TrialBalanceLedgerControl LedgerControl, TrialBalanceReconciliation Reconciliation);
