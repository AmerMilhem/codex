using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.UnitTests;

public sealed class TrialBalanceTests
{
    [Theory]
    [InlineData(null, "2026-01-31")]
    [InlineData("2026-01-01", null)]
    [InlineData("2026-02-01", "2026-01-01")]
    public void Missing_or_inverted_dates_have_stable_error(string? start, string? end) =>
        Assert.Equal(TrialBalanceRange.Invalid, Assert.Throws<FoundationException>(() => TrialBalanceRange.Create(start is null ? null : DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end))).Error);
    [Theory]
    [InlineData("999999999999999999999999999999.123456")]
    [InlineData("-0.000001")]
    [InlineData("100")]
    public void Aggregate_precision_is_exact_beyond_decimal_capacity(string value) => Assert.Equal(value, TrialBalanceArithmetic.Format(TrialBalanceArithmetic.Units(value)));
    [Fact]
    public void Equal_but_wrong_projection_totals_do_not_pass_independent_ledger_reconciliation()
    {
        var wrong = new TrialBalanceAmounts("0", "0", "99", "99", "99", "99");
        var expected = new TrialBalanceAmounts("0", "0", "100", "100", "100", "100");
        var result = TrialBalanceArithmetic.Reconcile([], wrong, new(expected, "0", "0", "100", "100", 1, 2, 0));
        Assert.False(result.Passed); Assert.Contains(result.Checks, x => x.Code == "TB_PERIOD_DEBIT_MATCH" && !x.Passed);
    }
    [Fact]
    public void Incorrect_per_account_equation_fails_even_if_grand_totals_balance()
    {
        var rows = new[] { new TrialBalanceAccount(Guid.NewGuid(), "A", "A", new("0", "0", "100", "0", "90", "0")), new TrialBalanceAccount(Guid.NewGuid(), "B", "B", new("0", "0", "0", "100", "0", "90")) };
        var totals = TrialBalanceArithmetic.Total(rows);
        var result = TrialBalanceArithmetic.Reconcile(rows, totals, new(totals, "0", "0", "100", "100", 1, 2, 0));
        Assert.False(result.Passed); Assert.Contains(result.Checks, x => x.Code == "TB_ACCOUNT_EQUATIONS" && !x.Passed);
    }
}
