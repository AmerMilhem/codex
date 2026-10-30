using Erp.Kernel;

namespace Erp.Application.Abstractions;

public sealed record PrincipalIdentity(string Issuer, string Subject);
public enum ExecutionSource { Api, Worker, Test }

public sealed record CompanyGrant(Guid TenantId, Guid CompanyId, IReadOnlySet<string> Permissions);
public sealed record PrincipalMembership(bool Enabled, IReadOnlyList<CompanyGrant> Grants);

public interface IMembershipStore
{
    ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken cancellationToken);
}

// Constructed from authenticated server identity + current ERP grants, never directly from a request DTO.
public sealed class VerifiedContext
{
    internal VerifiedContext(PrincipalIdentity identity, Guid tenantId, Guid companyId, string action,
        string correlationId, string requestId, ExecutionSource source)
    {
        Identity = identity; TenantId = tenantId; CompanyId = companyId; Action = action;
        CorrelationId = correlationId; RequestId = requestId; Source = source;
    }
    public PrincipalIdentity Identity { get; }
    public Guid TenantId { get; }
    public Guid CompanyId { get; }
    public string Action { get; }
    public string CorrelationId { get; }
    public string RequestId { get; }
    public ExecutionSource Source { get; }
    public VerifiedContext ContinueCorrelation(string correlationId)
    {
        if (correlationId.Length is < 1 or > 64 || !correlationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new FoundationException(Errors.VALIDATION_FAILED);
        return new(Identity, TenantId, CompanyId, Action, correlationId, RequestId, Source);
    }
}

public sealed class ContextResolver(IMembershipStore memberships)
{
    public async ValueTask<VerifiedContext> ResolveAsync(PrincipalIdentity identity, Guid tenantId, Guid companyId,
        string action, string correlationId, string requestId, ExecutionSource source, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || string.IsNullOrWhiteSpace(action))
            throw new FoundationException(Errors.TENANT_CONTEXT_INVALID);
        var membership = await memberships.FindAsync(identity, cancellationToken);
        if (membership is not { Enabled: true } || !membership.Grants.Any(g =>
                g.TenantId == tenantId && g.CompanyId == companyId && g.Permissions.Contains(action)))
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        return new(identity, tenantId, companyId, action, correlationId, requestId, source);
    }
}

public interface IApplicationTransaction { VerifiedContext Context { get; } }
public interface IUnitOfWorkExecutor
{
    Task<T> ExecuteAsync<T>(VerifiedContext context, Func<IApplicationTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}

public sealed record ModuleDescriptor(string Name, string Schema, bool BusinessFeaturesImplemented = false);
public sealed record CommandReceipt(Guid Reference, long AppliedRevision);
public sealed record CommandIdentity(string Command, string Target, string Key, string Fingerprint);

public interface IIdempotentCommands
{
    Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity command,
        Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken cancellationToken = default);
}

public sealed record AuditIntent(string Action, string EntityType, string EntityId,
    IReadOnlyDictionary<string, object?>? Before = null, IReadOnlyDictionary<string, object?>? After = null);
public interface IAuditWriter { void Append(IApplicationTransaction transaction, AuditIntent intent); }
public sealed record AuditRecord(string Action, string ActorIssuer, string ActorSubject, DateTimeOffset Timestamp,
    Guid TenantId, Guid CompanyId, string RequestId, string CorrelationId);
public interface IAuditReader
{
    Task<IReadOnlyList<AuditRecord>> ReadAsync(IApplicationTransaction transaction, string entityType, string entityId, CancellationToken token);
}
public interface IAuditSnapshotPolicy { string? Mask(IReadOnlyDictionary<string, object?>? snapshot); }

public sealed record IntegrationEvent(Guid Id, string Type, string SourceId, string Payload, DateTimeOffset OccurredAt);
public interface IOutboxWriter { void Append(IApplicationTransaction transaction, IntegrationEvent message); }
public interface IIntegrationEventHandler
{
    string EventType { get; }
    string Consumer { get; }
    Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken cancellationToken);
}

public interface ISecretProvider { string? Get(string name); }
public sealed class CommitOutcomeUnknownException : Exception
{
    public CommitOutcomeUnknownException(Exception inner) : base("Commit outcome is unknown; query or replay the same intent.", inner) { }
}
