using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
namespace Erp.Finance.Application;

public sealed class ReverseJournalCommand(IIdempotentCommands idempotency, IJournalSessions sessions, IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, Guid id, long expected, ReverseJournalRequest request, CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.Reverse);
        if (request is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 || expected < 1 || identity.Command != "Finance.ReverseManualJournal" || identity.Target != id.ToString())
            throw new FoundationException(Errors.VALIDATION_FAILED);
        if (request.PostingDate is null) throw new FoundationException(ReversalErrors.DateInvalid);
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var session = sessions.Open(tx); var book = await session.BookAsync(true, ct);
            var original = await session.LoadAsync(id, true, ct); var existing = await session.ReversalAsync(id, ct); var now = clock.GetUtcNow();
            if (await session.CorrectionAsync(id, ct) is not null) throw new FoundationException(CorrectionErrors.NotAllowed);
            var reversal = JournalReversal.CreateOpposite(original, expected, existing?.OriginalJournalId == id, existing?.ReversalJournalId == id, request.PostingDate, request.Reason, now);
            var periods = await session.PeriodsAsync(reversal.PostingDate, true, ct);
            if (periods.Count != 1) throw new FoundationException(ReversalErrors.DateInvalid);
            if (periods[0].Status != FiscalPeriodStatus.Open) throw new FoundationException(ReversalErrors.PeriodClosed);
            var accounts = await session.AccountsAsync(reversal.Lines.Select(x => x.AccountId), true, ct);
            var issues = reversal.Validate(book, periods, accounts); if (issues.Count > 0) throw new FoundationException(issues[0].Error);
            // Persist a new Draft only inside this uncommitted transaction; existing DB guards require saved lines before Post.
            session.Add(reversal); await session.FlushAsync(ct);
            var number = await session.AllocateNumberAsync(periods[0].FiscalYear, ct);
            reversal.Post(reversal.Revision, book, periods, accounts, number, context.Identity.Issuer, context.Identity.Subject, now);
            session.AddReversal(JournalReversal.Link(original, reversal, request.Reason, context.Identity.Issuer, context.Identity.Subject, context.RequestId, context.CorrelationId, now));
            // Relationship metadata retains typed reason/date/IDs. Global snapshot masking remains unchanged.
            audit.Append(tx, new("JournalReversalRequested", "ManualJournal", original.Id.ToString()));
            audit.Append(tx, new("JournalReversed", "ManualJournal", original.Id.ToString()));
            audit.Append(tx, new("JournalPosted", "ManualJournal", reversal.Id.ToString()));
            outbox.Append(tx, new(Guid.CreateVersion7(), JournalEntryPostedV1.EventType, reversal.Id.ToString(), JsonSerializer.Serialize(new JournalEntryPostedV1(reversal.Id, reversal.JournalNumber!, reversal.PostingDate!.Value, reversal.Currency), JsonSerializerOptions.Web), now));
            outbox.Append(tx, new(Guid.CreateVersion7(), JournalEntryReversedV1.EventType, original.Id.ToString(), JsonSerializer.Serialize(new JournalEntryReversedV1(original.Id, reversal.Id, reversal.JournalNumber!, reversal.PostingDate!.Value, reversal.Currency), JsonSerializerOptions.Web), now));
            await session.FlushAsync(ct);
            return new CommandReceipt(reversal.Id, reversal.Revision);
        }, token);
    }
}
