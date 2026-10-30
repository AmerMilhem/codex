using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.UnitTests;

public sealed class AccountSetupTests
{
    [Theory]
    [InlineData("  ab-01  ", "AB-01")]
    [InlineData("1_200.5", "1_200.5")]
    [InlineData("z", "Z")]
    public void Code_is_trimmed_uppercased_and_name_is_trimmed(string input, string expected)
    {
        Assert.Equal((expected, "Name"), AccountSetup.Validate(input, " Name "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A B")]
    [InlineData("A/B")]
    [InlineData("é")]
    public void Invalid_code_rejected(string code) => Assert.Equal(AccountSetupErrors.InvalidCode,
        Assert.Throws<FoundationException>(() => AccountSetup.Validate(code, "Name")).Error);

    [Fact]
    public void Empty_or_too_long_name_rejected()
    {
        foreach (var name in new[] { "  ", new string('x', 201) })
            Assert.Equal(AccountSetupErrors.InvalidName,
                Assert.Throws<FoundationException>(() => AccountSetup.Validate("1000", name)).Error);
    }
}
