using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.UnitTests;

public sealed class AccountingBookSetupTests
{
    [Theory]
    [InlineData(" primary ", "PRIMARY", "JOD", 3)]
    [InlineData("a-1", "A-1", "usd", 2)]
    [InlineData("JPN", "JPN", "JPY", 0)]
    public void Canonical_code_and_functional_currency_have_explicit_precision(string code, string expected,
        string currency, int minorUnits)
    {
        var result = AccountingBookSetup.Validate(code, " Primary Book ", currency);
        Assert.Equal((expected, "Primary Book", currency.Trim().ToUpperInvariant(), minorUnits), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A B")]
    [InlineData("A/B")]
    [InlineData("é")]
    public void Invalid_code_is_rejected(string code) => Assert.Equal(BookSetupErrors.InvalidCode,
        Assert.Throws<FoundationException>(() => AccountingBookSetup.Validate(code, "Book", "JOD")).Error);

    [Fact]
    public void Name_and_currency_are_required()
    {
        Assert.Equal(BookSetupErrors.InvalidName,
            Assert.Throws<FoundationException>(() => AccountingBookSetup.Validate("PRIMARY", " ", "JOD")).Error);
        Assert.Equal(BookSetupErrors.InvalidCurrency,
            Assert.Throws<FoundationException>(() => AccountingBookSetup.Validate("PRIMARY", "Book", "ZZZ")).Error);
    }
}
