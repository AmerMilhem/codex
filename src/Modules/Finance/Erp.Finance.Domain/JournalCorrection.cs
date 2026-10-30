using Erp.Kernel;
namespace Erp.Finance.Domain;

public static class CorrectionErrors
{
    public static readonly ErrorDefinition AlreadyCorrected = new("FIN_JOURNAL_ALREADY_CORRECTED", "finance.journal_already_corrected", 409);
    public static readonly ErrorDefinition NotAllowed = new("FIN_JOURNAL_CORRECTION_NOT_ALLOWED", "finance.journal_correction_not_allowed", 422);
    public static readonly ErrorDefinition PeriodClosed = new("FIN_CORRECTION_PERIOD_CLOSED", "finance.correction_period_closed", 422);
}
public sealed class JournalCorrection
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OriginalJournalId { get; set; }
    public Guid ReversalJournalId { get; set; }
    public Guid ReplacementJournalId { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Reason { get; set; } = "";
    public string ActorIssuer { get; set; } = "";
    public string ActorSubject { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}
