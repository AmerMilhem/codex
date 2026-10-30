using Microsoft.EntityFrameworkCore;

namespace Erp.Platform.Infrastructure.Persistence;

public sealed class IdempotencyRow
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Caller { get; set; } = "";
    public string Command { get; set; } = "";
    public string Target { get; set; } = "";
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string State { get; set; } = "Processing";
    public Guid? Reference { get; set; }
    public long? AppliedRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class OutboxRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Type { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceParent { get; set; }
    public string CorrelationId { get; set; } = "";
    public string State { get; set; } = "Pending";
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public int Attempts { get; set; }
    public string? FailureCode { get; set; }
}
public sealed class InboxRow
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Consumer { get; set; } = "";
    public Guid EventId { get; set; }
    public string PayloadHash { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}
public sealed class AuditRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string ActorIssuer { get; set; } = "";
    public string ActorSubject { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string CorrelationId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
}

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();
    public DbSet<AuditRow> Audit => Set<AuditRow>();
    public DbSet<IdempotencyRow> Idempotency => Set<IdempotencyRow>();
    public DbSet<InboxRow> Inbox => Set<InboxRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<IdempotencyRow>(e =>
        {
            e.ToTable("idempotency", "integration", t => t.HasCheckConstraint("ck_idempotency_state",
                "state IN ('Processing','Completed') AND (state <> 'Completed' OR (reference IS NOT NULL AND applied_revision IS NOT NULL))"));
            e.HasKey(x => new { x.TenantId, x.CompanyId, x.Caller, x.Command, x.Target, x.Key });
            e.Property(x => x.Caller).HasMaxLength(64); e.Property(x => x.Command).HasMaxLength(96);
            e.Property(x => x.Target).HasMaxLength(128); e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Fingerprint).HasMaxLength(64); e.Property(x => x.State).HasMaxLength(16);
        });
        b.Entity<OutboxRow>(e =>
        {
            e.ToTable("outbox", "integration", t => t.HasCheckConstraint("ck_outbox_state",
                "state IN ('Pending','Processing','Completed','Failed') AND attempts >= 0"));
            e.HasKey(x => x.Id); e.HasIndex(x => new { x.TenantId, x.CompanyId, x.State, x.AvailableAt });
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.LeaseUntil }).HasFilter("state = 'Processing'").HasDatabaseName("ix_outbox_lease_recovery");
            e.Property(x => x.Type).HasMaxLength(128); e.Property(x => x.SourceId).HasMaxLength(128);
            e.Property(x => x.Payload).HasColumnType("jsonb"); e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.FailureCode).HasMaxLength(64); e.Property(x => x.TraceParent).HasMaxLength(128);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
        });
        b.Entity<InboxRow>(e =>
        {
            e.ToTable("inbox", "integration");
            e.HasKey(x => new { x.TenantId, x.CompanyId, x.Consumer, x.EventId });
            e.Property(x => x.Consumer).HasMaxLength(128); e.Property(x => x.PayloadHash).HasMaxLength(64);
        });
        b.Entity<AuditRow>(e =>
        {
            e.ToTable("entries", "audit"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.Timestamp });
            e.Property(x => x.ActorIssuer).HasMaxLength(128); e.Property(x => x.ActorSubject).HasMaxLength(128);
            e.Property(x => x.Action).HasMaxLength(128); e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.EntityId).HasMaxLength(128); e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.CorrelationId).HasMaxLength(64); e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.Before).HasColumnType("jsonb"); e.Property(x => x.After).HasColumnType("jsonb");
        });
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}
