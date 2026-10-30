using Erp.Kernel;

namespace Erp.Finance.Domain;

public static class PeriodCloseErrors
{
    public static readonly ErrorDefinition AlreadyClosed = new("FIN_PERIOD_ALREADY_CLOSED", "finance.period_already_closed", 409);
    public static readonly ErrorDefinition NotAllowed = new("FIN_PERIOD_CLOSE_NOT_ALLOWED", "finance.period_close_not_allowed", 422);
    public static readonly ErrorDefinition ReconciliationFailed = new("FIN_PERIOD_RECONCILIATION_FAILED", "finance.period_reconciliation_failed", 422);
    public static readonly ErrorDefinition ConcurrencyConflict = new("FIN_PERIOD_CONCURRENCY_CONFLICT", "finance.period_concurrency_conflict", 409);
    public static readonly ErrorDefinition InvalidState = new("FIN_PERIOD_INVALID_STATE", "finance.period_invalid_state", 422);
}

// Period identity is Tenant + Company + accounting book, with one nonoverlapping date range.
// This transition never alters historical journal or period boundaries.
public static class FiscalPeriodClose
{
    public static void RequireOpen(FiscalPeriodStatus status, long revision, long expected)
    {
        if (status == FiscalPeriodStatus.Closed) throw new FoundationException(PeriodCloseErrors.AlreadyClosed);
        if (status != FiscalPeriodStatus.Open) throw new FoundationException(PeriodCloseErrors.InvalidState);
        if (revision != expected) throw new FoundationException(PeriodCloseErrors.ConcurrencyConflict);
    }

    public static string? ValidateComment(string? comment)
    {
        if (comment is not null && (comment.Length > 1000 || string.IsNullOrWhiteSpace(comment)))
            throw new FoundationException(Errors.VALIDATION_FAILED);
        return comment?.Trim();
    }
}
