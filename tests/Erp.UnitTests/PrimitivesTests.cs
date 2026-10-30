using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure;

namespace Erp.UnitTests;

public sealed class PrimitivesTests
{
    [Fact]
    public void Fingerprint_orders_object_properties_but_binds_original_precondition()
    {
        var first = RequestFingerprint.Create("POST", "/intent", "1", "{\"a\":1,\"b\":2}");
        Assert.Equal(first, RequestFingerprint.Create("POST", "/intent", "1", "{\"b\":2,\"a\":1}"));
        Assert.NotEqual(first, RequestFingerprint.Create("POST", "/intent", "2", "{\"a\":1,\"b\":2}"));
        Assert.NotEqual(first, RequestFingerprint.Create("POST", "/other", "1", "{\"a\":1,\"b\":2}"));
    }
    [Fact]
    public void Duplicate_JSON_members_and_unbounded_bodies_are_rejected()
    {
        Assert.Throws<FoundationException>(() => RequestFingerprint.Create("POST", "/", "1", "{\"a\":1,\"a\":2}"));
        Assert.Throws<FoundationException>(() => RequestFingerprint.Create("POST", "/", "1", new string('x', 65537)));
    }
    [Fact]
    public async Task Current_membership_is_required_even_for_an_authenticated_identity()
    {
        var store = new Membership(); var resolver = new ContextResolver(store); var identity = new PrincipalIdentity("test", "subject");
        var context = await resolver.ResolveAsync(identity, store.Tenant, store.Company, "read", "correlation", "request", ExecutionSource.Test);
        Assert.Equal(store.Tenant, context.TenantId);
        store.Enabled = false;
        var denied = await Assert.ThrowsAsync<FoundationException>(async () =>
            await resolver.ResolveAsync(identity, store.Tenant, store.Company, "read", "correlation", "request", ExecutionSource.Test));
        Assert.Equal(Errors.AUTH_SCOPE_DENIED, denied.Error);
    }
    [Fact]
    public async Task Other_company_or_empty_context_cannot_be_resolved()
    {
        var s = new Membership(); var r = new ContextResolver(s);
        await Assert.ThrowsAsync<FoundationException>(async () => await r.ResolveAsync(new("test", "subject"), s.Tenant, Guid.NewGuid(), "read", "c", "r", ExecutionSource.Test));
        await Assert.ThrowsAsync<FoundationException>(async () => await r.ResolveAsync(new("test", "subject"), Guid.Empty, s.Company, "read", "c", "r", ExecutionSource.Test));
    }
    [Fact]
    public void Audit_snapshot_values_are_masked_by_default()
    {
        var masked = new MaskAllSnapshots().Mask(new Dictionary<string, object?> { ["token"] = "sensitive", ["amount"] = 123 });
        Assert.DoesNotContain("sensitive", masked!); Assert.DoesNotContain("123", masked!); Assert.Contains("[MASKED]", masked!);
    }
    [Fact]
    public void Registry_preserves_wire_codes_and_revision_does_not_wrap()
    {
        Assert.Equal("idempotency.key_reused", Errors.API_IDEMPOTENCY_CONFLICT.Code);
        Assert.Throws<OverflowException>(() => new Revision(long.MaxValue).Next());
        Assert.Throws<FoundationException>(() => new Revision(-1).Next());
    }
    private sealed class Membership : IMembershipStore
    {
        public Guid Tenant { get; } = Guid.NewGuid(); public Guid Company { get; } = Guid.NewGuid(); public bool Enabled = true;
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(Enabled, [new(Tenant, Company, new HashSet<string> { "read" })]));
    }
}
