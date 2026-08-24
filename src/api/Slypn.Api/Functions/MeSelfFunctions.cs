using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class MeSelfFunctions(
    IContentRepository repo,
    ILogger<MeSelfFunctions> log)
{
    [OpenApiOperation(operationId: "me.whoami", tags: new[] { "me" }, Summary = "Who am I", Description = "Returns the validated JWT claims for the current caller. Useful for diagnosing auth issues.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "JWT claims")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Missing or invalid token")]
    [Function("WhoAmI")]
    [RequireRole]
    public static async Task<HttpResponseData> WhoAmI(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whoami")] HttpRequestData req,
        FunctionContext context)
    {
        var principal = context.GetPrincipal();
        var claims = principal?.Claims
            .Select(c => new { type = c.Type, value = c.Value })
            .ToList();
        return await Ok(req, new { claims });
    }


    [OpenApiOperation(operationId: "me.get", tags: new[] { "me" }, Summary = "My profile", Description = "Returns the caller's member profile (roles + status). On first call after sign-up, links the Entra OID to the member record.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Member profile with roles and status")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Missing or invalid token")]
    [Function("GetMe")]
    [RequireRole]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites)
            return await Ok(req, new { roles = Array.Empty<string>(), status = (string?)null });

        var callerOid = context.GetUserOid();
        if (string.IsNullOrEmpty(callerOid))
            return await Ok(req, new { roles = Array.Empty<string>(), status = (string?)null });

        // OID is linked — fast path. Otherwise re-link via email (e.g. stored OID is
        // stale because it was seeded from az-cli, which returns a different OID for
        // personal Microsoft accounts than the JWT contains).
        var member = await repo.GetMemberByOidAsync(callerOid, ct)
            ?? await TryRelinkByEmailAsync(context, callerOid, ct);

        if (member is null)
            return await Ok(req, new { roles = Array.Empty<string>(), status = (string?)null });

        return await Ok(req, new { roles = member.Roles, status = member.Status });
    }

    /// <summary>
    /// Looks up a member by the caller's JWT email/preferred_username and, if its stored
    /// OID doesn't match the validated JWT OID, re-links it to the current caller.
    /// </summary>
    private async Task<Member?> TryRelinkByEmailAsync(FunctionContext context, string callerOid, CancellationToken ct)
    {
        var email = context.GetPrincipal()?.FindFirst("email")?.Value
            ?? context.GetPrincipal()?.FindFirst("preferred_username")?.Value;
        if (string.IsNullOrEmpty(email))
            return null;

        // Only a row an admin actually created may be claimed by a signed-in caller: linking an
        // OID also flips it to "active" below and surfaces it in Member Management as though an
        // admin had invited them. Newsletter subscribers used to land here and be claimable.
        var byEmail = await repo.GetMemberByEmailAsync(email.Trim().ToLowerInvariant(), ct);
        if (byEmail is null || !byEmail.IsInvited || byEmail.Oid == callerOid)
            return null;

        // Use the display name the user chose during CIAM sign-up; fall back
        // to the admin's placeholder only if the JWT carries no name claim.
        var jwtName = context.GetUserName();
        var activated = byEmail with
        {
            Oid         = callerOid,
            AcceptedAt  = byEmail.AcceptedAt ?? DateTime.UtcNow,
            Status      = byEmail.AcceptedAt is null ? "active" : byEmail.Status,
            DisplayName = !string.IsNullOrWhiteSpace(jwtName) ? jwtName : byEmail.DisplayName,
        };
        try
        {
            log.LogInformation("Linked OID {Oid} to member {Email}", callerOid, email);
            return await repo.UpsertMemberAsync(activated, byEmail.Etag, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to link OID {Oid} to member {Email}", callerOid, email);
            return byEmail;
        }
    }
}
