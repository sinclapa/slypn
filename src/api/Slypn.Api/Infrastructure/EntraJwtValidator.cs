using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Slypn.Api.Infrastructure;

public sealed class EntraJwtValidator : IJwtValidator
{
    private readonly EntraOptions _opts;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    // MapInboundClaims=false keeps JWT claim names verbatim (e.g. "roles")
    // so RoleClaimType="roles" resolves correctly. The default true remaps
    // "roles" to the long-form ClaimTypes.Role URI, breaking IsInRole checks.
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public EntraJwtValidator(IOptions<EntraOptions> options)
    {
        _opts = options.Value;
        if (!_opts.IsConfigured) return;

        // CIAM always stamps tokens with iss = https://{tenantId}.ciamlogin.com/...
        // regardless of which branded domain was used for the authorization request.
        // Fetch the JWKS from the GUID-domain so we get the keys that actually sign tokens.
        var metadataBase = _opts.TenantId is not null
            ? $"https://{_opts.TenantId}.ciamlogin.com/{_opts.TenantId}/v2.0"
            : _opts.Authority!.TrimEnd('/');
        var metadataAddress = $"{metadataBase}/.well-known/openid-configuration";
        _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = !metadataAddress.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) });
    }

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct)
    {
        if (_configManager is null)
            throw new InvalidOperationException("EntraJwtValidator is not configured.");

        var config = await _configManager.GetConfigurationAsync(ct);
        try
        {
            return _handler.ValidateToken(token, BuildParams(config), out _);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // CIAM may have rotated signing keys since this instance last fetched the JWKS.
            // Force a refresh and retry once so long-running instances self-heal.
            _configManager.RequestRefresh();
            var fresh = await _configManager.GetConfigurationAsync(ct);
            return _handler.ValidateToken(token, BuildParams(fresh), out _);
        }
    }

    private TokenValidationParameters BuildParams(OpenIdConnectConfiguration config)
    {
        var audience = _opts.Audience!;
        var guidOnly = audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
                           ? audience["api://".Length..] : audience;
        var apiUri   = audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
                           ? audience : $"api://{audience}";

        return new TokenValidationParameters
        {
            ValidateIssuer           = true,
            // Use the issuer from the OIDC discovery doc, not the authority URL.
            // For CIAM the discovery issuer uses the tenant-GUID subdomain
            // (e.g. 3a825f01-....ciamlogin.com) while the configured authority
            // uses the friendly name (slypn.ciamlogin.com) — they differ.
            ValidIssuers             = _opts.ValidIssuers.Length > 0 ? _opts.ValidIssuers : [config.Issuer],
            ValidateAudience         = true,
            ValidAudiences           = [guidOnly, apiUri],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = config.SigningKeys,
            ClockSkew                = TimeSpan.FromMinutes(2),
            RoleClaimType            = "roles",
            NameClaimType            = "name",
        };
    }
}
