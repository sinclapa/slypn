using System.Net;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
using Xunit;

namespace Slypn.Api.Tests;

public class SwaggerTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── SwaggerOAuth2RedirectFunction ─────────────────────────────────────────

    [Fact]
    public async Task SwaggerOAuth2Redirect_returns_200_html_page()
    {
        var fn = new SwaggerOAuth2RedirectFunction();
        var ctx = new TestFunctionContext();
        var req = TestHttp.Get(ctx, "http://localhost/api/swagger/oauth2-redirect.html");
        var resp = (TestHttpResponseData)await fn.Run(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("Content-Type", out var ct));
        Assert.Contains("text/html", ct!.First());
        Assert.Contains("no-store", resp.Headers.GetValues("Cache-Control").First());
        var body = resp.ReadBodyAsString();
        Assert.Contains("oauth2", body);
        Assert.Contains("window.opener", body);
    }

    // ── EntraJwtValidator ─────────────────────────────────────────────────────

    [Fact]
    public void EntraJwtValidator_IsConfigured_false_when_empty_options()
    {
        var v = new EntraJwtValidator(Options.Create(new EntraOptions()));
        Assert.False(v.IsConfigured);
    }

    [Fact]
    public void EntraJwtValidator_IsConfigured_true_when_authority_and_audience_set()
    {
        var opts = new EntraOptions { Authority = "https://tenant.ciamlogin.com/tenant/v2.0", Audience = "api://client-id" };
        var v = new EntraJwtValidator(Options.Create(opts));
        Assert.True(v.IsConfigured);
    }

    [Fact]
    public void EntraJwtValidator_IsConfigured_false_missing_audience()
    {
        var v = new EntraJwtValidator(Options.Create(new EntraOptions { Authority = "https://x" }));
        Assert.False(v.IsConfigured);
    }

    // ── SwaggerOAuthFilter ────────────────────────────────────────────────────

    private static SwaggerOAuthFilter MakeFilter(
        string authority = "https://tenant.ciamlogin.com/tenant/v2.0",
        string audience  = "api://client-id",
        string spaClient = "spa-client-id") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:Authority"]      = authority,
            ["AzureAd:Audience"]       = audience,
            ["Swagger:SpaClientId"]    = spaClient,
        }).Build());

    private static OpenApiDocument DocWithBearerOp()
    {
        var bearerScheme = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearer_auth" }
        };
        var doc = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    ["bearer_auth"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer" }
                }
            },
            Paths = new OpenApiPaths
            {
                ["/api/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            Security = new List<OpenApiSecurityRequirement>
                            {
                                new() { [bearerScheme] = new List<string>() }
                            }
                        }
                    }
                }
            }
        };
        return doc;
    }

    [Fact]
    public void SwaggerOAuthFilter_adds_oauth2_scheme_and_removes_bearer_auth()
    {
        var doc = DocWithBearerOp();
        MakeFilter().Apply(null!, doc);

        Assert.True(doc.Components.SecuritySchemes.ContainsKey("oauth2"));
        Assert.False(doc.Components.SecuritySchemes.ContainsKey("bearer_auth"));
    }

    [Fact]
    public void SwaggerOAuthFilter_replaces_bearer_security_on_operations()
    {
        var doc = DocWithBearerOp();
        MakeFilter().Apply(null!, doc);

        var op = doc.Paths["/api/test"].Operations[OperationType.Get];
        Assert.Single(op.Security);
        var scheme = op.Security[0].Keys.First();
        Assert.Equal("oauth2", scheme.Reference?.Id);
    }

    [Fact]
    public void SwaggerOAuthFilter_strips_v2_suffix_from_authority()
    {
        var doc = DocWithBearerOp();
        MakeFilter(authority: "https://tenant.ciamlogin.com/tenant/v2.0").Apply(null!, doc);

        var oauth2 = doc.Components.SecuritySchemes["oauth2"];
        var authUrl = oauth2.Flows.AuthorizationCode.AuthorizationUrl.ToString();
        // base URL should NOT end with /v2.0
        Assert.Contains("oauth2/v2.0/authorize", authUrl);
        Assert.DoesNotContain("v2.0/oauth2", authUrl);
    }

    [Fact]
    public void SwaggerOAuthFilter_handles_authority_without_v2_suffix()
    {
        var doc = DocWithBearerOp();
        MakeFilter(authority: "https://login.microsoftonline.com/tenant").Apply(null!, doc);

        var oauth2 = doc.Components.SecuritySchemes["oauth2"];
        Assert.Contains("oauth2/v2.0/authorize", oauth2.Flows.AuthorizationCode.AuthorizationUrl.ToString());
    }

    [Fact]
    public void SwaggerOAuthFilter_skips_operations_with_no_security()
    {
        var doc = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    ["bearer_auth"] = new OpenApiSecurityScheme()
                }
            },
            Paths = new OpenApiPaths
            {
                ["/api/public"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation { Security = null }
                    }
                }
            }
        };
        MakeFilter().Apply(null!, doc); // Must not throw
        Assert.True(doc.Components.SecuritySchemes.ContainsKey("oauth2"));
    }
}
