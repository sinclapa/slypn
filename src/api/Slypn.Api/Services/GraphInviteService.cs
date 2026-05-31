using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

/// <summary>
/// Calls the Microsoft Graph invitations endpoint with client-credentials
/// auth. Pure HttpClient — avoids dragging in the full Microsoft.Graph SDK.
/// </summary>
public sealed class GraphInviteService(
    IOptions<GraphOptions> options,
    IHttpClientFactory httpFactory,
    ILogger<GraphInviteService> logger) : IInviteService
{
    private readonly GraphOptions _opts = options.Value;

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<InviteResult> SendInviteAsync(string email, string displayName, CancellationToken ct)
    {
        if (!IsConfigured)
            return new InviteResult(false, null, "graph-not-configured");

        var http = httpFactory.CreateClient();

        var token = await AcquireTokenAsync(http, ct);
        if (token is null)
            return new InviteResult(false, null, "token-acquisition-failed");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/invitations")
        {
            Content = JsonContent.Create(new
            {
                invitedUserEmailAddress = email,
                invitedUserDisplayName  = displayName,
                inviteRedirectUrl       = _opts.InviteRedirectUrl,
                sendInvitationMessage   = true,
            }),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Graph invitation failed: {Status} {Body}", (int)resp.StatusCode, body);
            return new InviteResult(false, null, $"graph-{(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var redeemUrl = doc.RootElement.TryGetProperty("inviteRedeemUrl", out var u) ? u.GetString() : null;
        return new InviteResult(true, redeemUrl, null);
    }

    private async Task<string?> AcquireTokenAsync(HttpClient http, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            KeyValuePair.Create("client_id",     _opts.ClientId!),
            KeyValuePair.Create("client_secret", _opts.ClientSecret!),
            KeyValuePair.Create("scope",         "https://graph.microsoft.com/.default"),
            KeyValuePair.Create("grant_type",    "client_credentials"),
        });
        var tokenUrl = $"https://login.microsoftonline.com/{_opts.TenantId}/oauth2/v2.0/token";
        using var resp = await http.PostAsync(tokenUrl, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Graph token acquisition failed: {Status} {Body}", (int)resp.StatusCode, body);
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }
}
