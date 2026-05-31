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
            "Graph is not configured — recording the invite for {Email} but no email was sent. " +
            "Set Graph__TenantId/ClientId/ClientSecret (see docs/auth-setup.md) to enable.",
            email);
        return Task.FromResult(new InviteResult(Sent: false, RedeemUrl: null, Reason: "graph-not-configured"));
    }
}
