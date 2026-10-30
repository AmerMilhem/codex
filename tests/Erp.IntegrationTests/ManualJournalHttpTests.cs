using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class ManualJournalHttpTests(DatabaseFixture db)
{
    private static readonly string Base = $"/api/v1/tenants/{DatabaseFixture.TenantA}/companies/{DatabaseFixture.CompanyA}/finance";
    private static ApiFactory Factory(DatabaseFixture db)
    { var factory = new ApiFactory(db); factory.Members.Permissions.UnionWith([JournalPermissions.View, JournalPermissions.Create, JournalPermissions.Edit, JournalPermissions.Post]); return factory; }
    private static HttpClient Client(ApiFactory factory)
    { var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token); return client; }
    private static HttpRequestMessage Command(string path, long revision, string? key = null, string content = "{}")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Base + path) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{revision}\""); if (key is not null) request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-ID", "manual-journal-http-golden"); return request;
    }
    [Fact]
    public async Task Http_golden_create_edit_validate_post_read_audit_trace_replay_reconcile()
    {
        var s = await FinanceScenario.Create(db); await using var factory = Factory(db); using var client = Client(factory);
        var created = await client.PostAsJsonAsync(Base + "/journals/drafts", s.Request()); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<CommandReceipt>())!; Assert.Equal("\"1\"", created.Headers.ETag!.ToString());
        var edit = new HttpRequestMessage(HttpMethod.Put, Base + $"/journals/{receipt.Reference}/draft") { Content = JsonContent.Create(s.Request() with { Reference = "HTTP golden" }) }; edit.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(edit)).StatusCode);
        var validation = await client.SendAsync(Command($"/journals/{receipt.Reference}/validate", 2)); Assert.True((await validation.Content.ReadFromJsonAsync<JournalValidation>())!.Valid);
        var post = await client.SendAsync(Command($"/journals/{receipt.Reference}/post", 2, "http-golden")); Assert.Equal(HttpStatusCode.OK, post.StatusCode); Assert.Equal("\"3\"", post.Headers.ETag!.ToString());
        var trace = post.Headers.GetValues("X-Trace-ID").Single();
        var replay = await client.SendAsync(Command($"/journals/{receipt.Reference}/post", 2, "http-golden")); Assert.Equal(await post.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var journal = (await client.GetFromJsonAsync<JournalView>(Base + $"/journals/{receipt.Reference}"))!; Assert.Equal("Posted", journal.Status); Assert.Equal("HTTP golden", journal.Reference);
        var audit = (await client.GetFromJsonAsync<JournalAuditView[]>(Base + $"/journals/{receipt.Reference}/audit"))!;
        Assert.Contains(audit, x => x.Action == "JournalPosted" && x.CorrelationId == "manual-journal-http-golden");
        var traceParent = await db.Scalar<string>("SELECT trace_parent FROM integration.outbox WHERE source_id=@id", ("id", receipt.Reference.ToString())); Assert.Contains(trace, traceParent);
        Assert.Contains(factory.Exporter.Spans, x => x.TraceId.ToString() == trace && x.OperationName == "database.unit_of_work");
        Assert.True((await client.GetFromJsonAsync<JournalReconciliation>(Base + "/journal-reconciliation"))!.Passed); await s.AssertOnePosting(receipt.Reference);
    }
    [Fact]
    public async Task Http_post_permission_is_independent_revoked_permissions_block_idempotent_replay()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(); await using var factory = Factory(db); using var client = Client(factory);
        factory.Members.Permissions.Remove(JournalPermissions.Post);
        var denied = await client.SendAsync(Command($"/journals/{draft.Reference}/post", 1, "permission")); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("AUTH_SCOPE_DENIED", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorId").GetString());
        factory.Members.Permissions.Add(JournalPermissions.Post); Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command($"/journals/{draft.Reference}/post", 1, "permission"))).StatusCode);
        factory.Members.Permissions.Remove(JournalPermissions.Post); Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Command($"/journals/{draft.Reference}/post", 1, "permission"))).StatusCode); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Http_stable_errors_preconditions_idempotency_and_scope_are_enforced()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(); await using var factory = Factory(db); using var client = Client(factory);
        await db.ClosePeriod(s.Period, s.Tenant, s.Company);
        var closed = await client.SendAsync(Command($"/journals/{draft.Reference}/post", 1, "closed")); Assert.Equal(HttpStatusCode.UnprocessableEntity, closed.StatusCode);
        var problem = await closed.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("FIN_PERIOD_CLOSED", problem.GetProperty("errorId").GetString()); Assert.True(problem.TryGetProperty("requestId", out _));
        var unknownBody = await client.SendAsync(Command($"/journals/{draft.Reference}/post", 1, "shape", "{\"status\":\"Posted\"}")); Assert.Equal(HttpStatusCode.BadRequest, unknownBody.StatusCode);
        var stale = await client.SendAsync(Command($"/journals/{draft.Reference}/post", 999, "stale")); Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Base + $"/journals/{draft.Reference}/post", new { })).StatusCode);
        var foreignPath = Base.Replace(DatabaseFixture.CompanyA.ToString(), DatabaseFixture.CompanyA2.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(foreignPath + "/journals")).StatusCode);
        foreignPath = Base.Replace(DatabaseFixture.TenantA.ToString(), DatabaseFixture.TenantB.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(foreignPath + "/journals")).StatusCode);
    }
}
