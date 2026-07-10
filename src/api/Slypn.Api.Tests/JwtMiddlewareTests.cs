using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

file sealed class StubJwtValidator : IJwtValidator
{
    public StubJwtValidator(bool isConfigured) => IsConfigured = isConfigured;
    public bool IsConfigured { get; }
    public Task<System.Security.Claims.ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct)
        => throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("invalid");
}

public class JwtMiddlewareTests
{
    private static JwtMiddleware Make(bool skipAuth = false, bool validatorConfigured = true) =>
        new(
            new StubJwtValidator(validatorConfigured),
            Options.Create(new EntraOptions { SkipAuth = skipAuth }),
            new FakeContentRepository(),
            NullLogger<JwtMiddleware>.Instance);

    // BlogFunctions.GetBlogPosts has no [RequireRole] → middleware must call next immediately.
    [Fact]
    public async Task Passthrough_when_function_has_no_RequireRole()
    {
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.BlogFunctions.GetBlogPosts");
        var called = false;

        await Make().Invoke(ctx, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    // Unknown entry point → GetRoleAttribute returns null → next called.
    [Fact]
    public async Task Passthrough_when_entry_point_not_found()
    {
        var ctx = new TestMiddlewareContext("Does.Not.Exist");
        var called = false;

        await Make().Invoke(ctx, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    // ArticlesFunctions.Create has [RequireRole] but Features returns no IFunctionBindingsFeature,
    // so GetHttpRequestDataAsync returns null → middleware must throw InvalidOperationException.
    [Fact]
    public async Task Throws_when_RequireRole_on_non_http_context()
    {
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.ArticlesFunctions.Create");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Make().Invoke(ctx, _ => Task.CompletedTask));
    }

    // [RequireRole("Admin")] present, SkipAuth=true, no HTTP context → still throws before SkipAuth check.
    [Fact]
    public async Task SkipAuth_still_throws_without_http_context()
    {
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.ArticlesFunctions.Publish");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Make(skipAuth: true).Invoke(ctx, _ => Task.CompletedTask));
    }

    [Fact]
    public void PrincipalContextKey_is_stable()
    {
        Assert.Equal("Slypn.Principal", JwtMiddleware.PrincipalContextKey);
    }
}
