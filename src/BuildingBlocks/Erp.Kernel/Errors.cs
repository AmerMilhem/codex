namespace Erp.Kernel;

public sealed record ErrorDefinition(string Id, string Code, int Status);

// Uppercase registry identifiers retain the approved stable dotted API wire codes.
public static class Errors
{
    public static readonly ErrorDefinition AUTH_SCOPE_DENIED = new("AUTH_SCOPE_DENIED", "auth.scope_denied", 403);
    public static readonly ErrorDefinition TENANT_CONTEXT_INVALID = new("TENANT_CONTEXT_INVALID", "scope.conflicting_context", 400);
    public static readonly ErrorDefinition DOC_CONCURRENCY_CONFLICT = new("DOC_CONCURRENCY_CONFLICT", "concurrency.revision_mismatch", 412);
    public static readonly ErrorDefinition API_IDEMPOTENCY_CONFLICT = new("API_IDEMPOTENCY_CONFLICT", "idempotency.key_reused", 409);
    public static readonly ErrorDefinition VALIDATION_FAILED = new("VALIDATION_FAILED", "request.invalid_shape", 400);
    public static readonly ErrorDefinition RESOURCE_NOT_FOUND = new("RESOURCE_NOT_FOUND", "resource.not_found", 404);
    public static readonly ErrorDefinition AUTH_REQUIRED = new("AUTH_REQUIRED", "auth.required", 401);
    public static readonly ErrorDefinition SERVICE_UNAVAILABLE = new("SERVICE_UNAVAILABLE", "service.temporarily_unavailable", 503);
    public static readonly ErrorDefinition EVENT_PAYLOAD_CONFLICT = new("EVENT_PAYLOAD_CONFLICT", "integration.payload_conflict", 409);
}

public sealed class FoundationException(ErrorDefinition error) : Exception(error.Code)
{
    public ErrorDefinition Error { get; } = error;
}

public readonly record struct Revision(long Value)
{
    public Revision Next() => Value >= 0 ? new(checked(Value + 1)) : throw new FoundationException(Errors.VALIDATION_FAILED);
}
