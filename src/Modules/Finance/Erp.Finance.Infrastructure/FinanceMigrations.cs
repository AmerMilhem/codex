using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Erp.Finance.Infrastructure;

public static class FinanceMigrations
{
    public static async Task ApplyAsync(string connectionString, CancellationToken token = default)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(token);
        await using var guard = Sql.Command(connection, "SELECT current_user<>'erp_runtime' AND NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname=current_user");
        if (await guard.ExecuteScalarAsync(token) is not true) throw new InvalidOperationException("Use the separate non-superuser migration identity.");
        await using var db = new FinanceDbContext(FinanceDbContext.Options(connection));
        await db.Database.MigrateAsync(token);
    }
}
public sealed class FinanceDatabaseReadiness(NpgsqlDataSource source) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token = default)
    {
        try
        {
            await using var c = await source.OpenConnectionAsync(token);
            await using var cmd = Sql.Command(c, """
                SELECT NOT EXISTS(SELECT 1 FROM pg_tables WHERE schemaname='finance' AND tableowner=current_user)
                  AND (SELECT count(*)=8 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='finance' AND c.relname IN ('accounting_books','accounts','fiscal_periods','journal_entries','journal_lines','journal_numbers','journal_reversals','journal_corrections') AND c.relrowsecurity AND c.relforcerowsecurity)
                  AND EXISTS(SELECT 1 FROM public."__FinanceMigrationsHistory" WHERE "MigrationId"='20261029112730_ManualJournalCorrectionV1')
                """);
            return await cmd.ExecuteScalarAsync(token) is true ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Finance schema protection is not ready.");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Finance database readiness unavailable."); }
    }
}
public sealed class FinanceRuntimeGuard(FinanceDatabaseReadiness readiness) : IHostedService
{
    public async Task StartAsync(CancellationToken token)
    {
        if ((await readiness.CheckHealthAsync(new(), token)).Status != HealthStatus.Healthy)
            throw new InvalidOperationException("Finance RLS/migration gate failed; runtime will not start.");
    }
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}
