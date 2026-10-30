using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Kernel;
namespace Erp.UnitTests;

public sealed class GeneralLedgerTests
{
    [Theory]
    [InlineData(-1, 50)]
    [InlineData(0, 0)]
    [InlineData(0, 201)]
    public void Invalid_pagination_rejected(int offset, int limit) => Assert.Throws<FoundationException>(() => GeneralLedgerRequest.Create(Guid.NewGuid(), new(2026, 1, 1), new(2026, 1, 31), offset, limit));
    [Fact]
    public void Missing_account_and_inverted_dates_rejected()
    {
        Assert.Throws<FoundationException>(() => GeneralLedgerRequest.Create(null, new(2026, 1, 1), new(2026, 1, 31), 0, 50));
        Assert.Throws<FoundationException>(() => GeneralLedgerRequest.Create(Guid.NewGuid(), new(2026, 2, 1), new(2026, 1, 31), 0, 50));
    }
    [Theory]
    [InlineData("100", "40", "25", "115", true)]
    [InlineData("101", "40", "25", "116", false)]
    [InlineData("100", "41", "25", "116", false)]
    [InlineData("100", "40", "26", "114", false)]
    [InlineData("100", "40", "25", "999", false)]
    public void Independent_trial_balance_mismatch_is_observable(string opening, string debit, string credit, string closing, bool expected)
    {
        var r = GeneralLedgerReconciliation.Check(opening, debit, credit, closing, new("100", "0", "40", "25", "115", "0"), opening, []);
        Assert.Equal(expected, r.Passed); if (!expected) Assert.Contains(r.Checks, c => !c.Passed);
    }
    [Fact]
    public void Incorrect_page_running_accumulation_is_failure()
    {
        GeneralLedgerMovement line = new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 1, 1), "MJ-1", "", "", "", "40", "0", "139");
        var r = GeneralLedgerReconciliation.Check("100", "40", "0", "140", new("100", "0", "40", "0", "140", "0"), "100", [line]);
        Assert.False(r.Passed); Assert.Contains(r.Checks, x => x.Code == "GL_PAGE_RUNNING_EQUATION" && !x.Passed);
    }
}
