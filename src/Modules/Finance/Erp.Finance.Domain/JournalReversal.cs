using Erp.Kernel;
namespace Erp.Finance.Domain;

public static class ReversalErrors
{
    public static readonly ErrorDefinition NotPosted = new("FIN_JOURNAL_NOT_POSTED", "finance.journal_not_posted", 422);
    public static readonly ErrorDefinition AlreadyReversed = new("FIN_JOURNAL_ALREADY_REVERSED", "finance.journal_already_reversed", 409);
    public static readonly ErrorDefinition NotAllowed = new("FIN_JOURNAL_REVERSAL_NOT_ALLOWED", "finance.journal_reversal_not_allowed", 422);
    public static readonly ErrorDefinition DateInvalid = new("FIN_REVERSAL_DATE_INVALID", "finance.reversal_date_invalid", 422);
    public static readonly ErrorDefinition PeriodClosed = new("FIN_REVERSAL_PERIOD_CLOSED", "finance.reversal_period_closed", 422);
}
// Immutable relationship; neither journal's status/revision/lines are changed to describe reversal.
public sealed class JournalReversal
{
    private JournalReversal() { }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OriginalJournalId { get; private set; }
    public Guid ReversalJournalId { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public string Reason { get; private set; } = "";
    public string ActorIssuer { get; private set; } = "";
    public string ActorSubject { get; private set; } = "";
    public string RequestId { get; private set; } = "";
    public string CorrelationId { get; private set; } = "";
    public DateTimeOffset Timestamp { get; private set; }
    public static JournalEntry CreateOpposite(JournalEntry original, long expected, bool alreadyReversed, bool isReversal,
        DateOnly? date, string? reason, DateTimeOffset now)
    {
        if (original.Status != JournalStatus.Posted) throw new FoundationException(ReversalErrors.NotPosted);
        original.ExpectRevision(expected);
        if (isReversal) throw new FoundationException(ReversalErrors.NotAllowed);
        if (alreadyReversed) throw new FoundationException(ReversalErrors.AlreadyReversed);
        if (date is null) throw new FoundationException(ReversalErrors.DateInvalid);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new FoundationException(Errors.VALIDATION_FAILED);
        return JournalEntry.Create(original.TenantId, original.CompanyId, date, original.Currency, original.Reference, reason,
            original.Lines.OrderBy(x => x.Position).Select(x => new LineInput(null, x.AccountId, x.Credit, x.Debit, x.Description)).ToArray(), now);
    }
    public static JournalReversal Link(JournalEntry original, JournalEntry reversal, string reason, string issuer, string subject, string request, string correlation, DateTimeOffset now)
    {
        if (original.Id == reversal.Id || original.TenantId != reversal.TenantId || original.CompanyId != reversal.CompanyId
            || original.Status != JournalStatus.Posted || reversal.Status != JournalStatus.Posted || original.Currency != reversal.Currency
            || original.Lines.Count != reversal.Lines.Count || original.Lines.OrderBy(x => x.Position).Zip(reversal.Lines.OrderBy(x => x.Position))
                .Any(x => x.First.AccountId != x.Second.AccountId || x.First.Debit != x.Second.Credit || x.First.Credit != x.Second.Debit))
            throw new FoundationException(ReversalErrors.NotAllowed);
        return new()
        {
            TenantId = original.TenantId,
            CompanyId = original.CompanyId,
            OriginalJournalId = original.Id,
            ReversalJournalId = reversal.Id,
            PostingDate = reversal.PostingDate!.Value,
            Reason = reason,
            ActorIssuer = issuer,
            ActorSubject = subject,
            RequestId = request,
            CorrelationId = correlation,
            Timestamp = now
        };
    }
}
