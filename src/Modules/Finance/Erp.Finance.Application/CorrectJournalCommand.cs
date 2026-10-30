using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
namespace Erp.Finance.Application;

public sealed class CorrectJournalCommand(IIdempotentCommands idempotency, IJournalSessions sessions, IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, Guid id, long expected, CorrectJournalRequest request, CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.Correct);
        if (request is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 || expected < 1 || identity.Command != "Finance.CorrectManualJournal" || identity.Target != id.ToString())
            throw new FoundationException(Errors.VALIDATION_FAILED);
        if (request.PostingDate is null) throw new FoundationException(ReversalErrors.DateInvalid);
        if (request.Lines is null || request.Lines.Length > 200 || request.Lines.Any(x => x is null || x.Description is null)) throw new FoundationException(Errors.VALIDATION_FAILED);
        var inputs = request.Lines.Select(x => new LineInput(null, x.AccountId, JournalMoney.Parse(x.Debit), JournalMoney.Parse(x.Credit), x.Description)).ToArray();
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var session = sessions.Open(tx); var book = await session.BookAsync(true, ct);
            var original = await session.LoadAsync(id, true, ct); var existing = await session.ReversalAsync(id, ct); var now = clock.GetUtcNow();
            var prior = await session.CorrectionAsync(id, ct);
            if (prior?.OriginalJournalId == id) throw new FoundationException(CorrectionErrors.AlreadyCorrected);
            if (prior is not null || existing is not null || original.Status != JournalStatus.Posted) throw new FoundationException(CorrectionErrors.NotAllowed);
            var replacement = JournalEntry.Create(context.TenantId, context.CompanyId, request.PostingDate, original.Currency, original.Reference, request.Reason, inputs, now);
            var reversal = JournalReversal.CreateOpposite(original, expected, existing?.OriginalJournalId == id, existing?.ReversalJournalId == id, request.PostingDate, request.Reason, now);
            var periods = await session.PeriodsAsync(reversal.PostingDate, true, ct);
            if (periods.Count != 1) throw new FoundationException(ReversalErrors.DateInvalid);
            if (periods[0].Status != FiscalPeriodStatus.Open) throw new FoundationException(CorrectionErrors.PeriodClosed);
            var accounts = await session.AccountsAsync(reversal.Lines.Select(x => x.AccountId).Concat(replacement.Lines.Select(x => x.AccountId)), true, ct);
            var issues = reversal.Validate(book, periods, accounts); if (issues.Count > 0) throw new FoundationException(issues[0].Error);
            var replacementIssues = replacement.Validate(book, periods, accounts);
            if (replacementIssues.Count > 0) throw new FoundationException(replacementIssues[0].Error);
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
            session.Add(replacement); await session.FlushAsync(ct);
            var replacementNumber = await session.AllocateNumberAsync(periods[0].FiscalYear, ct);
            replacement.Post(replacement.Revision, book, periods, accounts, replacementNumber, context.Identity.Issuer, context.Identity.Subject, now);
            var correction = new JournalCorrection
            {
                Id = Guid.CreateVersion7(),
                TenantId = context.TenantId,
                CompanyId = context.CompanyId,
                OriginalJournalId = id,
                ReversalJournalId = reversal.Id,
                ReplacementJournalId = replacement.Id,
                PostingDate = request.PostingDate.Value,
                Reason = request.Reason,
                ActorIssuer = context.Identity.Issuer,
                ActorSubject = context.Identity.Subject,
                RequestId = context.RequestId,
                CorrelationId = context.CorrelationId,
                Timestamp = now
            };
            session.AddCorrection(correction);
            audit.Append(tx, new("JournalPosted", "ManualJournal", replacement.Id.ToString()));
            audit.Append(tx, new("JournalCorrectionRequested", "ManualJournal", id.ToString()));
            audit.Append(tx, new("JournalCorrectionCompleted", "ManualJournal", id.ToString()));
            outbox.Append(tx, new(Guid.CreateVersion7(), JournalEntryPostedV1.EventType, replacement.Id.ToString(), JsonSerializer.Serialize(new JournalEntryPostedV1(replacement.Id, replacement.JournalNumber!, replacement.PostingDate!.Value, replacement.Currency), JsonSerializerOptions.Web), now));
            outbox.Append(tx, new(Guid.CreateVersion7(), JournalCorrectedV1.EventType, id.ToString(), JsonSerializer.Serialize(new JournalCorrectedV1(correction.Id, id, reversal.Id, replacement.Id), JsonSerializerOptions.Web), now));
            await session.FlushAsync(ct);
            return new CommandReceipt(replacement.Id, replacement.Revision);
        }, token);
    }
}
