namespace Erp.Finance.Contracts;

public sealed record CorrectJournalRequest(DateOnly? PostingDate, string Reason, DraftLineRequest[] Lines);
public sealed record JournalCorrectionView(Guid Id, Guid OriginalJournalId, Guid ReversalJournalId, Guid ReplacementJournalId,
    DateOnly PostingDate, string Reason, string ActorIssuer, string ActorSubject, DateTimeOffset Timestamp, string RequestId, string CorrelationId);
public sealed record JournalCorrectedV1(Guid CorrectionId, Guid OriginalJournalId, Guid ReversalJournalId, Guid ReplacementJournalId)
{
    public const string EventType = "finance.journal-corrected.v1";
}
