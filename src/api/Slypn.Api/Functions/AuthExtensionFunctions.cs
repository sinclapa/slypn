using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

/// <summary>
/// Entra External ID custom authentication extension.
/// Called by CIAM during sign-up (onAttributeCollectionStart) to check whether
/// the registering email has been invited. Non-invited emails are blocked before
/// an Entra account is created.
///
/// The CIAM OAuth bearer can't be validated here: this runs as a SWA-managed
/// Function, and SWA strips/replaces the Authorization header before the request
/// reaches us. Instead the callout is authenticated by a shared secret in the
/// Target URL (<c>?k=</c>) plus the callout body's tenant + extension id. Each
/// check is skipped when its expected value is unconfigured (local dev).
/// </summary>
public sealed class AuthExtensionFunctions(
    IContentRepository repo,
    IOptions<SignupGateOptions> gateOptions,
    ILogger<AuthExtensionFunctions> log)
{
    [Function("AllowSignup")]
    public async Task<HttpResponseData> AllowSignup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/allow-signup")] HttpRequestData req,
        CancellationToken ct)
    {
        var gate = gateOptions.Value;

        // 1. Shared-secret check. Skipped when no secret is configured (local dev).
        if (!string.IsNullOrEmpty(gate.Secret))
        {
            var provided = HttpUtility.ParseQueryString(req.Url.Query)["k"];
            if (!FixedTimeEquals(provided, gate.Secret))
            {
                log.LogWarning("AllowSignup: shared-secret missing or mismatched — blocking");
                return await Block(req, "Unauthorised.");
            }
        }

        // 2. Parse the CIAM event body — identity checks + the email to gate on.
        // For onAttributeCollectionStart the registering email lives under
        // data.authenticationContext.user (mail, or the email identity's
        // issuerAssignedId). Older/other shapes are kept as fallbacks.
        string? email, calloutTenant, calloutExtensionId;
        string body;
        try
        {
            body = await req.ReadAsStringAsync() ?? string.Empty;
            var data = JsonNode.Parse(body)?["data"];
            calloutTenant      = Str(data?["tenantId"]);
            calloutExtensionId = Str(data?["customAuthenticationExtensionId"]);

            var user = data?["authenticationContext"]?["user"];
            email = Str(user?["mail"])
                 ?? FirstIdentityEmail(user?["identities"])
                 ?? Str(data?["userSignUpInfo"]?["attributes"]?["email"]?["value"])
                 ?? FirstIdentityEmail(data?["userSignUpInfo"]?["identities"])
                 ?? Str(data?["userDetails"]?["mail"])
                 ?? Str(data?["attributes"]?["email"]);
            log.LogInformation("AllowSignup: parsed email={Email} tenant={Tenant}", email, calloutTenant);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AllowSignup: failed to parse request body — blocking");
            return await Block(req, "Sign-up unavailable. Please try again later.");
        }

        // 3. Defence in depth: the callout must come from our tenant + extension.
        // Each comparison is skipped when the expected value isn't configured.
        if (!string.IsNullOrEmpty(gate.TenantId) &&
            !string.Equals(calloutTenant, gate.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("AllowSignup: unexpected callout tenant {Tenant} — blocking", calloutTenant);
            return await Block(req, "Unauthorised.");
        }
        if (!string.IsNullOrEmpty(gate.ExtensionId) &&
            !string.Equals(calloutExtensionId, gate.ExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("AllowSignup: unexpected extension id {ExtId} — blocking", calloutExtensionId);
            return await Block(req, "Unauthorised.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            // Log the raw payload (truncated) so an unexpected shape can be
            // pinpointed without another deploy. Tenant/extension already verified.
            log.LogWarning("AllowSignup: email not found in CIAM payload — blocking. payload={Payload}",
                body.Length > 4000 ? body[..4000] : body);
            return await Block(req, "Sign-up unavailable. Please try again later.");
        }

        if (!repo.SupportsWrites)
        {
            // Storage not configured — local dev only, allow through.
            log.LogWarning("AllowSignup: storage not configured — allowing {Email}", email);
            return await Continue(req);
        }

        try
        {
            // Existence is not an invitation. The members table used to hold newsletter
            // subscribers too, so gating on "a row exists" let anyone subscribe and then sign
            // up; subscribers have their own table now, and IsInvited keeps the gate honest
            // regardless of what else learns to write here.
            var member = await repo.GetMemberByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            if (member is { IsInvited: true })
            {
                log.LogInformation("AllowSignup: allowing invited email {Email}", email);
                return await Continue(req);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AllowSignup: member lookup failed for {Email} — blocking", email);
            return await Block(req, "Sign-up unavailable. Please try again later.");
        }

        log.LogInformation("AllowSignup: blocking uninvited email {Email}", email);
        return await Block(req,
            "You haven't been invited to SLYPN yet. Ask a SLYPN admin to send you an invite, then sign up with the same email address.",
            title: "You need an invite");
    }

    /// <summary>Reads a JSON node as a string, returning null for missing/non-string nodes.</summary>
    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Returns the first identity's email-bearing issuerAssignedId, if any.</summary>
    private static string? FirstIdentityEmail(JsonNode? identities)
    {
        if (identities is not JsonArray arr) return null;
        foreach (var id in arr)
        {
            var signInType = Str(id?["signInType"]);
            var value = Str(id?["issuerAssignedId"]);
            if (value is null) continue;
            if (string.Equals(signInType, "emailAddress", StringComparison.OrdinalIgnoreCase) ||
                value.Contains('@'))
                return value;
        }
        return null;
    }

    /// <summary>Constant-time string comparison to avoid leaking the secret via timing.</summary>
    private static bool FixedTimeEquals(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }

    private static async Task<HttpResponseData> Continue(HttpRequestData req)
    {
        var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(CiamJson(new JsonObject
        {
            ["@odata.type"] = "microsoft.graph.attributeCollectionStart.continueWithDefaultBehavior",
        }));
        return resp;
    }

    private static async Task<HttpResponseData> Block(HttpRequestData req, string message, string title = "Sign-up unavailable")
    {
        var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(CiamJson(new JsonObject
        {
            ["@odata.type"] = "microsoft.graph.attributeCollectionStart.showBlockPage",
            ["title"]       = title,
            ["message"]     = message,
        }));
        return resp;
    }

    private static string CiamJson(JsonObject action) =>
        new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["@odata.type"] = "microsoft.graph.onAttributeCollectionStartResponseData",
                ["actions"]     = new JsonArray(action),
            }
        }.ToJsonString();
}
