using Microsoft.Extensions.Logging;

namespace Slypn.Api.Services;

/// <summary>
/// Fallback used when Graph isn't configured. The member record is still
/// persisted; this just logs that the email step was skipped.
/// </summary>
public sealed class LoggingInviteService(ILogger<LoggingInviteService> logger) : IInviteService
{
    public bool IsConfigured => false;

    public Task<InviteResult> SendInviteAsync(string email, string displayName, CancellationToken ct)
    {
        logger.LogWarning(
            "InviteRedirectUrl is not configured — member {Email} saved but no sign-up URL available. " +
            "Set Graph__InviteRedirectUrl in configuration.",
            email);
        return Task.FromResult(new InviteResult(Sent: false, RedeemUrl: null, Reason: "sign-up-url-not-configured"));
    }
}
