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

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

        document.Components.SecuritySchemes["oauth2"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{authority}/authorize"),
                    TokenUrl         = new Uri($"{authority}/token"),
                    Scopes           = new Dictionary<string, string> { [scope] = "Access the SLYPN API" }
                }
            },
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                ["x-client-id"] = new OpenApiString(spaClient)
            }
        };

        var oauth2Ref = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
        };

        foreach (var path in document.Paths.Values)
        foreach (var op in path.Operations.Values)
        {
            if (op.Security is null) continue;
            var bearerReq = op.Security.FirstOrDefault(
                r => r.Keys.Any(k => k.Reference?.Id == "bearer_auth"));
            if (bearerReq is null) continue;
            op.Security.Remove(bearerReq);
            op.Security.Add(new OpenApiSecurityRequirement { [oauth2Ref] = [scope] });
        }
    }
}
