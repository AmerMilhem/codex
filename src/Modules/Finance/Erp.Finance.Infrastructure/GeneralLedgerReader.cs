using System.Data;
using System.Diagnostics;
using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Erp.Finance.Infrastructure;

public sealed class GeneralLedgerReader(NpgsqlDataSource source, ILogger<GeneralLedgerReader> logger) : IGeneralLedgerReader
{
    private static readonly System.Diagnostics.Metrics.Histogram<double> Duration = FoundationTelemetry.Meter.CreateHistogram<double>("erp.finance.general_ledger.duration", "ms");
    public async Task<GeneralLedgerReport> ReadAsync(VerifiedContext context, GeneralLedgerRequest request, CancellationToken token)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        request = GeneralLedgerRequest.Create(request.AccountId, request.Range.StartDate, request.Range.EndDate, request.Offset, request.Limit);
        using var activity = FoundationTelemetry.Source.StartActivity("finance.general_ledger"); var timer = Stopwatch.StartNew();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await Sql.ExecuteAsync(connection, "SET TRANSACTION READ ONLY", token);
        await Sql.ExecuteAsync(connection, """
            SELECT set_config('erp.tenant_id',@tenant,true),set_config('erp.company_id',@company,true),
              set_config('erp.correlation_id',@correlation,true),set_config('erp.request_id',@request,true),set_config('erp.trace_id',@trace,true)
            """, token, ("tenant", context.TenantId.ToString()), ("company", context.CompanyId.ToString()),
            ("correlation", context.CorrelationId), ("request", context.RequestId), ("trace", Activity.Current?.TraceId.ToString() ?? ""));
        string code, name, currency; int minorUnits; DateTimeOffset generatedAt;
        await using (var cmd = Command(connection, "SELECT code,name FROM finance.accounts WHERE tenant_id=@tenant AND company_id=@company AND id=@account", context, request))
        await using (var r = await cmd.ExecuteReaderAsync(token))
        {
            // Do not reveal whether an unscoped/foreign account exists.
            if (!await r.ReadAsync(token)) throw new FoundationException(Errors.RESOURCE_NOT_FOUND);
            code = r.GetString(0); name = r.GetString(1);
        }
        await using (var cmd = Command(connection, "SELECT currency,minor_units,transaction_timestamp() FROM finance.accounting_books WHERE tenant_id=@tenant AND company_id=@company", context, request))
        await using (var r = await cmd.ExecuteReaderAsync(token))
        {
            if (!await r.ReadAsync(token)) throw new FoundationException(FinanceErrors.BookMissing);
            currency = r.GetString(0); minorUnits = r.GetInt32(1); generatedAt = r.GetFieldValue<DateTimeOffset>(2);
        }
        string opening;
        await using (var cmd = Command(connection, GeneralLedgerSql.Opening, context, request)) opening = (string)(await cmd.ExecuteScalarAsync(token))!;
        long count; string debit, credit;
        await using (var cmd = Command(connection, GeneralLedgerSql.Summary, context, request))
        await using (var r = await cmd.ExecuteReaderAsync(token))
        { await r.ReadAsync(token); count = r.GetInt64(0); debit = r.GetString(1); credit = r.GetString(2); }
        var closing = TrialBalanceArithmetic.Format(TrialBalanceArithmetic.Units(opening) + TrialBalanceArithmetic.Units(debit) - TrialBalanceArithmetic.Units(credit));
        var rows = new List<GeneralLedgerMovement>();
        await using (var cmd = Command(connection, GeneralLedgerSql.Movements, context, request, opening))
        await using (var r = await cmd.ExecuteReaderAsync(token))
            while (await r.ReadAsync(token)) rows.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetInt32(2), r.GetFieldValue<DateOnly>(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10)));
        TrialBalanceAmounts trial = new("0", "0", "0", "0", "0", "0");
        await using (var cmd = Command(connection, GeneralLedgerSql.Trial, context, request))
        await using (var r = await cmd.ExecuteReaderAsync(token))
            if (await r.ReadAsync(token)) trial = new(r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8));
        // For an empty/out-of-range page the preceding balance is the period closing balance.
        var pageOpening = rows.Count == 0 ? closing : TrialBalanceArithmetic.Format(TrialBalanceArithmetic.Units(rows[0].RunningBalance) - TrialBalanceArithmetic.Units(rows[0].Debit) + TrialBalanceArithmetic.Units(rows[0].Credit));
        var reconciliation = GeneralLedgerReconciliation.Check(opening, debit, credit, closing, trial, pageOpening, rows);
        if (rows.Count > 0 && request.Offset == 0 && TrialBalanceArithmetic.Units(pageOpening) != TrialBalanceArithmetic.Units(opening))
            reconciliation = new(false, [.. reconciliation.Checks, new("GL_FIRST_PAGE_OPENING", false, opening, pageOpening)]);
        if (rows.Count > 0 && (long)request.Offset + rows.Count == count && TrialBalanceArithmetic.Units(rows[^1].RunningBalance) != TrialBalanceArithmetic.Units(closing))
            reconciliation = new(false, [.. reconciliation.Checks, new("GL_LAST_PAGE_CLOSING", false, closing, rows[^1].RunningBalance)]);
        await transaction.CommitAsync(token);
        if (!reconciliation.Passed) logger.LogWarning("General ledger reconciliation failed: {Checks}", string.Join(',', reconciliation.Checks.Where(x => !x.Passed).Select(x => x.Code)));
        Duration.Record(timer.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("reconciled", reconciliation.Passed));
        activity?.SetTag("erp.report.reconciled", reconciliation.Passed);
        return new(context.TenantId, context.CompanyId, request.AccountId, code, name, request.Range.StartDate, request.Range.EndDate,
            currency, minorUnits, "company-book-lifetime", generatedAt, request.Offset, request.Limit, count, opening, debit, credit, closing, pageOpening, rows, reconciliation);
    }
    public static NpgsqlCommand Command(NpgsqlConnection connection, string sql, VerifiedContext context, GeneralLedgerRequest request, string opening = "0") =>
        Sql.Command(connection, sql, ("tenant", context.TenantId), ("company", context.CompanyId), ("account", request.AccountId),
            ("start", request.Range.StartDate), ("end", request.Range.EndDate), ("offset", request.Offset), ("limit", request.Limit), ("opening", opening));
}
