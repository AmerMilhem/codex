using Erp.Finance.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class FiscalPeriodMigrationTests(DatabaseFixture db)
{
    private static readonly string[] OrderedMigrations = [
        "20261018141639_ManualJournalV1", "20261020202816_ManualJournalReversalV1",
        "20261023152528_FiscalPeriodCloseV1", "20261023212645_FiscalPeriodSetupV1",
        "20261024162900_ChartOfAccountsSetupV1", "20261024211908_AccountingBookSetupV1",
        "20261029112730_ManualJournalCorrectionV1"
    ];

    [Fact]
    public async Task Clean_install_has_exact_history_current_model_and_forced_RLS()
    {
        var name = "setup_clean_" + Guid.NewGuid().ToString("N");
        await db.Execute($"CREATE DATABASE {name} OWNER erp_migrator");
        try
        {
            var migration = new NpgsqlConnectionStringBuilder(db.Migration) { Database = name }.ToString();
            await MigrationRunner.ApplyAsync(migration);
            await FinanceMigrations.ApplyAsync(migration);
            await using var connection = new NpgsqlConnection(migration); await connection.OpenAsync();
            await using var schema = new FinanceDbContext(FinanceDbContext.Options(connection));
            Assert.Equal(OrderedMigrations, schema.Database.GetMigrations().ToArray());
            Assert.Equal(OrderedMigrations, (await schema.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.False(schema.Database.HasPendingModelChanges());
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Admin) { Database = name }.ToString());
            await admin.OpenAsync();
            await using var inspect = Sql.Command(admin, """
                SELECT (SELECT count(*) FROM information_schema.columns WHERE table_schema='finance' AND table_name='fiscal_periods'
                  AND column_name IN ('revision','closed_at','closed_by_issuer','closed_by_subject','close_reconciliation',
                    'created_at','created_by_issuer','created_by_subject'))=8
                  AND (SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.fiscal_periods'::regclass)
                  AND EXISTS(SELECT 1 FROM pg_constraint WHERE conname='period_close_metadata')
                  AND EXISTS(SELECT 1 FROM pg_constraint WHERE conname='period_create_metadata')
                  AND EXISTS(SELECT 1 FROM pg_constraint WHERE conname='account_create_metadata')
                  AND to_regprocedure('finance.lock_book_for_period_create()') IS NOT NULL
                  AND to_regprocedure('finance.lock_book_for_account_create()') IS NOT NULL
                  AND to_regprocedure('finance.lock_company_for_book_create()') IS NOT NULL
                  AND EXISTS(SELECT 1 FROM pg_constraint WHERE conname='book_setup_valid')
                  AND (SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.journal_corrections'::regclass)
                  AND (SELECT count(*) FROM information_schema.columns WHERE table_schema='finance'
                    AND table_name='accounting_books' AND column_name IN ('id','code','name','status','created_at','created_by_issuer','created_by_subject'))=7
                  AND (SELECT count(*) FROM information_schema.columns WHERE table_schema='finance'
                    AND table_name='accounts' AND column_name IN ('created_at','created_by_issuer','created_by_subject'))=3
                """);
            Assert.True((bool)(await inspect.ExecuteScalarAsync())!);
        }
        finally { await db.Execute($"DROP DATABASE {name} WITH (FORCE)"); }
    }

    [Fact]
    public async Task Sequential_previous_to_close_to_setup_keeps_period_and_posted_history_exact()
    {
        var name = "setup_upgrade_" + Guid.NewGuid().ToString("N");
        await db.Execute($"CREATE DATABASE {name} OWNER erp_migrator");
        try
        {
            var migration = new NpgsqlConnectionStringBuilder(db.Migration) { Database = name }.ToString();
            var adminString = new NpgsqlConnectionStringBuilder(db.Admin) { Database = name }.ToString();
            await MigrationRunner.ApplyAsync(migration);
            await using var connection = new NpgsqlConnection(migration); await connection.OpenAsync();
            await using var schema = new FinanceDbContext(FinanceDbContext.Options(connection));
            Assert.Equal(OrderedMigrations, schema.Database.GetMigrations().ToArray());
            foreach (var (index, migrationId) in OrderedMigrations.Take(3).Select((x, i) => (i, x)))
            {
                await schema.GetService<IMigrator>().MigrateAsync(migrationId);
                Assert.Equal(OrderedMigrations.Take(index + 1), await schema.Database.GetAppliedMigrationsAsync());
            }
            var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
            var otherTenant = Guid.NewGuid(); var otherCompany = Guid.NewGuid();
            var open = Guid.NewGuid(); var closed = Guid.NewGuid(); var expense = Guid.NewGuid(); var cash = Guid.NewGuid();
            await using var admin = new NpgsqlConnection(adminString); await admin.OpenAsync();
            await Sql.ExecuteAsync(admin, """
                INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@tenant,@company,'JOD',3);
                INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@otherTenant,@otherCompany,'EUR',2);
                INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status)
                  VALUES(@closed,@tenant,@company,2026,'2026-01-01','2026-01-31','Open'),
                        (@open,@tenant,@company,2026,'2026-02-01','2026-02-28','Open');
                INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed)
                  VALUES(@expense,@tenant,@company,'5000','Expense','Active',true),
                        (@cash,@tenant,@company,'1000','Cash','Active',true);
                """, default, ("tenant", tenant), ("company", company), ("closed", closed),
                ("open", open), ("expense", expense), ("cash", cash),
                ("otherTenant", otherTenant), ("otherCompany", otherCompany));
            // Materialize an already-existing valid historical row on the prior schema. Current EF mappings
            // include Setup-only columns and cannot run against the prior schema under test.
            var journal = Guid.NewGuid(); var reversal = Guid.NewGuid();
            await Sql.ExecuteAsync(admin, """
                ALTER TABLE finance.fiscal_periods DISABLE TRIGGER USER;
                UPDATE finance.fiscal_periods SET status='Closed',revision=2,closed_at='2026-02-02T10:00:00Z',
                  closed_by_issuer='tests',closed_by_subject='historical-actor',
                  close_comment='Historical verified close',close_reconciliation='{"passed":true}' WHERE id=@closed;
                ALTER TABLE finance.fiscal_periods ENABLE TRIGGER USER;
                INSERT INTO finance.journal_numbers VALUES(@tenant,@company,2026,2);
                ALTER TABLE finance.journal_entries DISABLE TRIGGER USER;
                ALTER TABLE finance.journal_lines DISABLE TRIGGER USER;
                INSERT INTO finance.journal_entries(id,tenant_id,company_id,status,revision,posting_date,currency,
                  reference,description,rounding_policy,fiscal_period_id,fiscal_year,number_sequence,journal_number,
                  posted_by_issuer,posted_by_subject,posted_at,created_at)
                  VALUES(@journal,@tenant,@company,'Posted',2,'2026-01-02','JOD','pre-setup',
                    'historical posted journal','internal-to-even-v1',@closed,2026,1,'MJ-2026-000001',
                    'tests','historical-actor','2026-01-02T10:00:00Z','2026-01-01T10:00:00Z');
                INSERT INTO finance.journal_entries(id,tenant_id,company_id,status,revision,posting_date,currency,
                  reference,description,rounding_policy,fiscal_period_id,fiscal_year,number_sequence,journal_number,
                  posted_by_issuer,posted_by_subject,posted_at,created_at)
                  VALUES(@reversal,@tenant,@company,'Posted',2,'2026-01-03','JOD','pre-setup-reversal',
                    'historical reversal','internal-to-even-v1',@closed,2026,2,'MJ-2026-000002',
                    'tests','historical-actor','2026-01-03T10:00:00Z','2026-01-03T09:00:00Z');
                INSERT INTO finance.journal_lines(id,journal_id,tenant_id,company_id,position,account_id,debit,credit,description)
                  VALUES(gen_random_uuid(),@journal,@tenant,@company,1,@expense,100,0,''),
                        (gen_random_uuid(),@journal,@tenant,@company,2,@cash,0,100,''),
                        (gen_random_uuid(),@reversal,@tenant,@company,1,@expense,0,100,''),
                        (gen_random_uuid(),@reversal,@tenant,@company,2,@cash,100,0,'');
                ALTER TABLE finance.journal_lines ENABLE TRIGGER USER;
                ALTER TABLE finance.journal_entries ENABLE TRIGGER USER;
                ALTER TABLE finance.journal_reversals DISABLE TRIGGER USER;
                INSERT INTO finance.journal_reversals(tenant_id,company_id,original_journal_id,reversal_journal_id,
                  posting_date,reason,actor_issuer,actor_subject,request_id,correlation_id,timestamp)
                  VALUES(@tenant,@company,@journal,@reversal,'2026-01-03','Historical correction',
                    'tests','historical-actor','historical-request','historical-correlation','2026-01-03T10:00:00Z');
                ALTER TABLE finance.journal_reversals ENABLE TRIGGER USER;
                """, default, ("tenant", tenant), ("company", company), ("closed", closed),
                ("journal", journal), ("reversal", reversal), ("expense", expense), ("cash", cash));
            async Task<string> HistoricalDigest()
            {
                await using var command = Sql.Command(admin, """
                    SELECT md5(coalesce(string_agg(value,'|' ORDER BY value),'')) FROM (
                      SELECT to_jsonb((p.id,p.tenant_id,p.company_id,p.fiscal_year,p.starts_on,p.ends_on,p.status,p.revision,
                        p.closed_at,p.closed_by_issuer,p.closed_by_subject,p.close_comment,p.close_reconciliation))::text value
                        FROM finance.fiscal_periods p WHERE p.tenant_id=@tenant AND p.company_id=@company
                      UNION ALL SELECT to_jsonb(j)::text FROM finance.journal_entries j WHERE j.tenant_id=@tenant AND j.company_id=@company
                      UNION ALL SELECT to_jsonb(l)::text FROM finance.journal_lines l WHERE l.tenant_id=@tenant AND l.company_id=@company
                      UNION ALL SELECT to_jsonb(r)::text FROM finance.journal_reversals r WHERE r.tenant_id=@tenant AND r.company_id=@company
                      UNION ALL SELECT to_jsonb((a.id,a.tenant_id,a.company_id,a.code,a.name,a.status,a.posting_allowed))::text
                        FROM finance.accounts a WHERE a.tenant_id=@tenant AND a.company_id=@company
                      UNION ALL SELECT to_jsonb((b.tenant_id,b.company_id,b.currency,b.minor_units))::text
                        FROM finance.accounting_books b WHERE b.tenant_id=@tenant AND b.company_id=@company
                    ) history
                    """, ("tenant", tenant), ("company", company));
                return (string)(await command.ExecuteScalarAsync())!;
            }
            var before = await HistoricalDigest();
            await schema.GetService<IMigrator>().MigrateAsync(OrderedMigrations[3]);
            Assert.Equal(OrderedMigrations.Take(4), (await schema.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(before, await HistoricalDigest());
            await using var check = Sql.Command(admin, """
                SELECT count(*)=2 AND count(*) FILTER (WHERE created_at IS NOT NULL OR created_by_issuer IS NOT NULL
                  OR created_by_subject IS NOT NULL)=0 AND count(*) FILTER (WHERE status='Closed' AND
                  closed_by_subject='historical-actor' AND close_comment='Historical verified close'
                  AND closed_at='2026-02-02T10:00:00Z')=1
                  AND count(*) FILTER (WHERE status='Open')=1
                FROM finance.fiscal_periods WHERE tenant_id=@tenant AND company_id=@company
                """, ("tenant", tenant), ("company", company));
            Assert.True((bool)(await check.ExecuteScalarAsync())!);
            await using var linked = Sql.Command(admin, """
                SELECT count(*)=2 FROM finance.journal_entries j JOIN finance.fiscal_periods p
                ON (j.tenant_id,j.company_id,j.fiscal_period_id)=(p.tenant_id,p.company_id,p.id)
                WHERE j.tenant_id=@tenant AND j.company_id=@company AND j.status='Posted' AND p.status='Closed'
                """, ("tenant", tenant), ("company", company));
            Assert.True((bool)(await linked.ExecuteScalarAsync())!);
            await using var balanced = Sql.Command(admin, """
                SELECT count(*)=2 AND sum(debit)=sum(credit) AND sum(debit)=100
                FROM finance.journal_lines WHERE journal_id=@journal
                """, ("journal", journal));
            Assert.True((bool)(await balanced.ExecuteScalarAsync())!);
            await using var link = Sql.Command(admin, """
                SELECT count(*)=1 FROM finance.journal_reversals r
                JOIN finance.journal_entries o ON o.id=r.original_journal_id
                JOIN finance.journal_entries v ON v.id=r.reversal_journal_id
                WHERE r.original_journal_id=@journal AND r.reversal_journal_id=@reversal
                  AND o.status='Posted' AND v.status='Posted' AND r.reason='Historical correction'
                """, ("journal", journal), ("reversal", reversal));
            Assert.True((bool)(await link.ExecuteScalarAsync())!);
            await schema.GetService<IMigrator>().MigrateAsync(OrderedMigrations[4]);
            Assert.Equal(OrderedMigrations.Take(5), (await schema.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(before, await HistoricalDigest());
            await using var accounts = Sql.Command(admin, """
                SELECT count(*)=2 AND count(*) FILTER(WHERE created_at IS NOT NULL OR created_by_issuer IS NOT NULL
                  OR created_by_subject IS NOT NULL)=0 AND count(*) FILTER(WHERE status='Active' AND posting_allowed)=2
                FROM finance.accounts WHERE tenant_id=@tenant AND company_id=@company
                """, ("tenant", tenant), ("company", company));
            Assert.True((bool)(await accounts.ExecuteScalarAsync())!);
            await schema.GetService<IMigrator>().MigrateAsync(OrderedMigrations[5]);
            Assert.Equal(OrderedMigrations.Take(6), (await schema.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(before, await HistoricalDigest());
            await using var book = Sql.Command(admin, """
                SELECT id=md5('erp.finance.accounting-book.v1:'||tenant_id::text||':'||company_id::text)::uuid
                  AND code='PRIMARY' AND name='Legacy Accounting Book' AND status='Active'
                  AND currency='JOD' AND minor_units=3 AND created_at IS NULL
                  AND created_by_issuer IS NULL AND created_by_subject IS NULL
                FROM finance.accounting_books WHERE tenant_id=@tenant AND company_id=@company
                """, ("tenant", tenant), ("company", company));
            Assert.True((bool)(await book.ExecuteScalarAsync())!);
            await using var otherBook = Sql.Command(admin, """
                SELECT id=md5('erp.finance.accounting-book.v1:'||tenant_id::text||':'||company_id::text)::uuid
                  AND currency='EUR' AND minor_units=2 AND code='PRIMARY' AND status='Active'
                FROM finance.accounting_books WHERE tenant_id=@tenant AND company_id=@company
                """, ("tenant", otherTenant), ("company", otherCompany));
            Assert.True((bool)(await otherBook.ExecuteScalarAsync())!);
            await schema.GetService<IMigrator>().MigrateAsync(OrderedMigrations[6]);
            Assert.Equal(OrderedMigrations, (await schema.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.False(schema.Database.HasPendingModelChanges());
            Assert.Equal(before, await HistoricalDigest());
            await using var correctionGuard = Sql.Command(admin, "SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.journal_corrections'::regclass");
            Assert.True((bool)(await correctionGuard.ExecuteScalarAsync())!);
            await schema.GetService<IMigrator>().MigrateAsync();
            await using var countBooks = Sql.Command(admin, "SELECT count(*) FROM finance.accounting_books");
            Assert.Equal(2L, await countBooks.ExecuteScalarAsync());
        }
        finally { await db.Execute($"DROP DATABASE {name} WITH (FORCE)"); }
    }

}
