using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Slypn.Api.Infrastructure;

public sealed class SwaggerUiOptions(IConfiguration config) : IOpenApiCustomUIOptions
{
    public string CustomStylesheetPath { get; set; } = string.Empty;
    public string CustomJavaScriptPath { get; set; } = "/api/swagger/ui/init-oauth.js";

    public Task<string> GetStylesheetAsync() => Task.FromResult(string.Empty);

    public Task<string> GetJavaScriptAsync()
    {
        var clientId = config["Swagger:SpaClientId"] ?? string.Empty;
        var audience  = config["AzureAd:Audience"] ?? string.Empty;
        var scope     = string.IsNullOrEmpty(audience) ? string.Empty : $"{audience}/access_as_user";

        var js = $$"""
            (function waitForUi() {
              if (!window.ui) { setTimeout(waitForUi, 50); return; }
              window.ui.initOAuth({
                clientId: '{{clientId}}',
                scopes: '{{scope}}',
                usePkceWithAuthorizationCodeGrant: true
              });
            })();
            """;

        return Task.FromResult(js);
    }
}
