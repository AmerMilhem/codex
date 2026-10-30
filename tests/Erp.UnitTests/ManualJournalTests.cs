using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.UnitTests;

public sealed class ManualJournalTests
{
    [Theory]
    [InlineData("0.123", "0.123")]
    [InlineData("999999999999999999.999999", "999999999999999999.999999")]
    [InlineData("100.000000", "100")]
    public void Decimal_wire_roundtrip_is_exact(string input, string expected) => Assert.Equal(expected, JournalMoney.Format(JournalMoney.Parse(input)));
    [Theory]
    [InlineData("1.0000001")]
    [InlineData("1000000000000000000")]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("NaN")]
    public void Invalid_money_is_rejected_before_provider_rounding(string value) => Assert.Throws<FoundationException>(() => JournalMoney.Parse(value));
    [Theory]
    [InlineData("1.2345", "1.234")]
    [InlineData("1.2355", "1.236")]
    [InlineData("-1.2345", "-1.234")]
    public void Versioned_internal_midpoint_policy_is_to_even(string input, string expected) => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), JournalMoney.Quantize(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture), 3));
    [Fact]
    public void Posted_aggregate_rejects_edit_and_second_post()
    {
        var accounts = new[] { new AccountSnapshot(Guid.NewGuid(), AccountStatus.Active, true), new AccountSnapshot(Guid.NewGuid(), AccountStatus.Active, true) };
        var date = new DateOnly(2026, 10, 18); var period = new[] { new PeriodSnapshot(Guid.NewGuid(), 2026, FiscalPeriodStatus.Open, date, date) }; var book = new BookPolicy("JOD", 3);
        var journal = JournalEntry.Create(Guid.NewGuid(), Guid.NewGuid(), date, "JOD", "", "", [new(null, accounts[0].Id, 100, 0, ""), new(null, accounts[1].Id, 0, 100, "")], DateTimeOffset.UtcNow);
        journal.Post(1, book, period, accounts, 1, "tests", "actor", DateTimeOffset.UtcNow);
        Assert.Equal("MJ-2026-000001", journal.JournalNumber);
        Assert.Equal(FinanceErrors.AlreadyPosted, Assert.Throws<FoundationException>(() => journal.Edit(2, date, "JOD", "", "", [])).Error);
        Assert.Equal(FinanceErrors.AlreadyPosted, Assert.Throws<FoundationException>(() => journal.Post(2, book, period, accounts, 2, "tests", "actor", DateTimeOffset.UtcNow)).Error);
    }
}
