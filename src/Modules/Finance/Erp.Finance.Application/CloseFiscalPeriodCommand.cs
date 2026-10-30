using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.Finance.Application;

public interface IPeriodStore
{
    Task<IReadOnlyList<FiscalPeriodView>> ListAsync(IApplicationTransaction transaction, CancellationToken token);
    Task<FiscalPeriodView> LoadAsync(IApplicationTransaction transaction, Guid id, bool lockRoot, CancellationToken token);
    Task<PeriodClosePreview> ValidateAsync(IApplicationTransaction transaction, FiscalPeriodView period, CancellationToken token);
    Task CloseAsync(IApplicationTransaction transaction, FiscalPeriodView period, string? comment, PeriodClosePreview preview, DateTimeOffset now,
        PrincipalIdentity actor, CancellationToken token);
    Task<Guid> CreateAsync(IApplicationTransaction transaction, DateOnly start, DateOnly end, int fiscalYear,
        DateTimeOffset now, PrincipalIdentity actor, CancellationToken token);
}

public sealed class FiscalPeriodQueries(IUnitOfWorkExecutor uow, IPeriodStore periods)
{
    public Task<IReadOnlyList<FiscalPeriodView>> ListAsync(VerifiedContext context, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, PeriodPermissions.View);
        return uow.ExecuteAsync(context, (tx, ct) => periods.ListAsync(tx, ct), token);
    }

    public Task<PeriodClosePreview> PreviewAsync(VerifiedContext context, Guid id, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, PeriodPermissions.Close);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var period = await periods.LoadAsync(tx, id, false, ct);
            if (period.Status != "Open") return new(period, false, "0", "0", [new("PERIOD_OPEN", false, "Open", period.Status)]);
            return await periods.ValidateAsync(tx, period, ct);
        }, token);
    }
}

public sealed class CloseFiscalPeriodCommand(IIdempotentCommands idempotency, IPeriodStore periods,
    IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, Guid id, long expected,
        ClosePeriodRequest request, CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, PeriodPermissions.Close);
        if (id == Guid.Empty || expected < 1 || request is null || identity.Command != "Finance.CloseFiscalPeriod"
            || identity.Target != id.ToString()) throw new FoundationException(Errors.VALIDATION_FAILED);
        var comment = FiscalPeriodClose.ValidateComment(request.Comment);
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var period = await periods.LoadAsync(tx, id, true, ct);
            FiscalPeriodClose.RequireOpen(Enum.Parse<FiscalPeriodStatus>(period.Status), period.Revision, expected);
            var preview = await periods.ValidateAsync(tx, period, ct);
            if (!preview.Passed) throw new FoundationException(PeriodCloseErrors.ReconciliationFailed);
            var instant = clock.GetUtcNow();
            var now = instant.AddTicks(-(instant.Ticks % 10)); // PostgreSQL timestamp precision is one microsecond.
            await periods.CloseAsync(tx, period, comment, preview, now, context.Identity, ct);
            var evidence = new Dictionary<string, object?>
            {
                ["reconciliationPassed"] = preview.Passed,
                ["periodDebit"] = preview.PeriodDebit,
                ["periodCredit"] = preview.PeriodCredit,
                ["comment"] = comment
            };
            audit.Append(tx, new("FiscalPeriodCloseRequested", "FiscalPeriod", id.ToString(), After: evidence));
            audit.Append(tx, new("FiscalPeriodClosed", "FiscalPeriod", id.ToString(), After: evidence));
            var payload = new FiscalPeriodClosedV1(context.TenantId, context.CompanyId, id, period.StartDate, period.EndDate, now);
            outbox.Append(tx, new(Guid.CreateVersion7(), FiscalPeriodClosedV1.EventType, id.ToString(),
                JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), now));
            return new CommandReceipt(id, expected + 1);
        }, token);
    }
}
