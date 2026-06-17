using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Validates the bearer token that an Entra custom authentication extension
/// (OnAttributeCollectionStart) sends to <c>/api/auth/allow-signup</c>.
///
/// This token is NOT a user-flow token — it's an app token minted via client
/// credentials by Microsoft's first-party custom-extension caller
/// (<see cref="ExtensionCallerAppId"/>) for our API app. Its signing key lives
/// in the tenant's standard Entra JWKS (login.microsoftonline.com), which is a
/// different key set from the ciamlogin.com user-flow JWKS that
/// <see cref="EntraJwtValidator"/> uses — hence a separate validator. We union
/// the signing keys from both metadata documents so validation succeeds
/// regardless of which endpoint signed the token.
/// </summary>
public sealed class CustomExtensionTokenValidator : ICustomExtensionTokenValidator
{
    /// <summary>Microsoft first-party "custom authentication extension" caller appId (azp).</summary>
    private const string ExtensionCallerAppId = "99045fe1-7639-4a75-9d4a-577b6ca3810f";

    private readonly EntraOptions _opts;
    private readonly ILogger<CustomExtensionTokenValidator> _log;
    private readonly List<ConfigurationManager<OpenIdConnectConfiguration>> _configManagers = [];
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public CustomExtensionTokenValidator(IOptions<EntraOptions> options, ILogger<CustomExtensionTokenValidator> log)
    {
        _opts = options.Value;
        _log  = log;
        if (string.IsNullOrWhiteSpace(_opts.TenantId) || string.IsNullOrWhiteSpace(_opts.Audience))
            return;

        // App tokens for the extension are signed by the standard Entra endpoint;
        // include the ciamlogin authority too so we cover both key sources.
        foreach (var metadata in MetadataAddresses())
            _configManagers.Add(new ConfigurationManager<OpenIdConnectConfiguration>(
                metadata,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever()));

        IsConfigured = _configManagers.Count > 0;
    }

    public bool IsConfigured { get; }

    public async Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("CustomExtensionTokenValidator is not configured.");

        ClaimsPrincipal principal;
        try
        {
            principal = await ValidateAgainstAllKeysAsync(token, refresh: false, ct);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Keys may have rotated since we last fetched the JWKS — refresh and retry once.
            principal = await ValidateAgainstAllKeysAsync(token, refresh: true, ct);
        }
        catch (Exception ex)
        {
            LogTokenMetadata(token, ex);
            throw;
        }

        // Defence in depth: the token must come from Microsoft's custom-extension caller,
        // not just any app in the tenant that can mint a token for our audience.
        var azp = principal.FindFirst("azp")?.Value ?? principal.FindFirst("appid")?.Value;
        if (!string.Equals(azp, ExtensionCallerAppId, StringComparison.OrdinalIgnoreCase))
            throw new SecurityTokenInvalidIssuerException(
                $"Unexpected azp '{azp}' — expected the Entra custom-extension caller.");

        return principal;
    }

    private async Task<ClaimsPrincipal> ValidateAgainstAllKeysAsync(string token, bool refresh, CancellationToken ct)
    {
        var keys      = new List<SecurityKey>();
        var issuers   = new List<string>(_opts.ValidIssuers);
        foreach (var mgr in _configManagers)
        {
            if (refresh) mgr.RequestRefresh();
            var config = await mgr.GetConfigurationAsync(ct);
            keys.AddRange(config.SigningKeys);
            if (!string.IsNullOrEmpty(config.Issuer)) issuers.Add(config.Issuer);
        }

        return _handler.ValidateToken(token, BuildParams(keys, issuers), out _);
    }

    private TokenValidationParameters BuildParams(IEnumerable<SecurityKey> keys, IEnumerable<string> issuers)
    {
        var audience = _opts.Audience!;
        var guidOnly = audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
                           ? audience["api://".Length..] : audience;
        var apiUri   = audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
                           ? audience : $"api://{audience}";

        return new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuers             = issuers.Distinct().ToArray(),
            ValidateAudience         = true,
            ValidAudiences           = [guidOnly, apiUri],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = keys,
            ClockSkew                = TimeSpan.FromMinutes(2),
        };
    }

    private IEnumerable<string> MetadataAddresses()
    {
        yield return $"https://login.microsoftonline.com/{_opts.TenantId}/v2.0/.well-known/openid-configuration";
        if (!string.IsNullOrWhiteSpace(_opts.Authority))
            yield return $"{_opts.Authority!.TrimEnd('/')}/.well-known/openid-configuration";
    }

    /// <summary>Logs non-secret token metadata (kid/iss/aud/azp) to pinpoint a validation failure.</summary>
    private void LogTokenMetadata(string token, Exception ex)
    {
        try
        {
            var jwt = _handler.ReadJwtToken(token);
            _log.LogWarning(
                "AllowSignup token rejected ({Error}). kid={Kid} iss={Iss} aud={Aud} azp={Azp}",
                ex.GetType().Name,
                jwt.Header.Kid,
                jwt.Issuer,
                string.Join(",", jwt.Audiences),
                jwt.Claims.FirstOrDefault(c => c.Type is "azp" or "appid")?.Value);
        }
        catch
        {
            // Unparseable token — nothing useful to log.
        }
    }
}
