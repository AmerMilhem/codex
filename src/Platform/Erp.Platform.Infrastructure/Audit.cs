using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Platform.Infrastructure;

// Fail closed: callers must introduce an explicitly reviewed policy to retain snapshot values.
public sealed class MaskAllSnapshots : IAuditSnapshotPolicy
{
    public string? Mask(IReadOnlyDictionary<string, object?>? snapshot) => snapshot is null ? null
        : JsonSerializer.Serialize(snapshot.Take(64).ToDictionary(x => x.Key, _ => "[MASKED]"));
}
public sealed class AuditWriter(TimeProvider clock, IAuditSnapshotPolicy masking) : IAuditWriter
{
    public void Append(IApplicationTransaction transaction, AuditIntent intent)
    {
        if (new[] { intent.Action, intent.EntityType, intent.EntityId }.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128))
            throw new FoundationException(Errors.VALIDATION_FAILED);
        var tx = PostgresTransaction.Require(transaction); var c = tx.Context;
        tx.Platform.Audit.Add(new AuditRow
        {
            Id = Guid.CreateVersion7(),
            TenantId = c.TenantId,
            CompanyId = c.CompanyId,
            ActorIssuer = c.Identity.Issuer,
            ActorSubject = c.Identity.Subject,
            Action = intent.Action,
            EntityType = intent.EntityType,
            EntityId = intent.EntityId,
            Timestamp = clock.GetUtcNow(),
            CorrelationId = c.CorrelationId,
            RequestId = c.RequestId,
            Source = c.Source.ToString(),
            Before = masking.Mask(intent.Before),
            After = masking.Mask(intent.After)
        });
    }
}
public sealed class AuditReader : IAuditReader
{
    public async Task<IReadOnlyList<AuditRecord>> ReadAsync(IApplicationTransaction transaction, string entityType, string entityId, CancellationToken token)
        => await PostgresTransaction.Require(transaction).Platform.Audit.AsNoTracking()
            .Where(x => x.EntityType == entityType && x.EntityId == entityId).OrderBy(x => x.Timestamp).ThenBy(x => x.Id)
            .Select(x => new AuditRecord(x.Action, x.ActorIssuer, x.ActorSubject, x.Timestamp, x.TenantId, x.CompanyId, x.RequestId, x.CorrelationId))
            .Take(500).ToArrayAsync(token);
}
