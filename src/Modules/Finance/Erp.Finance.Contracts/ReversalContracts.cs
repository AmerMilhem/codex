namespace Erp.Finance.Contracts;

public sealed record ReverseJournalRequest(DateOnly? PostingDate, string Reason);
public sealed record JournalReversalView(Guid OriginalJournalId, string OriginalJournalNumber, Guid ReversalJournalId, string ReversalJournalNumber,
    DateOnly PostingDate, string Reason, string ActorIssuer, string ActorSubject, DateTimeOffset Timestamp, string RequestId, string CorrelationId);
public sealed record JournalEntryReversedV1(Guid OriginalJournalId, Guid ReversalJournalId, string ReversalJournalNumber, DateOnly PostingDate, string Currency)
{
    public const string EventType = "finance.journal-entry-reversed.v1";
}
