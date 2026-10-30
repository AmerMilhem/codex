using System.Globalization;
using Erp.Kernel;

namespace Erp.Finance.Domain;

public static class FinanceErrors
{
    public static readonly ErrorDefinition Unbalanced = new("FIN_JOURNAL_UNBALANCED", "finance.journal_unbalanced", 422);
    public static readonly ErrorDefinition PeriodClosed = new("FIN_PERIOD_CLOSED", "finance.period_closed", 422);
    public static readonly ErrorDefinition PeriodMissing = new("FIN_PERIOD_INVALID", "finance.period_invalid", 422);
    public static readonly ErrorDefinition InactiveAccount = new("FIN_ACCOUNT_INACTIVE", "finance.account_inactive", 422);
    public static readonly ErrorDefinition NonPostableAccount = new("FIN_ACCOUNT_NOT_POSTABLE", "finance.account_not_postable", 422);
    public static readonly ErrorDefinition AccountMissing = new("FIN_ACCOUNT_INVALID", "finance.account_invalid", 422);
    public static readonly ErrorDefinition AlreadyPosted = new("FIN_JOURNAL_ALREADY_POSTED", "finance.journal_already_posted", 409);
    public static readonly ErrorDefinition InvalidLine = new("FIN_JOURNAL_INVALID_LINE", "finance.journal_invalid_line", 422);
    public static readonly ErrorDefinition CurrencyInvalid = new("FIN_CURRENCY_INVALID", "finance.currency_invalid", 422);
    public static readonly ErrorDefinition DateRequired = new("FIN_POSTING_DATE_REQUIRED", "finance.posting_date_required", 422);
    public static readonly ErrorDefinition BookMissing = new("FIN_BOOK_NOT_CONFIGURED", "finance.book_not_configured", 422);
}

public enum JournalStatus { Draft, Posted }
public enum FiscalPeriodStatus { Open, Closed }
public enum AccountStatus { Active, Inactive }
public sealed record AccountSnapshot(Guid Id, AccountStatus Status, bool PostingAllowed);
public sealed record PeriodSnapshot(Guid Id, int FiscalYear, FiscalPeriodStatus Status, DateOnly StartsOn, DateOnly EndsOn);
public sealed record BookPolicy(string Currency, int MinorUnits);
public sealed record LineInput(Guid? Id, Guid AccountId, decimal Debit, decimal Credit, string Description);
public sealed record JournalIssue(ErrorDefinition Error, string Field, Guid? LineId = null);

public static class JournalMoney
{
    public const decimal Limit = 1_000_000_000_000_000_000m;
    public const string RoundingPolicy = "internal-to-even-v1";
    public static decimal Quantize(decimal amount, int minorUnits) => decimal.Round(amount, minorUnits, MidpointRounding.ToEven);
    public static bool Fits(decimal amount) => amount >= 0 && amount < Limit && decimal.Round(amount, 6) == amount;
    public static string Format(decimal amount) => amount.ToString("0.######", CultureInfo.InvariantCulture);
    public static decimal Parse(string? value)
    {
        if (value is null || value.Length is < 1 or > 25 || !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[0-9]{1,18}(\.[0-9]{1,6})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || !Fits(amount))
            throw new FoundationException(FinanceErrors.InvalidLine);
        return amount;
    }
}

// The posted journal and its lines ARE the immutable general-ledger fact for this slice.
// No duplicate mutable ledger/projection is used as an accounting source of truth.
public sealed class JournalEntry
{
    private readonly List<JournalLine> lines = [];
    private JournalEntry() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public JournalStatus Status { get; private set; }
    public long Revision { get; private set; }
    public DateOnly? PostingDate { get; private set; }
    public string Currency { get; private set; } = "";
    public string Reference { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string RoundingPolicy { get; private set; } = JournalMoney.RoundingPolicy;
    public Guid? FiscalPeriodId { get; private set; }
    public int? FiscalYear { get; private set; }
    public long? NumberSequence { get; private set; }
    public string? JournalNumber { get; private set; }
    public string? PostedByIssuer { get; private set; }
    public string? PostedBySubject { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<JournalLine> Lines => lines.AsReadOnly();
    public decimal TotalDebit => lines.Sum(x => x.Debit);
    public decimal TotalCredit => lines.Sum(x => x.Credit);

    public static JournalEntry Create(Guid tenant, Guid company, DateOnly? date, string currency, string reference,
        string description, IReadOnlyList<LineInput> inputs, DateTimeOffset now)
    {
        if (tenant == Guid.Empty || company == Guid.Empty) throw new FoundationException(Errors.TENANT_CONTEXT_INVALID);
        var journal = new JournalEntry { Id = Guid.CreateVersion7(), TenantId = tenant, CompanyId = company, CreatedAt = now };
        journal.Edit(0, date, currency, reference, description, inputs);
        return journal;
    }
    public void RequireDraft()
    {
        if (Status != JournalStatus.Draft) throw new FoundationException(FinanceErrors.AlreadyPosted);
    }
    public void ExpectRevision(long expected)
    {
        if (expected != Revision) throw new FoundationException(Errors.DOC_CONCURRENCY_CONFLICT);
    }
    public void Edit(long expected, DateOnly? date, string currency, string reference, string description, IReadOnlyList<LineInput> inputs)
    {
        RequireDraft(); ExpectRevision(expected);
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') || reference.Length > 128 || description.Length > 1000 || inputs.Count > 200
            || inputs.Where(x => x.Id.HasValue).Select(x => x.Id).Distinct().Count() != inputs.Count(x => x.Id.HasValue))
            throw new FoundationException(Errors.VALIDATION_FAILED);
        foreach (var input in inputs)
        {
            if (input.AccountId == Guid.Empty || input.Description.Length > 256 || !JournalMoney.Fits(input.Debit) || !JournalMoney.Fits(input.Credit)
                || (input.Id.HasValue && !lines.Any(x => x.Id == input.Id)))
                throw new FoundationException(FinanceErrors.InvalidLine);
        }
        PostingDate = date; Currency = currency; Reference = reference; Description = description;
        lines.RemoveAll(x => !inputs.Any(i => i.Id == x.Id));
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var line = input.Id.HasValue ? lines.Single(x => x.Id == input.Id) : new JournalLine(Id, TenantId, CompanyId);
            line.Edit(index + 1, input);
            if (!input.Id.HasValue) lines.Add(line);
        }
        Revision = checked(Revision + 1);
    }
    public IReadOnlyList<JournalIssue> Validate(BookPolicy book, IReadOnlyList<PeriodSnapshot> periods, IReadOnlyList<AccountSnapshot> accounts)
    {
        var issues = new List<JournalIssue>();
        if (Status != JournalStatus.Draft) issues.Add(new(FinanceErrors.AlreadyPosted, "status"));
        if (PostingDate is null) issues.Add(new(FinanceErrors.DateRequired, "postingDate"));
        else if (periods.Count != 1 || PostingDate < periods[0].StartsOn || PostingDate > periods[0].EndsOn)
            issues.Add(new(FinanceErrors.PeriodMissing, "postingDate"));
        else if (periods[0].Status != FiscalPeriodStatus.Open) issues.Add(new(FinanceErrors.PeriodClosed, "postingDate"));
        if (book.Currency != Currency || book.MinorUnits is < 0 or > 6) issues.Add(new(FinanceErrors.CurrencyInvalid, "currency"));
        if (lines.Count < 2) issues.Add(new(FinanceErrors.InvalidLine, "lines"));
        foreach (var line in lines)
        {
            if (!((line.Debit > 0 && line.Credit == 0) || (line.Credit > 0 && line.Debit == 0))
                || !JournalMoney.Fits(line.Debit) || !JournalMoney.Fits(line.Credit)
                || (book.MinorUnits is >= 0 and <= 6 && (JournalMoney.Quantize(line.Debit, book.MinorUnits) != line.Debit || JournalMoney.Quantize(line.Credit, book.MinorUnits) != line.Credit)))
                issues.Add(new(FinanceErrors.InvalidLine, "lines", line.Id));
            var account = accounts.SingleOrDefault(x => x.Id == line.AccountId);
            if (account is null) issues.Add(new(FinanceErrors.AccountMissing, "accountId", line.Id));
            else
            {
                if (account.Status != AccountStatus.Active) issues.Add(new(FinanceErrors.InactiveAccount, "accountId", line.Id));
                if (!account.PostingAllowed) issues.Add(new(FinanceErrors.NonPostableAccount, "accountId", line.Id));
            }
        }
        if (TotalDebit != TotalCredit) issues.Add(new(FinanceErrors.Unbalanced, "totals"));
        if (!JournalMoney.Fits(TotalDebit) || !JournalMoney.Fits(TotalCredit)) issues.Add(new(FinanceErrors.InvalidLine, "totals"));
        return issues;
    }
    public void Post(long expected, BookPolicy book, IReadOnlyList<PeriodSnapshot> periods, IReadOnlyList<AccountSnapshot> accounts,
        long sequence, string issuer, string subject, DateTimeOffset now)
    {
        RequireDraft(); ExpectRevision(expected);
        var issues = Validate(book, periods, accounts);
        if (issues.Count != 0) throw new FoundationException(issues[0].Error);
        if (sequence <= 0) throw new FoundationException(Errors.VALIDATION_FAILED);
        FiscalPeriodId = periods[0].Id; FiscalYear = periods[0].FiscalYear; NumberSequence = sequence;
        JournalNumber = $"MJ-{FiscalYear}-{sequence:D6}";
        PostedByIssuer = issuer; PostedBySubject = subject; PostedAt = now;
        Status = JournalStatus.Posted; Revision = checked(Revision + 1);
    }
}

public sealed class JournalLine
{
    private JournalLine() { }
    internal JournalLine(Guid journal, Guid tenant, Guid company)
    { Id = Guid.CreateVersion7(); JournalId = journal; TenantId = tenant; CompanyId = company; }
    public Guid Id { get; private set; }
    public Guid JournalId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public int Position { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public string Description { get; private set; } = "";
    internal void Edit(int position, LineInput input)
    { Position = position; AccountId = input.AccountId; Debit = input.Debit; Credit = input.Credit; Description = input.Description; }
}
