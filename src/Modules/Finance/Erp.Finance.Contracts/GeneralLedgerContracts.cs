namespace Erp.Finance.Contracts;

// Signed net balances: positive = debit, negative = credit; all monetary values are exact decimal strings.
public sealed record GeneralLedgerMovement(Guid LineId, Guid JournalId, int Position, DateOnly PostingDate,
    string JournalNumber, string Reference, string Description, string JournalDescription, string Debit, string Credit, string RunningBalance);
public sealed record GeneralLedgerReport(Guid TenantId, Guid CompanyId, Guid AccountId, string AccountCode, string AccountName,
    DateOnly StartDate, DateOnly EndDate, string Currency, int MinorUnits, string OpeningScope, DateTimeOffset GeneratedAt,
    int Offset, int Limit, long TotalCount, string OpeningBalance, string PeriodDebit, string PeriodCredit, string ClosingBalance,
    string PageOpeningBalance, IReadOnlyList<GeneralLedgerMovement> Movements, TrialBalanceReconciliation Reconciliation);
