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

        var metadataAddress = $"{_opts.Authority!.TrimEnd('/')}/.well-known/openid-configuration";
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

        // CIAM v2 tokens set `aud` to the bare application GUID, not the
        // api:// URI. Accept both so either form works in configuration.
        var audience  = _opts.Audience!;
        var guidOnly  = audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
                            ? audience["api://".Length..]
                            : audience;
        var apiUri    = audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
                            ? audience
                            : $"api://{audience}";

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuers             = _opts.ValidIssuers.Length > 0 ? _opts.ValidIssuers : [ _opts.Authority!.TrimEnd('/') ],
            ValidateAudience         = true,
            ValidAudiences           = [ guidOnly, apiUri ],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys        = config.SigningKeys,
            ClockSkew                = TimeSpan.FromMinutes(2),
            RoleClaimType            = "roles",
            NameClaimType            = "name",
        };

        var principal = _handler.ValidateToken(token, validationParameters, out _);
        return principal;
    }
}
