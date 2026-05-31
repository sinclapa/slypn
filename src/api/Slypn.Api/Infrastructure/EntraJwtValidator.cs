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
    private readonly JwtSecurityTokenHandler _handler = new();

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

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuers             = _opts.ValidIssuers.Length > 0 ? _opts.ValidIssuers : [ _opts.Authority!.TrimEnd('/') ],
            ValidateAudience         = true,
            ValidAudience            = _opts.Audience,
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
