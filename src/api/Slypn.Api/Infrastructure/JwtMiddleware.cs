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
    private const string RolesClaimType = "roles";
    private const string NameClaimType = "name";

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

        var result = await AuthenticateAsync(httpReq, attr, context.CancellationToken);

        if (!result.IsAllowed)
        {
            await ShortCircuit(context, httpReq, result.RefusalCode!.Value, result.RefusalMessage!);
            return;
        }

        context.Items[PrincipalContextKey] = result.Principal!;
        await next(context);
    }

    /// <summary>
    /// Decide whether this caller may run the function, without touching the
    /// FunctionContext or writing anything. Returns the principal to run as, or
    /// the refusal for <see cref="Invoke"/> to render.
    ///
    /// Deliberately free of FunctionContext. Writing a response needs
    /// GetInvocationResult() and reading the request needs
    /// GetHttpRequestDataAsync(); both go through IFunctionBindingsFeature,
    /// which is internal to the Worker SDK and cannot be implemented from a test
    /// assembly. Every refusal path was therefore unreachable in a unit test.
    /// Separating the decision from rendering it makes them all testable.
    /// </summary>
    internal async Task<AuthResult> AuthenticateAsync(
        HttpRequestData httpReq, RequireRoleAttribute attr, CancellationToken ct)
    {
        // Local-dev escape hatch — synthesise a principal for the requested test
        // persona when SkipAuth=true. The role gate is still enforced below so the
        // member persona correctly gets 403 on Admin-only endpoints.
        if (options.Value.SkipAuth)
        {
            logger.LogWarning("AzureAd:SkipAuth=true — bypassing JWT validation. DO NOT use in production.");
            return EnforceRole(attr, DevPrincipal(httpReq));
        }

        if (!validator.IsConfigured)
        {
            return AuthResult.Refuse(HttpStatusCode.ServiceUnavailable,
                "Auth is not configured. Set AzureAd__Authority / AzureAd__Audience or AzureAd__SkipAuth=true.");
        }

        var token = ExtractBearer(httpReq);
        if (token is null)
            return AuthResult.Refuse(HttpStatusCode.Unauthorized, "Missing Bearer token.");

        var (principal, tokenRefusal) = await ValidateTokenAsync(token, ct);
        if (principal is null)
            return tokenRefusal;

        principal = await ApplyTableRolesAsync(principal, ct);

        return EnforceRole(attr, principal);
    }

    private static ClaimsPrincipal DevPrincipal(HttpRequestData httpReq)
    {
        var persona = DevPersonas.Resolve(GetHeader(httpReq, DevPersonas.HeaderName));
        var identity = new ClaimsIdentity("dev", "name", RolesClaimType);
        identity.AddClaim(new Claim("name",  persona.Name));
        identity.AddClaim(new Claim("oid",   persona.Oid));
        identity.AddClaim(new Claim("email", persona.Email));
        foreach (var role in persona.Roles)
            identity.AddClaim(new Claim(RolesClaimType, role));
        return new ClaimsPrincipal(identity);
    }

    private async Task<(ClaimsPrincipal? Principal, AuthResult Refusal)> ValidateTokenAsync(
        string token, CancellationToken ct)
    {
        try
        {
            return (await validator.ValidateAsync(token, ct), default);
        }
        catch (SecurityTokenException ex)
        {
            logger.LogInformation(ex, "JWT validation failed: {Message}", ex.Message);
            return (null, AuthResult.Refuse(HttpStatusCode.Unauthorized, $"Invalid token: {ex.Message}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating JWT");
            return (null, AuthResult.Refuse(HttpStatusCode.Unauthorized, "Token validation error."));
        }
    }

    /// <summary>
    /// Return the caller as the members table says they are. The table is the source of
    /// truth for roles: an admin grants them through PATCH /api/members/{id}, not through
    /// an Entra app-role assignment.
    ///
    /// Replacing the roles rather than adding to them is the whole point. This used to
    /// only ever add, so demoting someone in the table left their old JWT role effective
    /// until the token expired — the table could grant a role but never take one back.
    ///
    /// A caller the table cannot vouch for ends up with no roles at all — whether the row
    /// is missing, the token carries no oid to look one up by, or storage is unconfigured
    /// so there is no table to consult. A valid CIAM token proves who someone is, not that
    /// they belong to SLYPN, and any of these falling back to the token's roles would hand
    /// authority quietly back to Entra app-role assignments.
    ///
    /// Note that the stored OID can go stale: personal Microsoft accounts are issued a
    /// different oid than az-cli reports, so a seeded record can miss here. GET /me
    /// re-links such a record by email, which restores the roles from the next request on.
    ///
    /// The one exception is a lookup that throws — see below.
    /// </summary>
    private async Task<ClaimsPrincipal> ApplyTableRolesAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        IReadOnlyList<string> roles = [];

        if (!repo.SupportsWrites)
        {
            // Storage is configured in every deployed environment, so this means the
            // connection string is missing rather than that roles are irrelevant.
            logger.LogWarning("Members table unavailable (storage not configured) — no caller can hold a role.");
        }
        else if (principal.FindFirst("oid")?.Value is not { Length: > 0 } callerOid)
        {
            logger.LogInformation("Token carries no oid claim — no member record to resolve roles from.");
        }
        else
        {
            try
            {
                var member = await repo.GetMemberByOidAsync(callerOid, ct);
                if (member is null)
                    logger.LogInformation("No member record for OID {Oid} — caller holds no roles.", callerOid);
                roles = member?.Roles ?? [];
            }
            catch (Exception ex)
            {
                // Storage is reachable in principle but failed. Fall back to the token's
                // own roles rather than signing every member out at once over a transient
                // fault — that would turn a blip into a site-wide outage. This is the only
                // path that keeps token roles, and it is deliberately the transient one.
                logger.LogWarning(ex, "Role lookup failed for OID {Oid}; falling back to the token's roles", callerOid);
                return principal;
            }
        }

        // Rebuilt rather than mutated: ClaimsIdentity.RemoveClaim only accepts claims the
        // identity itself owns, and claims arriving from the token handler need not be, so
        // removal in place can throw and leave a stale role attached.
        var identity = new ClaimsIdentity(
            principal.Claims.Where(c => c.Type != RolesClaimType),
            principal.Identity?.AuthenticationType ?? "jwt",
            NameClaimType,
            RolesClaimType);
        foreach (var role in roles)
            identity.AddClaim(new Claim(RolesClaimType, role));

        return new ClaimsPrincipal(identity);
    }

    private static AuthResult EnforceRole(RequireRoleAttribute attr, ClaimsPrincipal principal) =>
        attr.Roles.Length == 0 || attr.Roles.Any(r => principal.IsInRole(r))
            ? AuthResult.Allow(principal)
            : AuthResult.Refuse(HttpStatusCode.Forbidden,
                $"Required role: {string.Join(" or ", attr.Roles)}.");

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

/// <summary>
/// Outcome of an authentication + authorisation decision: either the principal
/// to run as, or the status and message to refuse with. Exactly one is set.
/// </summary>
internal readonly record struct AuthResult(
    ClaimsPrincipal? Principal,
    HttpStatusCode? RefusalCode,
    string? RefusalMessage)
{
    public static AuthResult Allow(ClaimsPrincipal principal) => new(principal, null, null);

    public static AuthResult Refuse(HttpStatusCode code, string message) => new(null, code, message);

    public bool IsAllowed => Principal is not null;
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
