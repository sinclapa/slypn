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
        // onAttributeCollectionStart fires before the form is submitted, so
        // data.attributes is empty — the email lives in data.userDetails.mail.
        string? email, calloutTenant, calloutExtensionId;
        try
        {
            var body = await req.ReadAsStringAsync() ?? string.Empty;
            var data = JsonNode.Parse(body)?["data"];
            calloutTenant      = data?["tenantId"]?.GetValue<string>();
            calloutExtensionId = data?["customAuthenticationExtensionId"]?.GetValue<string>();
            email = data?["userDetails"]?["mail"]?.GetValue<string>()
                 ?? data?["attributes"]?["email"]?.GetValue<string>();
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
            log.LogWarning("AllowSignup: email not found in CIAM payload — blocking");
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
            var member = await repo.GetMemberByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            if (member is not null)
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
