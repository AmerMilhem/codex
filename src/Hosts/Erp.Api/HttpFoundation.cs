using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Api;

public interface IAuthenticatedIdentity { PrincipalIdentity? Get(HttpContext context); }
public sealed class AuthenticatedIdentity : IAuthenticatedIdentity
{
    public PrincipalIdentity? Get(HttpContext context) => context.User.Identity?.IsAuthenticated == true
        && context.User.FindFirstValue("iss") is { } issuer && context.User.FindFirstValue("sub") is { } subject
        ? new(issuer, subject) : null;
}

public sealed class DevelopmentAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, DevelopmentIdentityOptions identity, ISecretProvider secrets,
    IHostEnvironment environment) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!identity.Enabled || !(environment.IsDevelopment() || environment.IsEnvironment("Test")))
            return Task.FromResult(AuthenticateResult.NoResult());
        var expected = secrets.Get("ERP_DEV_AUTH_TOKEN");
        var authorization = Request.Headers.Authorization.ToString();
        if (expected is null || expected.Length < 24 || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]))))
            return Task.FromResult(AuthenticateResult.NoResult());
        var remote = Context.Connection.RemoteIpAddress;
        if (remote is not null && !System.Net.IPAddress.IsLoopback(remote))
            return Task.FromResult(AuthenticateResult.Fail("Development adapter is loopback-only."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "development-adapter"), new Claim("sub", identity.Subject)
        ], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => Problems.WriteAsync(Context, Errors.AUTH_REQUIRED);
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => Problems.WriteAsync(Context, Errors.AUTH_SCOPE_DENIED);
}

public static class Problems
{
    public static Task WriteAsync(HttpContext context, ErrorDefinition error) => Results.Problem(
        statusCode: error.Status, title: "Request could not be completed.", type: "urn:erp:error:" + error.Code,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["errorId"] = error.Id,
            ["requestId"] = context.TraceIdentifier,
            ["correlationId"] = context.Items["correlationId"],
            ["traceId"] = Activity.Current?.TraceId.ToString()
        }).ExecuteAsync(context);
}

public sealed class RequestFoundationMiddleware(RequestDelegate next, ILogger<RequestFoundationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        var supplied = context.Request.Headers["X-Correlation-ID"].ToString();
        var correlation = supplied.Length is > 0 and <= 64 && supplied.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? supplied : Guid.NewGuid().ToString("N");
        context.Items["correlationId"] = correlation;
        context.Response.Headers["X-Correlation-ID"] = correlation;
        context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
        context.Response.Headers["X-Trace-ID"] = Activity.Current?.TraceId.ToString();
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        context.Response.Headers.CacheControl = "no-store";
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        { ["CorrelationId"] = correlation, ["RequestId"] = context.TraceIdentifier, ["TraceId"] = Activity.Current?.TraceId.ToString() });
        try { await next(context); }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var definition = error switch
            {
                FoundationException f => f.Error,
                DbUpdateConcurrencyException => Errors.DOC_CONCURRENCY_CONFLICT,
                System.Text.Json.JsonException or BadHttpRequestException => Errors.VALIDATION_FAILED,
                _ => Errors.SERVICE_UNAVAILABLE
            };
            logger.LogWarning("Request failed with {Code} ({ExceptionType})", definition.Code, error.GetType().Name);
            await Problems.WriteAsync(context, definition);
        }
    }
}

public sealed class CompanyScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAuthenticatedIdentity authentication, ContextResolver resolver)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ScopeAction>() is { } metadata)
        {
            var identity = authentication.Get(context) ?? throw new FoundationException(Errors.AUTH_REQUIRED);
            if (!Guid.TryParse(context.Request.RouteValues["tenantId"]?.ToString(), out var tenant)
                || !Guid.TryParse(context.Request.RouteValues["companyId"]?.ToString(), out var company))
                throw new FoundationException(Errors.TENANT_CONTEXT_INVALID);
            context.Items[typeof(VerifiedContext)] = await resolver.ResolveAsync(identity, tenant, company,
                metadata.Permission, context.Items["correlationId"]!.ToString()!, context.TraceIdentifier,
                ExecutionSource.Api, context.RequestAborted);
        }
        await next(context);
    }
}

// Test and future module delivery adapters contribute routes without leaking persistence into API.
public interface IEndpointContributor { void Map(IEndpointRouteBuilder endpoints); }
