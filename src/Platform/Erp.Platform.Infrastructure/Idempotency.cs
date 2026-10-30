using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Platform.Infrastructure;

public static class RequestFingerprint
{
    public static string Create(string method, string route, string originalPrecondition, string body)
    {
        if (body.Length > 65536) throw new FoundationException(Errors.VALIDATION_FAILED);
        using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray(); writer.WriteStringValue(method); writer.WriteStringValue(route);
            writer.WriteStringValue(originalPrecondition); Canonical(writer, json.RootElement); writer.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    private static void Canonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new FoundationException(Errors.VALIDATION_FAILED);
            writer.WriteStartObject();
            foreach (var property in properties.OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Canonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Canonical(writer, item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}

public sealed class IdempotentCommands(IUnitOfWorkExecutor executor, TimeProvider clock) : IIdempotentCommands
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity command,
        Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken cancellationToken = default)
    {
        if (!Safe(command.Command, 96) || !Safe(command.Target, 128) || !Safe(command.Key, 128)
            || command.Fingerprint.Length != 64 || !command.Fingerprint.All(Uri.IsHexDigit))
            throw new FoundationException(Errors.VALIDATION_FAILED);
        var caller = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(context.Identity)));
        return executor.ExecuteAsync(context, async (transaction, token) =>
        {
            var tx = PostgresTransaction.Require(transaction);
            var inserted = await Sql.ExecuteAsync(tx.Connection, """
                INSERT INTO integration.idempotency(tenant_id,company_id,caller,command,target,key,fingerprint,state,created_at)
                VALUES(@tenant,@company,@caller,@command,@target,@key,@fingerprint,'Processing',@created)
                ON CONFLICT DO NOTHING
                """, token, ("tenant", context.TenantId), ("company", context.CompanyId), ("caller", caller),
                ("command", command.Command), ("target", command.Target), ("key", command.Key),
                ("fingerprint", command.Fingerprint), ("created", clock.GetUtcNow()));
            if (inserted == 0)
            {
                var original = await tx.Platform.Idempotency.AsNoTracking().SingleAsync(x => x.Caller == caller
                    && x.Command == command.Command && x.Target == command.Target && x.Key == command.Key, token);
                if (original.Fingerprint != command.Fingerprint) throw new FoundationException(Errors.API_IDEMPOTENCY_CONFLICT);
                if (original.State != "Completed" || original.Reference is null || original.AppliedRevision is null)
                    throw new FoundationException(Errors.SERVICE_UNAVAILABLE);
                return new CommandReceipt(original.Reference.Value, original.AppliedRevision.Value);
            }
            var result = await work(transaction, token);
            await Sql.ExecuteAsync(tx.Connection, """
                UPDATE integration.idempotency SET state='Completed',reference=@reference,applied_revision=@revision
                WHERE caller=@caller AND command=@command AND target=@target AND key=@key
                """, token, ("reference", result.Reference), ("revision", result.AppliedRevision), ("caller", caller),
                ("command", command.Command), ("target", command.Target), ("key", command.Key));
            return result;
        }, cancellationToken);
    }
    private static bool Safe(string value, int max) => value.Length is > 0 && value.Length <= max
        && value.All(c => c is >= '!' and <= '~');
}
