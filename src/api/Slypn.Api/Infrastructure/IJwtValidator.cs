using System.Security.Claims;

namespace Slypn.Api.Infrastructure;

public interface IJwtValidator
{
    bool IsConfigured { get; }

    /// <summary>
    /// Validates a raw bearer token against the configured Entra authority + audience.
    /// Returns the principal on success; throws on failure (caller maps to 401).
    /// </summary>
    Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct);
}
