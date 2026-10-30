namespace Erp.Finance.Contracts;

public static class BookPermissions
{
    public const string View = "Finance.Book.View";
    public const string Create = "Finance.Book.Create";
}

public sealed record CreateAccountingBookRequest(string? Code, string? Name, string? FunctionalCurrency);
public sealed record AccountingBookView(Guid Id, Guid TenantId, Guid CompanyId, string Code, string Name,
    string FunctionalCurrency, int MinorUnits, string Status, DateTimeOffset? CreatedAt,
    string? CreatedByIssuer, string? CreatedBySubject);
public sealed record AccountingBookCreatedV1(Guid TenantId, Guid CompanyId, Guid AccountingBookId,
    string Code, string FunctionalCurrency)
{
    public const string EventType = "finance.accounting-book-created.v1";
}
