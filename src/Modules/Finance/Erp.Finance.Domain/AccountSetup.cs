using System.Text.RegularExpressions;
using Erp.Kernel;

namespace Erp.Finance.Domain;

public static class AccountSetupErrors
{
    public static readonly ErrorDefinition InvalidCode = new("FIN_ACCOUNT_CODE_INVALID", "finance.account_code_invalid", 422);
    public static readonly ErrorDefinition InvalidName = new("FIN_ACCOUNT_NAME_INVALID", "finance.account_name_invalid", 422);
    public static readonly ErrorDefinition Duplicate = new("FIN_ACCOUNT_DUPLICATE", "finance.account_duplicate", 409);
}

public static class AccountSetup
{
    // Canonical, case-insensitive business codes: trim outer whitespace, uppercase ASCII,
    // reject interior whitespace and all characters outside this documented alphabet.
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9._-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static (string Code, string Name) Validate(string? code, string? name)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? "";
        if (!CodePattern.IsMatch(normalizedCode)) throw new FoundationException(AccountSetupErrors.InvalidCode);
        var normalizedName = name?.Trim() ?? "";
        if (normalizedName.Length is < 1 or > 200) throw new FoundationException(AccountSetupErrors.InvalidName);
        return (normalizedCode, normalizedName);
    }
}
