using System.Security.Claims;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Validates the bearer token sent by an Entra custom authentication extension
/// to the sign-up gate. Separate from <see cref="IJwtValidator"/> because the
/// extension callout token uses a different issuer/JWKS than user-flow tokens.
/// </summary>
public interface ICustomExtensionTokenValidator
{
    bool IsConfigured { get; }

    /// <summary>Validates the token; returns the principal on success, throws on failure.</summary>
    Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct);
}
