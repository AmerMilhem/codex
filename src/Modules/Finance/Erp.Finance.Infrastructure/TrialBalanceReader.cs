using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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

public sealed class TrialBalanceReader(NpgsqlDataSource source, ILogger<TrialBalanceReader> logger) : ITrialBalanceReader
{
    private static readonly Counter<long> Reconciliations = FoundationTelemetry.Meter.CreateCounter<long>("erp.finance.trial_balance.reconciliations");
    private static readonly Histogram<double> Durations = FoundationTelemetry.Meter.CreateHistogram<double>("erp.finance.trial_balance.duration", "ms");
    public async Task<TrialBalanceSnapshot> ReadAsync(VerifiedContext context, TrialBalanceRange range, CancellationToken token)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        using var activity = FoundationTelemetry.Source.StartActivity("finance.trial_balance");
        var timer = Stopwatch.StartNew();
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        // READ ONLY is enforced by PostgreSQL, not just an API naming convention. No writer/UoW/audit/outbox is invoked.
        await Sql.ExecuteAsync(connection, "SET TRANSACTION READ ONLY", token);
        await Sql.ExecuteAsync(connection, """
            SELECT set_config('erp.tenant_id',@tenant,true),set_config('erp.company_id',@company,true),
              set_config('erp.correlation_id',@correlation,true),set_config('erp.request_id',@request,true),set_config('erp.trace_id',@trace,true)
            """, token, ("tenant", context.TenantId.ToString()), ("company", context.CompanyId.ToString()),
            ("correlation", context.CorrelationId), ("request", context.RequestId), ("trace", Activity.Current?.TraceId.ToString() ?? ""));
        string currency; int minorUnits; DateTimeOffset generatedAt;
        await using (var book = Sql.Command(connection, "SELECT currency,minor_units,transaction_timestamp() FROM finance.accounting_books WHERE tenant_id=@tenant AND company_id=@company", ("tenant", context.TenantId), ("company", context.CompanyId)))
        await using (var r = await book.ExecuteReaderAsync(token))
        {
            if (!await r.ReadAsync(token)) throw new FoundationException(FinanceErrors.BookMissing);
            currency = r.GetString(0); minorUnits = r.GetInt32(1); generatedAt = r.GetFieldValue<DateTimeOffset>(2);
        }
        var accounts = new List<TrialBalanceAccount>();
        await using (var command = Command(connection, TrialBalanceSql.Accounts, context, range))
        await using (var r = await command.ExecuteReaderAsync(token))
            while (await r.ReadAsync(token)) accounts.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), ReadAmounts(r, 3)));
        TrialBalanceLedgerControl control;
        await using (var command = Command(connection, TrialBalanceSql.Control, context, range))
        await using (var r = await command.ExecuteReaderAsync(token))
        {
            await r.ReadAsync(token);
            control = new(ReadAmounts(r, 0), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetInt64(10), r.GetInt64(11), r.GetInt64(12));
        }
        await transaction.CommitAsync(token);
        var reconciliation = TrialBalanceArithmetic.Reconcile(accounts, TrialBalanceArithmetic.Total(accounts), control);
        Reconciliations.Add(1, new KeyValuePair<string, object?>("outcome", reconciliation.Passed ? "balanced" : "failure"));
        if (!reconciliation.Passed) logger.LogWarning("Trial balance reconciliation failed: {Checks}", string.Join(',', reconciliation.Checks.Where(x => !x.Passed).Select(x => x.Code)));
        Durations.Record(timer.Elapsed.TotalMilliseconds);
        activity?.SetTag("erp.report.reconciled", reconciliation.Passed); activity?.SetTag("erp.report.accounts", accounts.Count);
        return new(currency, minorUnits, generatedAt, accounts, control);
    }
    public static NpgsqlCommand Command(NpgsqlConnection connection, string sql, VerifiedContext context, TrialBalanceRange range) =>
        Sql.Command(connection, sql, ("tenant", context.TenantId), ("company", context.CompanyId), ("start", range.StartDate), ("end", range.EndDate));
    private static TrialBalanceAmounts ReadAmounts(NpgsqlDataReader r, int offset) =>
        new(r.GetString(offset), r.GetString(offset + 1), r.GetString(offset + 2), r.GetString(offset + 3), r.GetString(offset + 4), r.GetString(offset + 5));
}
