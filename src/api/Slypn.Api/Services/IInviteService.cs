namespace Slypn.Api.Services;

public sealed record InviteResult(bool Sent, string? RedeemUrl, string? Reason);

public interface IInviteService
{
    bool IsConfigured { get; }
    Task<InviteResult> SendInviteAsync(string email, string displayName, CancellationToken ct);
}
