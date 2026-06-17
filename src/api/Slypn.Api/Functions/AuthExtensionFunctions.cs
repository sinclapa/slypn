using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

/// <summary>
/// Entra External ID custom authentication extension.
/// Called by CIAM during sign-up (onAttributeCollectionStart) to check whether
/// the registering email has been invited. Non-invited emails are blocked before
/// an Entra account is created.
/// </summary>
public sealed class AuthExtensionFunctions(
    IContentRepository repo,
    ICustomExtensionTokenValidator validator,
    ILogger<AuthExtensionFunctions> log)
{
    [Function("AllowSignup")]
    public async Task<HttpResponseData> AllowSignup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/allow-signup")] HttpRequestData req,
        CancellationToken ct)
    {
        // CIAM authenticates extension calls with a bearer token issued for our API app.
        var rawToken = ExtractBearer(req);
        if (rawToken is null || !validator.IsConfigured)
        {
            log.LogWarning("AllowSignup: missing bearer token or validator not configured — blocking");
            return await Block(req, "Unauthorised.");
        }

        try
        {
            await validator.ValidateAsync(rawToken, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "AllowSignup: token validation failed — blocking");
            return await Block(req, "Unauthorised.");
        }

        // Extract the email from the CIAM event body.
        // onAttributeCollectionStart fires before the form is submitted, so
        // data.attributes is empty — the email lives in data.userDetails.mail.
        string? email;
        try
        {
            var body = await req.ReadAsStringAsync() ?? string.Empty;
            var node = JsonNode.Parse(body);
            email = node?["data"]?["userDetails"]?["mail"]?.GetValue<string>()
                 ?? node?["data"]?["attributes"]?["email"]?.GetValue<string>();
            log.LogInformation("AllowSignup: parsed email={Email}", email);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AllowSignup: failed to parse request body — blocking");
            return await Block(req, "Sign-up unavailable. Please try again later.");
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

    private static string? ExtractBearer(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var vals)) return null;
        var raw = vals.FirstOrDefault();
        const string prefix = "Bearer ";
        return raw?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? raw[prefix.Length..].Trim()
            : null;
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
