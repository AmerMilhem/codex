using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.Finance.Application;

public sealed record GeneralLedgerRequest(Guid AccountId, TrialBalanceRange Range, int Offset, int Limit)
{
    public static GeneralLedgerRequest Create(Guid? account, DateOnly? start, DateOnly? end, int offset, int limit)
    {
        if (account is null || account == Guid.Empty || offset < 0 || limit is < 1 or > 200) throw new FoundationException(Errors.VALIDATION_FAILED);
        return new(account.Value, TrialBalanceRange.Create(start, end), offset, limit);
    }
}
public interface IGeneralLedgerReader
{
    Task<GeneralLedgerReport> ReadAsync(VerifiedContext context, GeneralLedgerRequest request, CancellationToken token);
}
public sealed class GeneralLedgerQuery(IGeneralLedgerReader reader)
{
    public Task<GeneralLedgerReport> RunAsync(VerifiedContext context, Guid? account, DateOnly? start, DateOnly? end, int offset = 0, int limit = 50, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        return reader.ReadAsync(context, GeneralLedgerRequest.Create(account, start, end, offset, limit), token);
    }
}
public static class GeneralLedgerReconciliation
{
    public static TrialBalanceReconciliation Check(string opening, string debit, string credit, string closing,
        TrialBalanceAmounts trial, string pageOpening, IReadOnlyList<GeneralLedgerMovement> movements)
    {
        var checks = new List<TrialBalanceCheck>();
        void Check(string code, string expected, string actual) => checks.Add(new(code, TrialBalanceArithmetic.Units(expected) == TrialBalanceArithmetic.Units(actual), expected, actual));
        string Net(string d, string c) => TrialBalanceArithmetic.Format(TrialBalanceArithmetic.Units(d) - TrialBalanceArithmetic.Units(c));
        Check("GL_TB_OPENING", Net(trial.OpeningDebit, trial.OpeningCredit), opening);
        Check("GL_TB_PERIOD_DEBIT", trial.PeriodDebit, debit); Check("GL_TB_PERIOD_CREDIT", trial.PeriodCredit, credit);
        Check("GL_TB_CLOSING", Net(trial.ClosingDebit, trial.ClosingCredit), closing);
        Check("GL_CLOSING_EQUATION", TrialBalanceArithmetic.Format(TrialBalanceArithmetic.Units(opening) + TrialBalanceArithmetic.Units(debit) - TrialBalanceArithmetic.Units(credit)), closing);
        var running = TrialBalanceArithmetic.Units(pageOpening); var invalid = 0;
        foreach (var line in movements)
        {
            running += TrialBalanceArithmetic.Units(line.Debit) - TrialBalanceArithmetic.Units(line.Credit);
            if (running != TrialBalanceArithmetic.Units(line.RunningBalance)) invalid++;
        }
        Check("GL_PAGE_RUNNING_EQUATION", "0", invalid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new(checks.All(x => x.Passed), checks);
    }
}
