namespace Erp.Finance.Contracts;

public static class PeriodPermissions
{
    public const string View = "Finance.Period.View";
    public const string Create = "Finance.Period.Create";
    public const string Close = "Finance.Period.Close";
}

public sealed record CreateFiscalPeriodRequest(DateOnly StartDate, DateOnly EndDate);
public sealed record ClosePeriodRequest(string? Comment);
public sealed record FiscalPeriodView(Guid Id, Guid TenantId, Guid CompanyId, int FiscalYear, DateOnly StartDate,
    DateOnly EndDate, string Status, long Revision, DateTimeOffset? ClosedAt, string? ClosedByIssuer,
    string? ClosedBySubject, string? CloseComment, string? CloseReconciliation,
    DateTimeOffset? CreatedAt = null, string? CreatedByIssuer = null, string? CreatedBySubject = null);
public sealed record PeriodCloseCheck(string Code, bool Passed, string Expected, string Actual);
public sealed record PeriodClosePreview(FiscalPeriodView Period, bool Passed, string PeriodDebit, string PeriodCredit,
    IReadOnlyList<PeriodCloseCheck> Checks);
public sealed record FiscalPeriodClosedV1(Guid TenantId, Guid CompanyId, Guid FiscalPeriodId, DateOnly StartDate,
    DateOnly EndDate, DateTimeOffset ClosedAt)
{
    public const string EventType = "finance.fiscal-period-closed.v1";
}
public sealed record FiscalPeriodCreatedV1(Guid TenantId, Guid CompanyId, Guid FiscalPeriodId, DateOnly StartDate,
    DateOnly EndDate, DateTimeOffset CreatedAt)
{
    public const string EventType = "finance.fiscal-period-created.v1";
}
