using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Api;
using Erp.Application.Abstractions;
using Erp.Platform.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class HttpTests(DatabaseFixture database)
{
    [Fact]
    public async Task API_requires_authentication_and_current_company_membership()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();
        var path = Path(DatabaseFixture.TenantA, DatabaseFixture.CompanyA);
        var anonymous = await client.GetAsync(path); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("auth.required", (await anonymous.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var ok = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("nosniff", ok.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(ok.Headers.CacheControl?.NoStore);
        foreach (var (tenant, company) in new[] { (DatabaseFixture.TenantB, DatabaseFixture.CompanyB), (DatabaseFixture.TenantA, DatabaseFixture.CompanyA2) })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Path(tenant, company))).StatusCode);
        factory.Members.Enabled = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task HTTP_strict_input_idempotency_conflict_and_correlation_flow_to_DB_outbox_worker()
    {
        await database.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        await using var factory = new ApiFactory(database); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "correlation-http-to-worker");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-key");
        var source = Guid.NewGuid().ToString(); var path = $"/test/tenants/{DatabaseFixture.TenantA}/companies/{DatabaseFixture.CompanyA}/record";
        var response = await client.PostAsJsonAsync(path, new { source }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<CommandReceipt>(); Assert.NotNull(receipt);
        var trace = response.Headers.GetValues("X-Trace-ID").Single();
        Assert.Equal("correlation-http-to-worker", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal(receipt, await (await client.PostAsJsonAsync(path, new { source })).Content.ReadFromJsonAsync<CommandReceipt>());
        var invalid = await client.PostAsJsonAsync(path, new { source, posted = true, tenantId = DatabaseFixture.TenantB });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var changed = await client.PostAsJsonAsync(path, new { source, fail = true }); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency.key_reused", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var outboxTrace = await database.Scalar<string>("SELECT trace_parent FROM integration.outbox WHERE source_id=@source", ("source", source));
        Assert.Contains(trace, outboxTrace);
        var pump = new OutboxPump(database.Store, database.Inbox, [new ProbeHandler()]);
        Assert.True(await pump.RunOnceAsync(await database.Context(source: ExecutionSource.Worker, action: "outbox.dispatch")));
        Assert.True(await database.Scalar<bool>("SELECT correlation='correlation-http-to-worker' AND db_correlation=correlation AND trace=@trace AND db_trace=trace FROM foundation_test.consumer_effects WHERE event_id=(SELECT id FROM integration.outbox WHERE source_id=@source)", ("trace", trace), ("source", source)));
        Assert.Contains(factory.Exporter.Spans, a => a.TraceId.ToString() == trace && a.Source.Name == "Npgsql");
        Assert.Contains(factory.Exporter.Spans, a => a.TraceId.ToString() == trace && a.OperationName == "outbox.dispatch");
    }
    [Fact]
    public void Development_identity_cannot_be_enabled_in_production()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DevelopmentIdentity:Enabled"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => services.AddPlatformFoundation(configuration, false));
    }
    [Fact]
    public async Task Readiness_rejects_owner_identity_and_live_endpoint_is_functional()
    {
        await using var factory = new ApiFactory(database); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        await using var unsafeSource = new Npgsql.NpgsqlDataSourceBuilder(database.Migration).Build();
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            (await new DatabaseReadiness(unsafeSource).CheckHealthAsync(new())).Status);
    }
    private static string Path(Guid tenant, Guid company) => $"/api/v1/tenants/{tenant}/companies/{company}/context";
}

public sealed class ApiFactory(DatabaseFixture database) : WebApplicationFactory<Erp.Api.Program>
{
    public const string Token = "development-test-bearer-token-32-characters";
    public HttpMembership Members { get; } = new();
    public CapturingExporter Exporter { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Runtime"] = database.Runtime,
            ["DevelopmentIdentity:Enabled"] = "true",
            ["DevelopmentIdentity:Subject"] = "http-test",
            ["AllowedHosts"] = "*"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DevelopmentIdentityOptions>();
            services.AddSingleton(new DevelopmentIdentityOptions { Enabled = true, Subject = "http-test" });
            services.RemoveAll<ISecretProvider>(); services.AddSingleton<ISecretProvider, TestSecrets>();
            services.RemoveAll<IMembershipStore>(); services.AddSingleton<IMembershipStore>(Members);
            services.AddSingleton<IEndpointContributor>(new TestEndpoint(database));
            services.AddOpenTelemetry().WithTracing(b => b.AddProcessor(new SimpleActivityExportProcessor(Exporter)));
        });
    }
    private sealed class TestSecrets : ISecretProvider { public string? Get(string name) => name == "ERP_DEV_AUTH_TOKEN" ? Token : null; }
    public sealed class HttpMembership : IMembershipStore
    {
        public bool Enabled = true;
        public HashSet<string> Permissions { get; } = ["foundation.context.read", "foundation.execute"];
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) => ValueTask.FromResult<PrincipalMembership?>(
            new(Enabled, [new(DatabaseFixture.TenantA, DatabaseFixture.CompanyA, Permissions.ToHashSet(StringComparer.Ordinal))]));
    }
    private sealed record ProbeInput(string Source, bool Fail = false);
    private sealed class TestEndpoint(DatabaseFixture database) : IEndpointContributor
    {
        public void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) =>
            endpoints.MapPost("/test/tenants/{tenantId}/companies/{companyId}/record",
                async (HttpContext http, ProbeInput input, IIdempotentCommands commands) =>
                {
                    if (!Guid.TryParse(input.Source, out _)) throw new Erp.Kernel.FoundationException(Erp.Kernel.Errors.VALIDATION_FAILED);
                    var context = (VerifiedContext)http.Items[typeof(VerifiedContext)]!;
                    return await commands.ExecuteAsync(context,
                        new("RecordProbe", input.Source, http.Request.Headers["Idempotency-Key"].ToString(),
                            RequestFingerprint.Create("POST", input.Source, "0", JsonSerializer.Serialize(input))),
                        (tx, token) => database.RecordEffect(tx, input.Source, input.Fail, token), http.RequestAborted);
                }).RequireAuthorization().WithMetadata(new ScopeAction("foundation.execute"));
    }
}
public sealed class CapturingExporter : BaseExporter<Activity>
{
    public ConcurrentBag<Activity> Spans { get; } = [];
    public override ExportResult Export(in Batch<Activity> batch) { foreach (var span in batch) Spans.Add(span); return ExportResult.Success; }
}
