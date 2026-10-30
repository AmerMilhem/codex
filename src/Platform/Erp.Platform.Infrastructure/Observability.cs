using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Erp.Platform.Infrastructure;

public static class FoundationTelemetry
{
    public const string Name = "Erp.Foundation";
    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Transactions = Meter.CreateCounter<long>("erp.uow.transactions");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("erp.uow.retries");
    public static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("erp.outbox.deliveries");
    public static IServiceCollection AddFoundationTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry().WithTracing(t => t.AddAspNetCoreInstrumentation()
            .AddSource(Name, "Npgsql")).WithMetrics(m => m.AddAspNetCoreInstrumentation().AddMeter(Name));
        // Exporter destinations/credentials are deliberately not guessed. Traces/metrics can be attached via DI.
        return services;
    }
}

public sealed class DatabaseReadiness(NpgsqlDataSource source) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken token = default)
    {
        try
        {
            await using var connection = await source.OpenConnectionAsync(token);
            await using var command = new NpgsqlCommand("""
                SELECT NOT rolsuper AND NOT rolbypassrls AND
                NOT EXISTS(SELECT 1 FROM pg_tables WHERE schemaname IN ('integration','audit') AND tableowner=current_user)
                AND EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId"='20261018000200_IndexOutboxLeaseRecovery')
                AND (SELECT count(*)=4 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                     WHERE n.nspname IN ('integration','audit') AND c.relname IN ('idempotency','outbox','inbox','entries')
                     AND c.relrowsecurity AND c.relforcerowsecurity)
                FROM pg_roles WHERE rolname=current_user
                """, connection) { CommandTimeout = 3 };
            return await command.ExecuteScalarAsync(token) is true ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database role/schema protection is not ready.");
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("Database readiness unavailable."); }
    }
}

public sealed class RuntimeDatabaseGuard(DatabaseReadiness readiness) : IHostedService
{
    public async Task StartAsync(CancellationToken token)
    {
        var result = await readiness.CheckHealthAsync(new HealthCheckContext(), token);
        if (result.Status != HealthStatus.Healthy)
            throw new InvalidOperationException("Database role, RLS or migration gate failed; runtime will not start.");
    }
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}
