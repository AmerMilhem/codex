using Erp.Application.Abstractions;
using Erp.Finance.Infrastructure;
using Erp.Platform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
builder.Services.AddPlatformFoundation(builder.Configuration,
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"));
builder.Services.AddFinanceFoundation();
builder.Services.AddHostedService<FoundationWorker>();
await builder.Build().RunAsync();

internal sealed class FoundationWorker(OutboxPump pump, ContextResolver resolver,
    DevelopmentIdentityOptions identity, ILogger<FoundationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Local-only server-configured grants; no scope taken from an untrusted event payload.
            foreach (var grant in identity.Grants.Where(g => identity.Enabled && g.Permissions.Contains("outbox.dispatch")))
            {
                try
                {
                    var context = await resolver.ResolveAsync(new("development-adapter", identity.Subject),
                        grant.TenantId, grant.CompanyId, "outbox.dispatch", Guid.NewGuid().ToString("N"),
                        Guid.NewGuid().ToString("N"), ExecutionSource.Worker, stoppingToken);
                    using var activity = FoundationTelemetry.Source.StartActivity("worker.poll");
                    for (var i = 0; i < 20 && await pump.RunOnceAsync(context, stoppingToken); i++) { }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception error) { logger.LogWarning("Worker poll failed ({ExceptionType}); next poll will retry", error.GetType().Name); }
            }
        }
    }
}
