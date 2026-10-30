using System.Globalization;
using System.Numerics;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.Finance.Application;

public sealed record TrialBalanceRange(DateOnly StartDate, DateOnly EndDate)
{
    public static readonly ErrorDefinition Invalid = new("FIN_REPORT_INVALID_RANGE", "finance.report_invalid_range", 400);
    public static TrialBalanceRange Create(DateOnly? start, DateOnly? end) => start is null || end is null || start > end
        ? throw new FoundationException(Invalid) : new(start.Value, end.Value);
}
public sealed record TrialBalanceSnapshot(string Currency, int MinorUnits, DateTimeOffset GeneratedAt,
    IReadOnlyList<TrialBalanceAccount> Accounts, TrialBalanceLedgerControl Control);
public interface ITrialBalanceReader
{
    Task<TrialBalanceSnapshot> ReadAsync(VerifiedContext context, TrialBalanceRange range, CancellationToken token);
}
public interface ITrialBalanceQuery
{
    Task<TrialBalanceReport> RunAsync(VerifiedContext context, DateOnly? start, DateOnly? end, CancellationToken token = default);
}

// Aggregate numeric values can exceed decimal's capacity even though each persisted line fits numeric(24,6).
// Exact scaled integers reconcile SQL aggregates; no financial aggregation is performed over in-memory journal entries.
public static class TrialBalanceArithmetic
{
    public static BigInteger Units(string value)
    {
        if (value.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(value, @"\A-?[0-9]+(?:\.[0-9]{1,6})?\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid exact ledger aggregate.");
        var negative = value[0] == '-'; var parts = (negative ? value[1..] : value).Split('.');
        var result = BigInteger.Parse(parts[0], CultureInfo.InvariantCulture) * 1_000_000
            + (parts.Length == 1 ? BigInteger.Zero : BigInteger.Parse(parts[1].PadRight(6, '0'), CultureInfo.InvariantCulture));
        return negative ? -result : result;
    }
    public static string Format(BigInteger units)
    {
        var magnitude = BigInteger.Abs(units); var fraction = (magnitude % 1_000_000).ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0');
        return (units.Sign < 0 ? "-" : "") + (magnitude / 1_000_000).ToString(CultureInfo.InvariantCulture) + (fraction.Length == 0 ? "" : "." + fraction);
    }
    public static TrialBalanceAmounts Total(IEnumerable<TrialBalanceAccount> accounts)
    {
        var values = new BigInteger[6];
        foreach (var account in accounts)
        {
            var a = account.Amounts; var fields = new[] { a.OpeningDebit, a.OpeningCredit, a.PeriodDebit, a.PeriodCredit, a.ClosingDebit, a.ClosingCredit };
            for (var i = 0; i < fields.Length; i++) values[i] += Units(fields[i]);
        }
        return new(Format(values[0]), Format(values[1]), Format(values[2]), Format(values[3]), Format(values[4]), Format(values[5]));
    }
    public static TrialBalanceReconciliation Reconcile(IReadOnlyList<TrialBalanceAccount> accounts, TrialBalanceAmounts totals, TrialBalanceLedgerControl ledger)
    {
        var checks = new List<TrialBalanceCheck>();
        void Check(string code, string expected, string actual) => checks.Add(new(code, Units(expected) == Units(actual), expected, actual));
        Check("TB_OPENING_BALANCED", totals.OpeningDebit, totals.OpeningCredit);
        Check("TB_PERIOD_BALANCED", totals.PeriodDebit, totals.PeriodCredit);
        Check("TB_CLOSING_BALANCED", totals.ClosingDebit, totals.ClosingCredit);
        Check("TB_LEDGER_OPENING_BALANCED", ledger.OpeningLedgerDebit, ledger.OpeningLedgerCredit);
        Check("TB_LEDGER_CLOSING_BALANCED", ledger.ClosingLedgerDebit, ledger.ClosingLedgerCredit);
        Check("TB_OPENING_DEBIT_MATCH", ledger.Totals.OpeningDebit, totals.OpeningDebit);
        Check("TB_OPENING_CREDIT_MATCH", ledger.Totals.OpeningCredit, totals.OpeningCredit);
        Check("TB_PERIOD_DEBIT_MATCH", ledger.Totals.PeriodDebit, totals.PeriodDebit);
        Check("TB_PERIOD_CREDIT_MATCH", ledger.Totals.PeriodCredit, totals.PeriodCredit);
        Check("TB_CLOSING_DEBIT_MATCH", ledger.Totals.ClosingDebit, totals.ClosingDebit);
        Check("TB_CLOSING_CREDIT_MATCH", ledger.Totals.ClosingCredit, totals.ClosingCredit);
        Check("TB_LEDGER_JOURNAL_INTEGRITY", "0", ledger.InvalidJournalCount.ToString(CultureInfo.InvariantCulture));
        var invalidRows = accounts.Count(x =>
        {
            var a = x.Amounts; var od = Units(a.OpeningDebit); var oc = Units(a.OpeningCredit); var pd = Units(a.PeriodDebit); var pc = Units(a.PeriodCredit); var cd = Units(a.ClosingDebit); var cc = Units(a.ClosingCredit);
            return od < 0 || oc < 0 || pd < 0 || pc < 0 || cd < 0 || cc < 0 || (od > 0 && oc > 0) || (cd > 0 && cc > 0) || od - oc + pd - pc != cd - cc;
        });
        Check("TB_ACCOUNT_EQUATIONS", "0", invalidRows.ToString(CultureInfo.InvariantCulture));
        return new(checks.All(x => x.Passed), checks);
    }
}
public sealed class TrialBalanceQuery(ITrialBalanceReader reader) : ITrialBalanceQuery
{
    public async Task<TrialBalanceReport> RunAsync(VerifiedContext context, DateOnly? start, DateOnly? end, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        var range = TrialBalanceRange.Create(start, end);
        var snapshot = await reader.ReadAsync(context, range, token);
        var totals = TrialBalanceArithmetic.Total(snapshot.Accounts);
        var reconciliation = TrialBalanceArithmetic.Reconcile(snapshot.Accounts, totals, snapshot.Control);
        return new(context.TenantId, context.CompanyId, range.StartDate, range.EndDate, snapshot.Currency, snapshot.MinorUnits,
            "company-book-lifetime", snapshot.GeneratedAt, snapshot.Accounts, totals, snapshot.Control, reconciliation);
    }
}
