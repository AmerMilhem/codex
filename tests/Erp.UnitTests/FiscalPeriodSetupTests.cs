using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.UnitTests;

public sealed class FiscalPeriodSetupTests
{
    [Fact]
    public void Valid_dates_derive_start_year() => Assert.Equal(2027,
        FiscalPeriodSetup.Validate(new(2027, 12, 31), new(2028, 1, 31)));

    [Theory]
    [InlineData(2027, 2, 1, 2027, 1, 31)]
    [InlineData(1, 1, 1, 1, 1, 1)]
    public void Reversed_or_default_dates_are_rejected(int sy, int sm, int sd, int ey, int em, int ed) =>
        Assert.Equal(PeriodSetupErrors.InvalidDates,
            Assert.Throws<FoundationException>(() => FiscalPeriodSetup.Validate(new(sy, sm, sd),
                sy == 1 ? default : new DateOnly(ey, em, ed))).Error);

    [Fact]
    public void Inclusive_endpoints_overlap_but_adjacent_next_day_does_not()
    {
        Assert.True(FiscalPeriodSetup.Overlaps(new(2027, 1, 1), new(2027, 1, 31), new(2027, 1, 31), new(2027, 2, 28)));
        Assert.False(FiscalPeriodSetup.Overlaps(new(2027, 1, 1), new(2027, 1, 31), new(2027, 2, 1), new(2027, 2, 28)));
    }
}
