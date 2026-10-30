using Erp.Kernel;

namespace Erp.Finance.Domain;

public static class PeriodSetupErrors
{
    public static readonly ErrorDefinition InvalidDates = new("FIN_PERIOD_INVALID_DATES", "finance.period_invalid_dates", 422);
    public static readonly ErrorDefinition Duplicate = new("FIN_PERIOD_DUPLICATE", "finance.period_duplicate", 409);
    public static readonly ErrorDefinition Overlap = new("FIN_PERIOD_OVERLAP", "finance.period_overlap", 409);
}

public static class FiscalPeriodSetup
{
    // Both bounds are inclusive. An endpoint shared by two periods is an overlap.
    public static int Validate(DateOnly start, DateOnly end)
    {
        if (start == default || end == default || start > end)
            throw new FoundationException(PeriodSetupErrors.InvalidDates);
        return start.Year; // Existing fiscal-year numbering uses the year in which the period starts.
    }

    public static bool Overlaps(DateOnly start, DateOnly end, DateOnly otherStart, DateOnly otherEnd) =>
        start <= otherEnd && otherStart <= end;
}
