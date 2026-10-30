using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;

namespace Erp.Platform.Infrastructure;

public sealed class OutboxWriter(TimeProvider clock) : IOutboxWriter
{
    public void Append(IApplicationTransaction transaction, IntegrationEvent message)
    {
        if (message.Id == Guid.Empty || message.Type.Length is < 1 or > 128 || message.SourceId.Length is < 1 or > 128
            || message.Payload.Length > 65536) throw new FoundationException(Errors.VALIDATION_FAILED);
        using var document = JsonDocument.Parse(message.Payload, new JsonDocumentOptions { MaxDepth = 32 });
        var tx = PostgresTransaction.Require(transaction);
        tx.Platform.Outbox.Add(new OutboxRow
        {
            Id = message.Id,
            TenantId = tx.Context.TenantId,
            CompanyId = tx.Context.CompanyId,
            Type = message.Type,
            SourceId = message.SourceId,
            Payload = message.Payload,
            OccurredAt = message.OccurredAt.ToUniversalTime(),
            AvailableAt = clock.GetUtcNow(),
            TraceParent = Activity.Current?.Id,
            CorrelationId = tx.Context.CorrelationId
        });
    }
}

public sealed record OutboxLease(IntegrationEvent Message, Guid LeaseId, string CorrelationId, string? TraceParent);
public sealed class OutboxStore(IUnitOfWorkExecutor executor)
{
    public Task<OutboxLease?> ClaimAsync(VerifiedContext context, CancellationToken token = default) => executor.ExecuteAsync(context,
        async (transaction, ct) =>
        {
            var tx = PostgresTransaction.Require(transaction); var lease = Guid.NewGuid();
            await using var command = Sql.Command(tx.Connection, """
                UPDATE integration.outbox SET state='Processing',lease_id=@lease,
                    lease_until=clock_timestamp()+interval '30 seconds',attempts=attempts+1
                WHERE id=(SELECT id FROM integration.outbox
                    WHERE ((state='Pending' AND available_at<=clock_timestamp())
                        OR (state='Processing' AND lease_until<clock_timestamp())) AND attempts<5
                    ORDER BY available_at,id FOR UPDATE SKIP LOCKED LIMIT 1)
                RETURNING id,type,source_id,payload::text,occurred_at,correlation_id,trace_parent
                """, ("lease", lease));
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new OutboxLease(new IntegrationEvent(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)), lease, reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6));
        }, token);

    public Task<bool> AcknowledgeAsync(VerifiedContext context, OutboxLease lease, CancellationToken token = default) =>
        executor.ExecuteAsync(context, async (transaction, ct) => await Sql.ExecuteAsync(PostgresTransaction.Require(transaction).Connection,
            "UPDATE integration.outbox SET state='Completed',lease_id=NULL,lease_until=NULL,failure_code=NULL WHERE id=@id AND lease_id=@lease AND lease_until>clock_timestamp()",
            ct, ("id", lease.Message.Id), ("lease", lease.LeaseId)) == 1, token);

    public Task<bool> FailAsync(VerifiedContext context, OutboxLease lease, bool permanent, CancellationToken token = default) =>
        executor.ExecuteAsync(context, async (transaction, ct) => await Sql.ExecuteAsync(PostgresTransaction.Require(transaction).Connection,
            """
            UPDATE integration.outbox SET state=CASE WHEN @permanent OR attempts>=5 THEN 'Failed' ELSE 'Pending' END,
                available_at=clock_timestamp()+make_interval(secs=>least(60.0,power(2.0,attempts))),
                lease_id=NULL,lease_until=NULL,failure_code=CASE WHEN @permanent THEN 'HANDLER_REJECTED' ELSE 'DELIVERY_FAILED' END
            WHERE id=@id AND lease_id=@lease AND lease_until>clock_timestamp()
            """, ct, ("permanent", permanent), ("id", lease.Message.Id), ("lease", lease.LeaseId)) == 1, token);

    public Task<int> ReapExhaustedAsync(VerifiedContext context, CancellationToken token = default) =>
        executor.ExecuteAsync(context, (transaction, ct) => Sql.ExecuteAsync(PostgresTransaction.Require(transaction).Connection,
            "UPDATE integration.outbox SET state='Failed',failure_code='LEASE_RETRIES_EXHAUSTED',lease_id=NULL,lease_until=NULL WHERE state='Processing' AND lease_until<clock_timestamp() AND attempts>=5", ct), token);
}

public sealed class InboxProcessor(IUnitOfWorkExecutor executor)
{
    public Task<bool> ProcessAsync(VerifiedContext context, string consumer, IntegrationEvent message,
        Func<IApplicationTransaction, CancellationToken, Task> effect, CancellationToken token = default)
    {
        if (consumer.Length is < 1 or > 128 || message.Payload.Length > 65536) throw new FoundationException(Errors.VALIDATION_FAILED);
        using var json = JsonDocument.Parse(message.Payload);
        // PostgreSQL jsonb normalizes formatting. Fingerprint semantic canonical JSON, not transport whitespace.
        var hash = RequestFingerprint.Create("EVENT", message.Type, message.SourceId, json.RootElement.GetRawText());
        return executor.ExecuteAsync(context, async (transaction, ct) =>
        {
            var tx = PostgresTransaction.Require(transaction);
            var inserted = await Sql.ExecuteAsync(tx.Connection, """
                INSERT INTO integration.inbox(tenant_id,company_id,consumer,event_id,payload_hash,processed_at)
                VALUES(@tenant,@company,@consumer,@id,@hash,clock_timestamp()) ON CONFLICT DO NOTHING
                """, ct, ("tenant", context.TenantId), ("company", context.CompanyId), ("consumer", consumer),
                ("id", message.Id), ("hash", hash));
            if (inserted == 0)
            {
                await using var command = Sql.Command(tx.Connection,
                    "SELECT payload_hash FROM integration.inbox WHERE consumer=@consumer AND event_id=@id",
                    ("consumer", consumer), ("id", message.Id));
                if ((string?)await command.ExecuteScalarAsync(ct) != hash) throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
                return false;
            }
            await effect(transaction, ct); return true;
        }, token);
    }
}

public sealed class OutboxPump(OutboxStore store, InboxProcessor inbox, IEnumerable<IIntegrationEventHandler> handlers)
{
    public async Task<bool> RunOnceAsync(VerifiedContext context, CancellationToken token = default)
    {
        if (context.Source != ExecutionSource.Worker || context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        await store.ReapExhaustedAsync(context, token);
        var lease = await store.ClaimAsync(context, token); if (lease is null) return false;
        var parent = ActivityContext.TryParse(lease.TraceParent, null, out var parsed) ? parsed : default;
        using var activity = FoundationTelemetry.Source.StartActivity("outbox.dispatch", ActivityKind.Consumer, parent);
        var consumerContext = context.ContinueCorrelation(lease.CorrelationId);
        try
        {
            var consumers = handlers.Where(h => h.EventType == lease.Message.Type).ToArray();
            if (consumers.Length == 0) throw new FoundationException(Errors.RESOURCE_NOT_FOUND);
            foreach (var handler in consumers)
                await inbox.ProcessAsync(consumerContext, handler.Consumer, lease.Message,
                    (tx, ct) => handler.HandleAsync(tx, lease.Message, ct), token);
            await store.AcknowledgeAsync(context, lease, token);
            FoundationTelemetry.Deliveries.Add(1, new KeyValuePair<string, object?>("outcome", "delivered"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; } // lease expires after crash/shutdown
        catch (Exception error)
        {
            await store.FailAsync(context, lease, error is FoundationException, token);
            FoundationTelemetry.Deliveries.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
        }
        return true;
    }
}
