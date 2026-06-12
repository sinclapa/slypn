using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class MeSelfFunctions(
    IContentRepository repo,
    ILogger<MeSelfFunctions> log)
{
    /// <summary>
    /// Returns the caller's Cosmos member profile (roles + status). On first call after
    /// sign-up, links the Entra OID to the member record and activates it. Safe to call
    /// repeatedly — idempotent once OID is linked.
    /// </summary>
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

        // OID is linked — fast path.
        var member = await repo.GetMemberByOidAsync(callerOid, ct);

        if (member is null)
        {
            // OID not linked yet, or stored OID is stale (e.g. seeded from az-cli which
            // returns a different OID for personal Microsoft accounts than the JWT contains).
            // Re-link whenever the stored OID doesn't match the validated JWT OID.
            var email = context.GetPrincipal()
                ?.FindFirst("email")?.Value
                ?? context.GetPrincipal()?.FindFirst("preferred_username")?.Value;

            if (!string.IsNullOrEmpty(email))
            {
                var byEmail = await repo.GetMemberByEmailAsync(email.Trim().ToLowerInvariant(), ct);
                if (byEmail is not null && byEmail.Oid != callerOid)
                {
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
                        member = await repo.UpsertMemberAsync(activated, byEmail.Etag, ct);
                        log.LogInformation("Linked OID {Oid} to member {Email}", callerOid, email);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Failed to link OID {Oid} to member {Email}", callerOid, email);
                        member = byEmail;
                    }
                }
            }
        }

        if (member is null)
            return await Ok(req, new { roles = Array.Empty<string>(), status = (string?)null });

        return await Ok(req, new { roles = member.Roles, status = member.Status });
    }
}
