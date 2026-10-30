namespace Erp.Finance.Contracts;

public static class JournalPermissions
{
    public const string View = "Finance.Journal.View";
    public const string Create = "Finance.Journal.Create";
    public const string Edit = "Finance.Journal.Edit";
    public const string Correct = "Finance.Journal.Correct";
    public const string Reverse = "Finance.Journal.Reverse";
    public const string Post = "Finance.Journal.Post";
}
// Money is a bounded invariant decimal STRING on the wire, never a JSON floating-point number.
public sealed record DraftLineRequest(Guid? Id, Guid AccountId, string Debit, string Credit, string Description);
public sealed record SaveDraftRequest(DateOnly? PostingDate, string Currency, string Reference, string Description, DraftLineRequest[] Lines);
public sealed record JournalLineView(Guid Id, int Position, Guid AccountId, string AccountCode, string AccountName, string Debit, string Credit, string Description);
public sealed record JournalView(Guid Id, string Status, long Revision, DateOnly? PostingDate, string Currency, string Reference, string Description,
    string? JournalNumber, string? PostedByIssuer, string? PostedBySubject, DateTimeOffset? PostedAt, string TotalDebit, string TotalCredit,
    string Difference, string RoundingPolicy, IReadOnlyList<JournalLineView> Lines, JournalReversalView? Reversal = null, JournalCorrectionView? Correction = null);
public sealed record ValidationIssue(string ErrorId, string Code, string Field, Guid? LineId);
public sealed record JournalValidation(long Revision, bool Valid, IReadOnlyList<ValidationIssue> Issues);
public sealed record AccountView(Guid Id, string Code, string Name, string Status, bool PostingAllowed);
public sealed record JournalSetup(string Currency, int MinorUnits, IReadOnlyList<AccountView> Accounts);
public sealed record JournalPage(IReadOnlyList<JournalView> Items, int Offset, int Limit, bool HasMore);
public sealed record JournalAuditView(string Action, string ActorIssuer, string ActorSubject, DateTimeOffset Timestamp, Guid TenantId, Guid CompanyId,
    string RequestId, string CorrelationId, JournalReversalView? Reversal = null);
public sealed record ReconciliationFailure(Guid JournalId, string Debit, string Credit, int LineCount);
public sealed record JournalReconciliation(bool Passed, long PostedCount, string TotalDebit, string TotalCredit, IReadOnlyList<ReconciliationFailure> Failures);
public sealed record JournalEntryPostedV1(Guid JournalId, string JournalNumber, DateOnly PostingDate, string Currency)
{
    public const string EventType = "finance.journal-entry-posted.v1";
}
