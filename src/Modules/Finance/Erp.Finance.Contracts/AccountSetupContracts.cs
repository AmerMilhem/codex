namespace Erp.Finance.Contracts;

public static class AccountPermissions
{
    public const string View = "Finance.Account.View";
    public const string Create = "Finance.Account.Create";
}

public sealed record CreateAccountRequest(string? Code, string? Name, bool PostingAllowed);
public sealed record AccountSetupView(Guid Id, string Code, string Name, string Status, bool PostingAllowed,
    DateTimeOffset? CreatedAt, string? CreatedByIssuer, string? CreatedBySubject);
public sealed record AccountCreatedV1(Guid TenantId, Guid CompanyId, Guid AccountId, string Code, bool PostingAllowed)
{
    public const string EventType = "finance.account-created.v1";
}
