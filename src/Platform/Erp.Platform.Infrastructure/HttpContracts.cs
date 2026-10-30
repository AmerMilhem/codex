namespace Erp.Platform.Infrastructure;

// Delivery metadata shared by host middleware and module-owned HTTP adapters.
public sealed record ScopeAction(string Permission);
