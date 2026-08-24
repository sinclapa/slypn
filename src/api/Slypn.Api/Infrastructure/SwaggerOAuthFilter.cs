using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;

namespace Slypn.Api.Infrastructure;

public sealed class SwaggerOAuthFilter(IConfiguration config) : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        var authority = (config["AzureAd:Authority"] ?? "").TrimEnd('/');
        var audience  = config["AzureAd:Audience"] ?? "";
        var spaClient = config["Swagger:SpaClientId"] ?? "";
        var scope     = $"{audience}/access_as_user";

        // authority ends with /v2.0; strip it to get the tenant base URL, then
        // build the actual OIDC endpoints which live under /oauth2/v2.0/
        var baseUrl = authority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase)
            ? authority[..^5]
            : authority;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        document.Components.SecuritySchemes["oauth2"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{baseUrl}/oauth2/v2.0/authorize"),
                    TokenUrl         = new Uri($"{baseUrl}/oauth2/v2.0/token"),
                    Scopes           = new Dictionary<string, string> { [scope] = "Access the SLYPN API" }
                }
            },
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                ["x-client-id"] = new OpenApiString(spaClient)
            }
        };

        document.Components.SecuritySchemes.Remove("bearer_auth");

        var oauth2Ref = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
        };

        var operationSecurities = document.Paths.Values
            .SelectMany(path => path.Operations.Values)
            .Select(op => op.Security);

        foreach (var security in operationSecurities)
        {
            if (security is null) continue;
            var bearerReq = security.FirstOrDefault(
                r => r.Keys.Any(k => k.Reference?.Id == "bearer_auth"));
            if (bearerReq is null) continue;
            security.Remove(bearerReq);
            security.Add(new OpenApiSecurityRequirement { [oauth2Ref] = [scope] });
        }
    }
}
