using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

/// <summary>
/// Calls the Microsoft Graph API to manage Entra External ID user accounts.
/// Requires Graph__ClientSecret with User.ReadWrite.All application permission.
/// </summary>
public sealed class EntraUserService(
    IOptions<GraphOptions> options,
    IOptions<EntraOptions> entraOptions,
    IHttpClientFactory httpFactory,
    ILogger<EntraUserService> logger) : IEntraUserService
{
    private readonly GraphOptions _opts  = options.Value;
    private readonly EntraOptions _entra = entraOptions.Value;

    public bool IsConfigured => _opts.HasClientCredentials;

    public async Task DeleteUserAsync(string oid, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogWarning(
                "Entra deletion skipped for OID {Oid} — Graph__ClientSecret not configured.",
                oid);
            return;
        }

        var http  = httpFactory.CreateClient();
        var token = await AcquireTokenAsync(http, ct);
        if (token is null) return;

        using var req = new HttpRequestMessage(
            HttpMethod.Delete,
            $"https://graph.microsoft.com/v1.0/users/{oid}");
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);

        if (resp.StatusCode == HttpStatusCode.NoContent)
        {
            logger.LogInformation("Deleted Entra account OID {Oid}", oid);
        }
        else if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            // Never completed sign-up or already deleted — not an error.
            logger.LogInformation("Entra account OID {Oid} not found — skipping.", oid);
        }
        else
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Entra deletion failed for OID {Oid}: {Status} — {Body}",
                oid, (int)resp.StatusCode, body);
        }
    }

    private async Task<string?> AcquireTokenAsync(HttpClient http, CancellationToken ct)
    {
        var tenantId = string.IsNullOrWhiteSpace(_opts.TenantId) ? _entra.TenantId : _opts.TenantId;
        var clientId = string.IsNullOrWhiteSpace(_opts.ClientId)  ? _entra.Audience : _opts.ClientId;

        var form = new FormUrlEncodedContent(new[]
        {
            KeyValuePair.Create("client_id",     clientId!),
            KeyValuePair.Create("client_secret", _opts.ClientSecret!),
            KeyValuePair.Create("scope",         "https://graph.microsoft.com/.default"),
            KeyValuePair.Create("grant_type",    "client_credentials"),
        });

        using var resp = await http.PostAsync(
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
            form, ct);

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Graph token acquisition failed: {Status} {Body}",
                (int)resp.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }
}
