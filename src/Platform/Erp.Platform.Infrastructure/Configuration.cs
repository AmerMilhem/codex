using Erp.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Platform.Infrastructure;

public sealed class EnvironmentSecrets : ISecretProvider
{
    private static readonly HashSet<string> Allowed = ["ERP_RUNTIME_CONNECTION", "ERP_DEV_AUTH_TOKEN", "ERP_MIGRATION_CONNECTION"];
    public string? Get(string name) => Allowed.Contains(name) ? Environment.GetEnvironmentVariable(name)
        : throw new InvalidOperationException("Secret name is not registered.");
}
public sealed class DevelopmentIdentityOptions
{
    public bool Enabled { get; set; }
    public string Subject { get; set; } = "local-developer";
    public List<LocalCompanyGrant> Grants { get; set; } = [];
}
public sealed class LocalCompanyGrant
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public List<string> Permissions { get; set; } = [];
}
public sealed class DevelopmentMembershipStore(DevelopmentIdentityOptions options) : IMembershipStore
{
    public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken cancellationToken)
    {
        if (!options.Enabled || identity != new PrincipalIdentity("development-adapter", options.Subject))
            return ValueTask.FromResult<PrincipalMembership?>(null);
        return ValueTask.FromResult<PrincipalMembership?>(new(true, options.Grants.Select(g =>
            new CompanyGrant(g.TenantId, g.CompanyId, g.Permissions.ToHashSet(StringComparer.Ordinal))).ToArray()));
    }
}
public static class Registration
{
    public static IServiceCollection AddPlatformFoundation(this IServiceCollection services,
        IConfiguration configuration, bool localEnvironment)
    {
        var identity = configuration.GetSection("DevelopmentIdentity").Get<DevelopmentIdentityOptions>() ?? new();
        if (identity.Enabled && !localEnvironment) throw new InvalidOperationException("Development identity is forbidden outside Development/Test.");
        if (identity.Subject.Length is < 1 or > 128 || identity.Grants.Any(g => g.TenantId == Guid.Empty || g.CompanyId == Guid.Empty))
            throw new InvalidOperationException("Invalid server-side development membership configuration.");
        services.AddSingleton(identity);
        services.AddSingleton<ISecretProvider, EnvironmentSecrets>();
        services.AddSingleton<IMembershipStore, DevelopmentMembershipStore>();
        services.AddSingleton<ContextResolver>(); services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp =>
        {
            var secret = sp.GetRequiredService<ISecretProvider>().Get("ERP_RUNTIME_CONNECTION")
                ?? configuration.GetConnectionString("Runtime") ?? throw new InvalidOperationException("Runtime connection is required.");
            var cs = new NpgsqlConnectionStringBuilder(secret);
            if (!localEnvironment && cs.SslMode != SslMode.VerifyFull)
                throw new InvalidOperationException("Nonlocal database requires VerifyFull TLS and trusted CA configuration.");
            if (cs.MaxPoolSize is < 1 or > 100) throw new InvalidOperationException("Database pool must be bounded to 1..100.");
            return new NpgsqlDataSourceBuilder(cs.ConnectionString).Build();
        });
        services.AddSingleton<IUnitOfWorkExecutor, Persistence.PostgresUnitOfWork>();
        services.AddSingleton<IIdempotentCommands, IdempotentCommands>();
        services.AddSingleton<IAuditSnapshotPolicy, MaskAllSnapshots>(); services.AddSingleton<IAuditWriter, AuditWriter>();
        services.AddSingleton<IAuditReader, AuditReader>();
        services.AddSingleton<IOutboxWriter, OutboxWriter>(); services.AddSingleton<InboxProcessor>();
        services.AddSingleton<OutboxStore>(); services.AddSingleton<OutboxPump>();
        services.AddSingleton(new ModuleDescriptor("Platform", "integration/audit"));
        services.AddFoundationTelemetry();
        services.AddSingleton<DatabaseReadiness>();
        services.AddHostedService<RuntimeDatabaseGuard>();
        services.AddHealthChecks().AddCheck<DatabaseReadiness>("postgres", tags: ["ready"]);
        return services;
    }
}
