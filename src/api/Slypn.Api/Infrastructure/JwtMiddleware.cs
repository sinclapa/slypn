using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Slypn.Api.Services;

namespace Slypn.Api.Infrastructure;

public sealed class JwtMiddleware(
    IJwtValidator validator,
    IOptions<EntraOptions> options,
    IContentRepository repo,
    ILogger<JwtMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<string, RequireRoleAttribute?> AttrCache = new();

    public const string PrincipalContextKey = "Slypn.Principal";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var attr = GetRoleAttribute(context);

        // No auth requirement on this Function — proceed unauthenticated.
        if (attr is null)
        {
            await next(context);
            return;
        }

        // Need HTTP context to short-circuit with a 401/403.
        var httpReq = await context.GetHttpRequestDataAsync();
        if (httpReq is null)
        {
            // Non-HTTP trigger with [RequireRole] is a misconfiguration; refuse loudly.
            logger.LogError("[RequireRole] is only supported on HTTP-triggered functions.");
            throw new InvalidOperationException("[RequireRole] is only supported on HTTP-triggered functions.");
        }

        // Local-dev escape hatch — synthesise a principal for the requested test
        // persona when SkipAuth=true. The role gate is still enforced below so the
        // member persona correctly gets 403 on Admin-only endpoints.
        if (options.Value.SkipAuth)
        {
            logger.LogWarning("AzureAd:SkipAuth=true — bypassing JWT validation. DO NOT use in production.");
            var persona = DevPersonas.Resolve(GetHeader(httpReq, DevPersonas.HeaderName));
            var identity = new ClaimsIdentity("dev", "name", "roles");
            identity.AddClaim(new Claim("name",  persona.Name));
            identity.AddClaim(new Claim("oid",   persona.Oid));
            identity.AddClaim(new Claim("email", persona.Email));
            foreach (var role in persona.Roles)
                identity.AddClaim(new Claim("roles", role));
            var devPrincipal = new ClaimsPrincipal(identity);

            if (attr.Roles.Length > 0 && !attr.Roles.Any(r => devPrincipal.IsInRole(r)))
            {
                await ShortCircuit(context, httpReq, HttpStatusCode.Forbidden,
                    $"Required role: {string.Join(" or ", attr.Roles)}.");
                return;
            }

            context.Items[PrincipalContextKey] = devPrincipal;
            await next(context);
            return;
        }

        if (!validator.IsConfigured)
        {
            await ShortCircuit(context, httpReq, HttpStatusCode.ServiceUnavailable,
                "Auth is not configured. Set AzureAd__Authority / AzureAd__Audience or AzureAd__SkipAuth=true.");
            return;
        }

        var token = ExtractBearer(httpReq);
        if (token is null)
        {
            await ShortCircuit(context, httpReq, HttpStatusCode.Unauthorized, "Missing Bearer token.");
            return;
        }

        ClaimsPrincipal principal;
        try
        {
            principal = await validator.ValidateAsync(token, context.CancellationToken);
        }
        catch (SecurityTokenException ex)
        {
            logger.LogInformation(ex, "JWT validation failed: {Message}", ex.Message);
            await ShortCircuit(context, httpReq, HttpStatusCode.Unauthorized, $"Invalid token: {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating JWT");
            await ShortCircuit(context, httpReq, HttpStatusCode.Unauthorized, "Token validation error.");
            return;
        }

        // Enrich principal with Cosmos roles (source of truth — overrides any JWT roles).
        if (repo.SupportsWrites && principal.FindFirst("oid")?.Value is { Length: > 0 } callerOid)
        {
            try
            {
                var member = await repo.GetMemberByOidAsync(callerOid, context.CancellationToken);
                if (member?.Roles is { Count: > 0 } cosmosRoles
                    && principal.Identity is ClaimsIdentity identity)
                {
                    foreach (var role in cosmosRoles)
                        if (!identity.HasClaim("roles", role))
                            identity.AddClaim(new Claim("roles", role));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cosmos role enrichment failed for OID {Oid}", callerOid);
            }
        }

        if (attr.Roles.Length > 0 && !attr.Roles.Any(r => principal.IsInRole(r)))
        {
            await ShortCircuit(context, httpReq, HttpStatusCode.Forbidden,
                $"Required role: {string.Join(" or ", attr.Roles)}.");
            return;
        }

        context.Items[PrincipalContextKey] = principal;
        await next(context);
    }

    private static RequireRoleAttribute? GetRoleAttribute(FunctionContext context)
    {
        return AttrCache.GetOrAdd(context.FunctionDefinition.EntryPoint, entryPoint =>
        {
            // EntryPoint is "Namespace.Type.Method".
            var lastDot = entryPoint.LastIndexOf('.');
            if (lastDot < 0) return null;
            var typeName   = entryPoint[..lastDot];
            var methodName = entryPoint[(lastDot + 1)..];

            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeGetTypes)
                .FirstOrDefault(t => t.FullName == typeName);
            if (type is null) return null;

            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == methodName);
            return method?.GetCustomAttribute<RequireRoleAttribute>();
        });
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static string? ExtractBearer(HttpRequestData req)
    {
        // SWA's gateway replaces the Authorization header with its own HS256
        // session token before requests reach the Functions. Read the MSAL token
        // from X-Slypn-Token (set by the frontend) which SWA leaves untouched.
        foreach (var headerName in new[] { "X-Slypn-Token", "Authorization" })
        {
            if (!req.Headers.TryGetValues(headerName, out var vals)) continue;
            var raw = vals.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            const string prefix = "Bearer ";
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var token = raw[prefix.Length..].Trim();
            // SWA's own HS256 session tokens carry no kid — skip them so they
            // never reach the CIAM validator and produce a confusing IDX10517.
            if (!HasKeyId(token)) continue;
            return token;
        }
        return null;
    }

    private static bool HasKeyId(string token)
    {
        try { return !string.IsNullOrEmpty(new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid); }
        catch { return false; }
    }

    private static string? GetHeader(HttpRequestData req, string name) =>
        req.Headers.TryGetValues(name, out var vals) ? vals.FirstOrDefault() : null;

    private static async Task ShortCircuit(FunctionContext context, HttpRequestData req, HttpStatusCode code, string message)
    {
        var resp = req.CreateResponse(code);
        await resp.WriteStringAsync(message);
        context.GetInvocationResult().Value = resp;
    }
}

public static class FunctionContextExtensions
{
    public static ClaimsPrincipal? GetPrincipal(this FunctionContext context) =>
        context.Items.TryGetValue(JwtMiddleware.PrincipalContextKey, out var v) ? v as ClaimsPrincipal : null;

    public static string? GetUserOid(this FunctionContext context) =>
        context.GetPrincipal()?.FindFirst("oid")?.Value;

    public static string? GetUserName(this FunctionContext context) =>
        context.GetPrincipal()?.FindFirst("name")?.Value
        ?? context.GetPrincipal()?.FindFirst("preferred_username")?.Value;

    public static bool IsAdmin(this FunctionContext context) =>
        context.GetPrincipal()?.FindAll("roles").Any(c => c.Value == "Admin") ?? false;
}
