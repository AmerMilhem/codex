using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.UnitTests;

public sealed class FiscalPeriodCloseTests
{
    [Fact]
    public void Open_period_with_matching_revision_is_eligible() => FiscalPeriodClose.RequireOpen(FiscalPeriodStatus.Open, 1, 1);

    [Fact]
    public void Closed_period_returns_stable_business_error() => Assert.Equal(PeriodCloseErrors.AlreadyClosed,
        Assert.Throws<FoundationException>(() => FiscalPeriodClose.RequireOpen(FiscalPeriodStatus.Closed, 2, 1)).Error);

    [Fact]
    public void Stale_revision_returns_period_concurrency_error() => Assert.Equal(PeriodCloseErrors.ConcurrencyConflict,
        Assert.Throws<FoundationException>(() => FiscalPeriodClose.RequireOpen(FiscalPeriodStatus.Open, 1, 2)).Error);

    [Theory]
    [InlineData(null, null)]
    [InlineData("  Closing  ", "Closing")]
    public void Comment_is_optional_and_trimmed(string? input, string? expected) => Assert.Equal(expected, FiscalPeriodClose.ValidateComment(input));

    [Fact]
    public void Whitespace_comment_is_invalid() => Assert.Equal(Errors.VALIDATION_FAILED,
        Assert.Throws<FoundationException>(() => FiscalPeriodClose.ValidateComment("   ")).Error);
}
