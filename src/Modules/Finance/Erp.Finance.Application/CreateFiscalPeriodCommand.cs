using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.Finance.Application;

public sealed class CreateFiscalPeriodCommand(IIdempotentCommands idempotency, IPeriodStore periods,
    IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CreateFiscalPeriodRequest request,
        CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, PeriodPermissions.Create);
        if (request is null || identity.Command != "Finance.CreateFiscalPeriod" || identity.Target != "periods")
            throw new FoundationException(Errors.VALIDATION_FAILED);
        var fiscalYear = FiscalPeriodSetup.Validate(request.StartDate, request.EndDate);
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var instant = clock.GetUtcNow();
            var now = instant.AddTicks(-(instant.Ticks % 10));
            var id = await periods.CreateAsync(tx, request.StartDate, request.EndDate, fiscalYear, now, context.Identity, ct);
            var evidence = new Dictionary<string, object?>
            {
                ["startDate"] = request.StartDate,
                ["endDate"] = request.EndDate,
                ["fiscalYear"] = fiscalYear
            };
            audit.Append(tx, new("FiscalPeriodCreateRequested", "FiscalPeriod", id.ToString(), After: evidence));
            audit.Append(tx, new("FiscalPeriodCreated", "FiscalPeriod", id.ToString(), After: evidence));
            var payload = new FiscalPeriodCreatedV1(context.TenantId, context.CompanyId, id,
                request.StartDate, request.EndDate, now);
            outbox.Append(tx, new(Guid.CreateVersion7(), FiscalPeriodCreatedV1.EventType, id.ToString(),
                JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), now));
            return new CommandReceipt(id, 1);
        }, token);
    }
}
