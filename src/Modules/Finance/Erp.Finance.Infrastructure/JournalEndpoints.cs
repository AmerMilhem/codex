using System.Globalization;
using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Finance.Infrastructure;

public sealed record PostJournalRequest;
public static class JournalEndpoints
{
    private static VerifiedContext Context(HttpContext http) => (VerifiedContext)http.Items[typeof(VerifiedContext)]!;
    private static long Expected(HttpContext http)
    {
        var header = http.Request.Headers.IfMatch.ToString();
        if (header.Length < 3 || header[0] != '"' || header[^1] != '"' || !long.TryParse(header.AsSpan(1, header.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw new FoundationException(Errors.VALIDATION_FAILED);
        return revision;
    }
    private static IResult Receipt(HttpContext http, CommandReceipt receipt)
    { http.Response.Headers.ETag = $"\"{receipt.AppliedRevision}\""; return Results.Ok(receipt); }
    public static IEndpointRouteBuilder MapFinanceJournals(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/companies/{companyId:guid}/finance").RequireAuthorization();
        group.MapGet("/journal-setup", (HttpContext h, IJournalQueries q, CancellationToken ct) => q.SetupAsync(Context(h), ct)).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapGet("/journals", (HttpContext h, IJournalQueries q, int? offset, int? limit, CancellationToken ct) => q.ListAsync(Context(h), offset ?? 0, limit ?? 50, ct)).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapGet("/journals/{id:guid}", async (HttpContext h, Guid id, IJournalQueries q, CancellationToken ct) =>
        { var journal = await q.GetAsync(Context(h), id, ct); h.Response.Headers.ETag = $"\"{journal.Revision}\""; return Results.Ok(journal); }).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapPost("/journals/drafts", async (HttpContext h, SaveDraftRequest request, JournalCommands commands, CancellationToken ct) =>
            Receipt(h, await commands.CreateAsync(Context(h), request, ct))).WithMetadata(new ScopeAction(JournalPermissions.Create));
        group.MapPut("/journals/{id:guid}/draft", async (HttpContext h, Guid id, SaveDraftRequest request, JournalCommands commands, CancellationToken ct) =>
            Receipt(h, await commands.UpdateAsync(Context(h), id, Expected(h), request, ct))).WithMetadata(new ScopeAction(JournalPermissions.Edit));
        group.MapPost("/journals/{id:guid}/validate", (HttpContext h, Guid id, JournalCommands commands, CancellationToken ct) =>
            commands.ValidateAsync(Context(h), id, Expected(h), ct)).WithMetadata(new ScopeAction(JournalPermissions.Edit));
        group.MapPost("/journals/{id:guid}/post", async (HttpContext h, Guid id, PostJournalRequest request, JournalCommands commands, CancellationToken ct) =>
        {
            var expected = Expected(h);
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, h.Request.Headers.IfMatch.ToString(), System.Text.Json.JsonSerializer.Serialize(request));
            var identity = new CommandIdentity("Finance.PostManualJournal", id.ToString(), h.Request.Headers["Idempotency-Key"].ToString(), fingerprint);
            return Receipt(h, await commands.PostAsync(Context(h), id, expected, identity, ct));
        }).WithMetadata(new ScopeAction(JournalPermissions.Post));
        group.MapPost("/journals/{id:guid}/reverse", async (HttpContext h, Guid id, ReverseJournalRequest request, ReverseJournalCommand command, CancellationToken ct) =>
        {
            var expected = Expected(h);
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, h.Request.Headers.IfMatch.ToString(), System.Text.Json.JsonSerializer.Serialize(request));
            return Receipt(h, await command.ExecuteAsync(Context(h), id, expected, request, new("Finance.ReverseManualJournal", id.ToString(), h.Request.Headers["Idempotency-Key"].ToString(), fingerprint), ct));
        }).WithMetadata(new ScopeAction(JournalPermissions.Reverse));
        group.MapPost("/journals/{id:guid}/correct", async (HttpContext h, Guid id, CorrectJournalRequest request, CorrectJournalCommand command, CancellationToken ct) =>
        {
            var expected = Expected(h);
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, h.Request.Headers.IfMatch.ToString(), System.Text.Json.JsonSerializer.Serialize(request));
            return Receipt(h, await command.ExecuteAsync(Context(h), id, expected, request, new("Finance.CorrectManualJournal", id.ToString(), h.Request.Headers["Idempotency-Key"].ToString(), fingerprint), ct));
        }).WithMetadata(new ScopeAction(JournalPermissions.Correct));
        group.MapGet("/journals/{id:guid}/audit", (HttpContext h, Guid id, IJournalQueries q, CancellationToken ct) => q.AuditAsync(Context(h), id, ct)).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapGet("/journal-reconciliation", (HttpContext h, IJournalQueries q, CancellationToken ct) => q.ReconcileAsync(Context(h), ct)).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapGet("/trial-balance", (HttpContext h, ITrialBalanceQuery query, DateOnly? startDate, DateOnly? endDate, CancellationToken ct) =>
            query.RunAsync(Context(h), startDate, endDate, ct)).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapGet("/general-ledger", (HttpContext h, GeneralLedgerQuery query, Guid? accountId, DateOnly? startDate, DateOnly? endDate, int? offset, int? limit, CancellationToken ct) =>
            query.RunAsync(Context(h), accountId, startDate, endDate, offset ?? 0, limit ?? 50, ct)).WithMetadata(new ScopeAction(JournalPermissions.View));
        group.MapGet("/periods", (HttpContext h, FiscalPeriodQueries query, CancellationToken ct) =>
            query.ListAsync(Context(h), ct)).WithMetadata(new ScopeAction(PeriodPermissions.View));
        group.MapGet("/accounts", (HttpContext h, AccountSetupQueries query, CancellationToken ct) =>
            query.ListAsync(Context(h), ct)).WithMetadata(new ScopeAction(AccountPermissions.View));
        group.MapGet("/books", (HttpContext h, AccountingBookSetupQueries query, CancellationToken ct) =>
            query.ListAsync(Context(h), ct)).WithMetadata(new ScopeAction(BookPermissions.View));
        group.MapPost("/books", async (HttpContext h, CreateAccountingBookRequest request, CreateAccountingBookCommand command, CancellationToken ct) =>
        {
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, "0", System.Text.Json.JsonSerializer.Serialize(request));
            return Receipt(h, await command.ExecuteAsync(Context(h), request,
                new("Finance.CreateAccountingBook", "books", h.Request.Headers["Idempotency-Key"].ToString(), fingerprint), ct));
        }).WithMetadata(new ScopeAction(BookPermissions.Create));
        group.MapPost("/accounts", async (HttpContext h, CreateAccountRequest request, CreateAccountCommand command, CancellationToken ct) =>
        {
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, "0", System.Text.Json.JsonSerializer.Serialize(request));
            return Receipt(h, await command.ExecuteAsync(Context(h), request,
                new("Finance.CreateAccount", "accounts", h.Request.Headers["Idempotency-Key"].ToString(), fingerprint), ct));
        }).WithMetadata(new ScopeAction(AccountPermissions.Create));
        group.MapPost("/periods", async (HttpContext h, CreateFiscalPeriodRequest request, CreateFiscalPeriodCommand command, CancellationToken ct) =>
        {
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, "0", System.Text.Json.JsonSerializer.Serialize(request));
            return Receipt(h, await command.ExecuteAsync(Context(h), request,
                new("Finance.CreateFiscalPeriod", "periods", h.Request.Headers["Idempotency-Key"].ToString(), fingerprint), ct));
        }).WithMetadata(new ScopeAction(PeriodPermissions.Create));
        group.MapGet("/periods/{id:guid}/pre-close", (HttpContext h, Guid id, FiscalPeriodQueries query, CancellationToken ct) =>
            query.PreviewAsync(Context(h), id, ct)).WithMetadata(new ScopeAction(PeriodPermissions.Close));
        group.MapPost("/periods/{id:guid}/close", async (HttpContext h, Guid id, ClosePeriodRequest request, CloseFiscalPeriodCommand command, CancellationToken ct) =>
        {
            var expected = Expected(h);
            var fingerprint = RequestFingerprint.Create("POST", h.Request.Path, h.Request.Headers.IfMatch.ToString(), System.Text.Json.JsonSerializer.Serialize(request));
            return Receipt(h, await command.ExecuteAsync(Context(h), id, expected, request,
                new("Finance.CloseFiscalPeriod", id.ToString(), h.Request.Headers["Idempotency-Key"].ToString(), fingerprint), ct));
        }).WithMetadata(new ScopeAction(PeriodPermissions.Close));
        return endpoints;
    }
}
