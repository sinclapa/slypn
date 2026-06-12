using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

/// <summary>
/// Returns the app's CIAM sign-up URL for the admin to share with the invitee.
/// No email is sent — the invitee navigates to the URL and creates their account
/// through the Entra External ID password sign-up flow.
/// </summary>
public sealed class CiamInviteService(
    IOptions<GraphOptions> options,
    ILogger<CiamInviteService> logger) : IInviteService
{
    private readonly string _signUpUrl = options.Value.InviteRedirectUrl;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_signUpUrl);

    public Task<InviteResult> SendInviteAsync(string email, string displayName, CancellationToken ct)
    {
        logger.LogInformation("Invite recorded for {Email} — sign-up URL: {Url}", email, _signUpUrl);
        return Task.FromResult(new InviteResult(Sent: true, RedeemUrl: _signUpUrl, Reason: null));
    }
}
