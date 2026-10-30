using System.Text.RegularExpressions;
using Erp.Kernel;

namespace Erp.Finance.Domain;

public static class BookSetupErrors
{
    public static readonly ErrorDefinition InvalidCode = new("FIN_BOOK_CODE_INVALID", "finance.book_code_invalid", 422);
    public static readonly ErrorDefinition InvalidName = new("FIN_BOOK_NAME_INVALID", "finance.book_name_invalid", 422);
    public static readonly ErrorDefinition InvalidCurrency = new("FIN_BOOK_CURRENCY_INVALID", "finance.book_currency_invalid", 422);
    public static readonly ErrorDefinition Duplicate = new("FIN_BOOK_DUPLICATE", "finance.book_duplicate", 409);
}

public enum AccountingBookStatus { Active }

public static class AccountingBookSetup
{
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9._-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    // V1 functional-currency/precision tuples. No FX or transaction currency conversion.
    private static readonly IReadOnlyDictionary<string, int> CurrencyMinorUnits = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["JOD"] = 3,
        ["USD"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["SAR"] = 2,
        ["AED"] = 2,
        ["KWD"] = 3,
        ["BHD"] = 3,
        ["OMR"] = 3,
        ["JPY"] = 0
    };

    public static (string Code, string Name, string Currency, int MinorUnits) Validate(
        string? code, string? name, string? functionalCurrency)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? "";
        if (!CodePattern.IsMatch(normalizedCode)) throw new FoundationException(BookSetupErrors.InvalidCode);
        var normalizedName = name?.Trim() ?? "";
        if (normalizedName.Length is < 1 or > 200) throw new FoundationException(BookSetupErrors.InvalidName);
        var currency = functionalCurrency?.Trim().ToUpperInvariant() ?? "";
        if (!CurrencyMinorUnits.TryGetValue(currency, out var minorUnits))
            throw new FoundationException(BookSetupErrors.InvalidCurrency);
        return (normalizedCode, normalizedName, currency, minorUnits);
    }
}
