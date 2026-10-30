using System.Diagnostics;
using System.Text.Json;
using Erp.Finance.Application;
using Erp.Finance.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class GeneralLedgerPerformanceTests(DatabaseFixture db)
{
    [Fact]
    public async Task Measure_102000_lines_with_51000_on_one_account_and_capture_each_query_plan()
    {
        var s = await TrialBalanceScenario.Create(db);
        // Same guarded posting fixture as Trial Balance; concentrated distribution tests a busy account.
        var seed = TrialBalancePerformanceTests.SeedSql.Replace("generate_series(1,500)", "generate_series(1,510)", StringComparison.Ordinal)
            .Replace("1+((j.n*100+(p-1)/2)%50)+CASE WHEN p%2=0 THEN 50 ELSE 0 END", "CASE WHEN p%2=0 THEN 51 ELSE 1 END", StringComparison.Ordinal);
        await using (var connection = new NpgsqlConnection(db.Admin))
        {
            await connection.OpenAsync(); await using var cmd = Sql.Command(connection, seed, ("tenant", s.Tenant), ("company", s.Company)); cmd.CommandTimeout = 240; await cmd.ExecuteNonQueryAsync();
        }
        await db.Execute("ANALYZE finance.journal_entries; ANALYZE finance.journal_lines; ANALYZE finance.accounts");
        var account = await db.Scalar<Guid>("SELECT id FROM finance.accounts WHERE tenant_id=@t AND company_id=@c AND code='P001'", ("t", s.Tenant), ("c", s.Company));
        var context = await s.Context(); var request = GeneralLedgerRequest.Create(account, new(2026, 1, 1), new(2026, 1, 31), 13000, 50);
        var q = new GeneralLedgerQuery(new GeneralLedgerReader(db.Source, NullLogger<GeneralLedgerReader>.Instance));
        var queries = new Dictionary<string, string> { ["opening"] = GeneralLedgerSql.Opening, ["count-and-period"] = GeneralLedgerSql.Summary, ["movements-middle-page"] = GeneralLedgerSql.Movements, ["trial-reconciliation"] = GeneralLedgerSql.Trial };
        var measurements = queries.Keys.ToDictionary(x => x, _ => new List<double>()); measurements.Add("full-report", []);
        async Task Run(bool measure)
        {
            foreach (var (name, sql) in queries)
                await db.Uow.ExecuteAsync(context, async (tx, ct) =>
                {
                    var timer = Stopwatch.StartNew(); await using var cmd = GeneralLedgerReader.Command(PostgresTransaction.Require(tx).Connection, sql, context, request, "25");
                    await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) { }
                    if (measure) measurements[name].Add(timer.Elapsed.TotalMilliseconds); return 0;
                });
            var clock = Stopwatch.StartNew(); var result = await q.RunAsync(context, account, request.Range.StartDate, request.Range.EndDate, request.Offset, request.Limit);
            if (measure) measurements["full-report"].Add(clock.Elapsed.TotalMilliseconds);
            Assert.Equal(26000, result.TotalCount); Assert.Equal(50, result.Movements.Count); Assert.Equal("25", result.OpeningBalance); Assert.Equal("26", result.PeriodDebit); Assert.Equal("51", result.ClosingBalance);
            Assert.Equal("38", result.PageOpeningBalance); Assert.Equal("38.001", result.Movements[0].RunningBalance); Assert.Equal("38.05", result.Movements[^1].RunningBalance); Assert.True(result.Reconciliation.Passed);
        }
        await Run(false); for (var i = 0; i < 5; i++) await Run(true);
        var last = await q.RunAsync(context, account, request.Range.StartDate, request.Range.EndDate, 25999, 1); Assert.Equal("51", last.Movements.Single().RunningBalance);
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Erp.slnx"))) directory = directory.Parent;
        var output = Path.Combine(directory!.FullName, "artifacts", "general-ledger"); Directory.CreateDirectory(output);
        foreach (var (name, sql) in queries)
        {
            var plan = await db.Uow.ExecuteAsync(context, async (tx, ct) =>
            {
                await using var cmd = GeneralLedgerReader.Command(PostgresTransaction.Require(tx).Connection, "EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON) " + sql, context, request, "25");
                return (string)(await cmd.ExecuteScalarAsync(ct))!;
            });
            await File.WriteAllTextAsync(Path.Combine(output, name + "-plan.json"), plan);
        }
        await File.WriteAllTextAsync(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(new
        {
            dataset = new { journals = 510, lines = 102000, busyAccountLines = 51000, openingAccountLines = 25000, periodAccountLines = 26000 },
            offset = request.Offset,
            pageSize = request.Limit,
            runs = 5,
            milliseconds = measurements,
            medians = measurements.ToDictionary(x => x.Key, x => x.Value.Order().ElementAt(2)),
            strategy = "Parameterized Npgsql SQL, NUMERIC window before LIMIT/OFFSET, independent existing Trial Balance projection, REPEATABLE READ READ ONLY snapshot",
            limitations = "Local warm-cache single-client measurements; not a production SLA. OFFSET windows and lifetime opening scan eligible history. Plans retained; no speculative index added."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
