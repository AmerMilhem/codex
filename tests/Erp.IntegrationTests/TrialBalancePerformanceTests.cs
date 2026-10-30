using System.Diagnostics;
using System.Text.Json;
using Erp.Finance.Application;
using Erp.Finance.Domain;
using Erp.Finance.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class TrialBalancePerformanceTests(DatabaseFixture db)
{
    [Fact]
    public async Task Measure_SQL_and_EF_aggregation_on_100000_lines_and_retain_actual_plans()
    {
        var s = await TrialBalanceScenario.Create(db); var seedClock = Stopwatch.StartNew(); await Seed(s); var seedMilliseconds = seedClock.Elapsed.TotalMilliseconds;
        await db.Execute("ANALYZE finance.journal_entries; ANALYZE finance.journal_lines; ANALYZE finance.accounts");
        var range = new TrialBalanceRange(new(2026, 1, 1), new(2026, 1, 31)); var context = await s.Context();
        var sqlTimes = new List<double>(); var efTimes = new List<double>(); var reportTimes = new List<double>();
        string efSql = "";
        async Task SqlRun()
        {
            await db.Uow.ExecuteAsync(context, async (transaction, token) =>
            {
                await using var cmd = TrialBalanceReader.Command(PostgresTransaction.Require(transaction).Connection, TrialBalanceSql.Accounts, context, range);
                await using var r = await cmd.ExecuteReaderAsync(token); var count = 0;
                while (await r.ReadAsync(token)) { Assert.Equal(500000, TrialBalanceArithmetic.Units(r.GetString(5)) + TrialBalanceArithmetic.Units(r.GetString(6))); count++; }
                Assert.Equal(100, count); return 0;
            });
        }
        async Task EfRun()
        {
            await db.Uow.ExecuteAsync(context, async (transaction, token) =>
            {
                var tx = PostgresTransaction.Require(transaction); var finance = tx.Enlist(c => new FinanceDbContext(FinanceDbContext.Options(c)));
                var grouped = from j in finance.Journals.AsNoTracking()
                              join l in finance.Lines.AsNoTracking() on new { j.TenantId, j.CompanyId, JournalId = j.Id } equals new { l.TenantId, l.CompanyId, l.JournalId }
                              where j.TenantId == context.TenantId && j.CompanyId == context.CompanyId && j.Status == JournalStatus.Posted && j.PostingDate <= range.EndDate
                              group new { j, l } by l.AccountId into g
                              select new
                              {
                                  Id = g.Key,
                                  Opening = g.Sum(x => x.j.PostingDate < range.StartDate ? x.l.Debit - x.l.Credit : 0),
                                  Debit = g.Sum(x => x.j.PostingDate >= range.StartDate ? x.l.Debit : 0),
                                  Credit = g.Sum(x => x.j.PostingDate >= range.StartDate ? x.l.Credit : 0)
                              };
                var projection = from m in grouped
                                 join a in finance.Accounts.AsNoTracking() on m.Id equals a.Id
                                 where a.TenantId == context.TenantId && a.CompanyId == context.CompanyId && (m.Opening != 0 || m.Debit != 0 || m.Credit != 0)
                                 orderby a.Code, a.Id
                                 select new
                                 {
                                     a.Id,
                                     a.Code,
                                     a.Name,
                                     OpeningDebit = Math.Max(m.Opening, 0),
                                     OpeningCredit = Math.Max(-m.Opening, 0),
                                     PeriodDebit = m.Debit,
                                     PeriodCredit = m.Credit,
                                     ClosingDebit = Math.Max(m.Opening + m.Debit - m.Credit, 0),
                                     ClosingCredit = Math.Max(-(m.Opening + m.Debit - m.Credit), 0)
                                 };
                efSql = projection.ToQueryString(); var result = await projection.ToArrayAsync(token);
                Assert.Equal(100, result.Length); Assert.All(result, x => Assert.Equal(.5m, x.PeriodDebit + x.PeriodCredit)); return 0;
            });
        }
        // Warm provider translation/connection caches before measuring the same primary projection through each adapter.
        await SqlRun(); await EfRun(); await s.Report(range.StartDate, range.EndDate);
        for (var i = 0; i < 5; i++)
        {
            var clock = Stopwatch.StartNew(); await SqlRun(); sqlTimes.Add(clock.Elapsed.TotalMilliseconds);
            clock.Restart(); await EfRun(); efTimes.Add(clock.Elapsed.TotalMilliseconds);
            clock.Restart(); var report = await s.Report(range.StartDate, range.EndDate); reportTimes.Add(clock.Elapsed.TotalMilliseconds);
            Assert.True(report.Reconciliation.Passed); Assert.Equal(500, report.LedgerControl.PostedJournalCount); Assert.Equal(100000, report.LedgerControl.PostedLineCount);
            Assert.Equal("25", report.Totals.PeriodDebit); Assert.Equal("25", report.Totals.OpeningDebit); Assert.Equal("50", report.Totals.ClosingDebit);
        }
        async Task<string> Explain(string sql, TrialBalanceRange dates) => await db.Uow.ExecuteAsync(context, async (tx, token) =>
        {
            await using var cmd = TrialBalanceReader.Command(PostgresTransaction.Require(tx).Connection, "EXPLAIN (ANALYZE,BUFFERS,FORMAT JSON) " + sql, context, dates);
            return (string)(await cmd.ExecuteScalarAsync(token))!;
        });
        var plan = await Explain(TrialBalanceSql.Accounts, range); var controlPlan = await Explain(TrialBalanceSql.Control, range);
        var selectivePlan = await Explain(TrialBalanceSql.Accounts, new(new(2024, 1, 1), new(2024, 1, 31)));
        string Root()
        { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "Erp.slnx"))) d = d.Parent; return d!.FullName; }
        var path = Path.Combine(Root(), "artifacts", "trial-balance"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "accounts-plan.json"), plan); await File.WriteAllTextAsync(Path.Combine(path, "control-plan.json"), controlPlan);
        await File.WriteAllTextAsync(Path.Combine(path, "selective-plan.json"), selectivePlan); await File.WriteAllTextAsync(Path.Combine(path, "ef-projection.sql"), efSql);
        using var document = JsonDocument.Parse(plan); var root = document.RootElement[0]; var nodes = new List<object>(); double scanned = 0;
        void Walk(JsonElement node)
        {
            var type = node.GetProperty("Node Type").GetString()!;
            if (type.Contains("Scan", StringComparison.Ordinal))
            {
                var loops = node.GetProperty("Actual Loops").GetDouble(); var rows = node.GetProperty("Actual Rows").GetDouble();
                var removed = node.TryGetProperty("Rows Removed by Filter", out var f) ? f.GetDouble() : 0;
                scanned += (rows + removed) * loops;
                nodes.Add(new
                {
                    node = type,
                    relation = node.TryGetProperty("Relation Name", out var relation) ? relation.GetString() : null,
                    index = node.TryGetProperty("Index Name", out var index) ? index.GetString() : null,
                    rowsPerLoop = rows,
                    loops,
                    removedPerLoop = removed
                });
            }
            if (node.TryGetProperty("Plans", out var children)) foreach (var child in children.EnumerateArray()) Walk(child);
        }
        Walk(root.GetProperty("Plan"));
        double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
        var evidence = new
        {
            dataset = new { journals = 500, lines = 100000, accounts = 100, openingJournals = 250, periodJournals = 250 },
            fixturePreparationMilliseconds = seedMilliseconds,
            runs = 5,
            sqlProjectionMilliseconds = sqlTimes,
            efProjectionMilliseconds = efTimes,
            fullReportWithIndependentControlMilliseconds = reportTimes,
            medians = new { sqlProjection = Median(sqlTimes), efProjection = Median(efTimes), fullReport = Median(reportTimes) },
            rowsReturned = root.GetProperty("Plan").GetProperty("Actual Rows").GetDouble(),
            scanTupleVisits = scanned,
            scanDefinition = "Sum of scan-node (Actual Rows + Rows Removed by Filter) * Actual Loops; tuple visits across tables, not unique ledger lines",
            postgresExecutionMilliseconds = root.GetProperty("Execution Time").GetDouble(),
            scanNodes = nodes,
            strategy = "Explicit parameterized Npgsql SQL: exact NUMERIC-to-text aggregates, explicit independent controls, no new dependency; EF projection measured as an alternative",
            indexesAdded = 0,
            limitations = "Local warm-cache single-client measurements, not production capacity or an SLA. Full historical opening must read eligible ledger history."
        };
        await File.WriteAllTextAsync(Path.Combine(path, "performance.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        // No fragile wall-clock threshold; correctness and source/result cardinality are hard gates. Measurements remain reviewable.
    }
    private async Task Seed(TrialBalanceScenario s)
    {
        await using var connection = new NpgsqlConnection(db.Admin); await connection.OpenAsync();
        await using var command = Sql.Command(connection, SeedSql, ("tenant", s.Tenant), ("company", s.Company));
        command.CommandTimeout = 180; // Test-only bulk setup; report commands retain the normal 30-second timeout.
        await command.ExecuteNonQueryAsync();
    }
    internal const string SeedSql = """
        BEGIN;
        SELECT set_config('erp.tenant_id',@tenant::text,true),set_config('erp.company_id',@company::text,true);
        INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed)
          SELECT gen_random_uuid(),@tenant,@company,'P'||lpad(n::text,3,'0'),'Performance '||n,'Active',true FROM generate_series(1,100) n;
        CREATE TEMP TABLE tb_performance_seed ON COMMIT DROP AS
          SELECT n,gen_random_uuid() AS id,CASE WHEN n<=250 THEN DATE '2025-12-31' ELSE DATE '2026-01-15' END AS posting_date,
            CASE WHEN n<=250 THEN 2025 ELSE 2026 END AS fiscal_year FROM generate_series(1,500) n;
        INSERT INTO finance.journal_entries(id,tenant_id,company_id,status,revision,posting_date,currency,reference,description,rounding_policy,created_at)
          SELECT id,@tenant,@company,'Draft',1,posting_date,'JOD','PERF','Performance fixture','internal-to-even-v1',now() FROM tb_performance_seed;
        WITH accounts AS (SELECT array_agg(id ORDER BY code) AS ids FROM finance.accounts WHERE tenant_id=@tenant AND company_id=@company AND code LIKE 'P%')
        INSERT INTO finance.journal_lines(id,tenant_id,company_id,journal_id,position,account_id,debit,credit,description)
          SELECT gen_random_uuid(),@tenant,@company,j.id,p,
            a.ids[1+((j.n*100+(p-1)/2)%50)+CASE WHEN p%2=0 THEN 50 ELSE 0 END],
            CASE WHEN p%2=1 THEN 0.001 ELSE 0 END,CASE WHEN p%2=0 THEN 0.001 ELSE 0 END,''
          FROM tb_performance_seed j CROSS JOIN generate_series(1,200) p CROSS JOIN accounts a;
        INSERT INTO audit.entries(id,tenant_id,company_id,actor_issuer,actor_subject,action,entity_type,entity_id,timestamp,correlation_id,request_id,source)
          SELECT gen_random_uuid(),@tenant,@company,'tests','performance-fixture','JournalPosted','ManualJournal',id::text,now(),'performance','performance','Test' FROM tb_performance_seed;
        INSERT INTO integration.outbox(id,tenant_id,company_id,type,source_id,payload,occurred_at,correlation_id,state,available_at,attempts)
          SELECT gen_random_uuid(),@tenant,@company,'finance.journal-entry-posted.v1',id::text,
            jsonb_build_object('journalId',id,'journalNumber','MJ-'||fiscal_year||'-'||lpad(n::text,6,'0'),'postingDate',posting_date,'currency','JOD'),
            now(),'performance','Pending',now(),0 FROM tb_performance_seed;
        UPDATE finance.journal_entries j SET status='Posted',revision=2,fiscal_year=s.fiscal_year,
          fiscal_period_id=(SELECT id FROM finance.fiscal_periods p WHERE p.tenant_id=@tenant AND p.company_id=@company AND p.fiscal_year=s.fiscal_year),
          number_sequence=s.n,journal_number='MJ-'||s.fiscal_year||'-'||lpad(s.n::text,6,'0'),posted_at=now(),posted_by_issuer='tests',posted_by_subject='performance-fixture'
          FROM tb_performance_seed s WHERE j.id=s.id;
        INSERT INTO finance.journal_numbers SELECT @tenant,@company,fiscal_year,max(n) FROM tb_performance_seed GROUP BY fiscal_year;
        COMMIT;
        """;
}
