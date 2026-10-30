using System.Text.Json.Serialization;
using Erp.Application.Abstractions;
using Erp.Finance.Infrastructure;
using Erp.Inventory.Infrastructure;
using Erp.Localization.Infrastructure;
using Erp.Manufacturing.Infrastructure;
using Erp.Platform.Infrastructure;
using Erp.Procurement.Infrastructure;
using Erp.Sales.Infrastructure;
using Erp.Workflow.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Erp.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
        builder.WebHost.ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxRequestBodySize = 65536; });
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.MaxDepth = 32; o.SerializerOptions.PropertyNameCaseInsensitive = false;
        });
        builder.Services.AddPlatformFoundation(builder.Configuration,
            builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"));
        builder.Services.AddFinanceFoundation().AddInventoryFoundation().AddSalesFoundation().AddProcurementFoundation()
            .AddManufacturingFoundation().AddWorkflowFoundation().AddLocalizationFoundation();
        builder.Services.AddSingleton<IAuthenticatedIdentity, AuthenticatedIdentity>();
        builder.Services.AddAuthentication("DevelopmentOnly").AddScheme<AuthenticationSchemeOptions, DevelopmentAuthentication>("DevelopmentOnly", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test")) app.UseHsts();
        app.UseMiddleware<RequestFoundationMiddleware>();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseMiddleware<CompanyScopeMiddleware>();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = x => x.Tags.Contains("ready") });
        app.MapGet("/api/v1/tenants/{tenantId}/companies/{companyId}/context", async (HttpContext http, IMembershipStore memberships) =>
        {
            var context = (VerifiedContext)http.Items[typeof(VerifiedContext)]!;
            var membership = await memberships.FindAsync(context.Identity, http.RequestAborted);
            var permissions = membership?.Grants.Where(g => g.TenantId == context.TenantId && g.CompanyId == context.CompanyId)
                .SelectMany(g => g.Permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
            return Results.Ok(new { context.TenantId, context.CompanyId, permissions, identityMode = "development-test-adapter; production-identity-pending" });
        }).RequireAuthorization().WithMetadata(new ScopeAction("foundation.context.read"));
        foreach (var contributor in app.Services.GetServices<IEndpointContributor>()) contributor.Map(app);
        app.MapFinanceJournals();
        app.MapFallback((HttpContext context) => Problems.WriteAsync(context, Erp.Kernel.Errors.RESOURCE_NOT_FOUND));
        await app.RunAsync();
    }
}
