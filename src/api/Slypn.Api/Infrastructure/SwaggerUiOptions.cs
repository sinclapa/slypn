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
            // SWA replaces the Authorization header with its own HS256 token before the
            // request reaches Functions. Patch fetch early so requests to /api/* carry
            // the MSAL Bearer token in X-Slypn-Token, which SWA leaves untouched.
            (function patchFetch() {
              var orig = window.fetch.bind(window);
              window.fetch = function(url, opts) {
                if (opts && opts.headers) {
                  var h = Object.assign({}, opts.headers);
                  var auth = h['Authorization'] || h['authorization'];
                  if (auth && typeof url === 'string' && url.indexOf('/api/') !== -1
                      && url.indexOf('/api/swagger') === -1) {
                    h['X-Slypn-Token'] = auth;
                    delete h['Authorization'];
                    delete h['authorization'];
                    opts = Object.assign({}, opts, { headers: h });
                  }
                }
                return orig(url, opts);
              };
            })();

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
