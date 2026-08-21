using Slypn.Api.Infrastructure;
using Xunit;

namespace Slypn.Api.Tests;

public class InfrastructureTests
{
    [Fact]
    public void EntraOptions_IsConfigured_requires_authority_and_audience()
    {
        Assert.False(new EntraOptions().IsConfigured);
        Assert.False(new EntraOptions { Authority = "https://x" }.IsConfigured);
        Assert.True(new EntraOptions { Authority = "https://x", Audience = "api://y" }.IsConfigured);
    }

    [Fact]
    public void GraphOptions_flags()
    {
        var opts = new GraphOptions();
        Assert.True(opts.IsConfigured); // default InviteRedirectUrl is set
        Assert.False(opts.HasClientCredentials);
        opts.ClientSecret = "secret";
        Assert.True(opts.HasClientCredentials);
    }

    [Fact]
    public void OtelOptions_IsConfigured_requires_endpoint()
    {
        Assert.False(new OtelOptions().IsConfigured);
        Assert.True(new OtelOptions { Endpoint = "https://otlp" }.IsConfigured);
        Assert.Equal("slypn-api", new OtelOptions().ServiceName);
    }

    [Fact]
    public void SignupGateOptions_holds_expected_values()
    {
        var opts = new SignupGateOptions { Secret = "s", TenantId = "t", ExtensionId = "e" };
        Assert.Equal("s", opts.Secret);
        Assert.Equal("t", opts.TenantId);
        Assert.Equal("e", opts.ExtensionId);
    }

    [Theory]
    [InlineData("admin", "Admin")]
    [InlineData("admin2", "Admin")]
    [InlineData("contributor", "Contributor")]
    [InlineData("contributor2", "Contributor")]
    [InlineData("member", "Member")]
    [InlineData("ADMIN", "Admin")]
    public void DevPersonas_resolves_known_keys(string key, string expectedRole)
    {
        var persona = DevPersonas.Resolve(key);
        Assert.Contains(expectedRole, persona.Roles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public void DevPersonas_falls_back_to_admin(string? key)
    {
        Assert.Equal("admin", DevPersonas.Resolve(key).Key);
    }

    [Fact]
    public void DevPersonas_all_have_one_role_each()
    {
        Assert.Equal(5, DevPersonas.All.Count);
        Assert.All(DevPersonas.All, p => Assert.Single(p.Roles));
    }

    [Fact]
    public void RequireRoleAttribute_captures_roles()
    {
        var attr = new RequireRoleAttribute("Admin", "Contributor");
        Assert.Equal(new[] { "Admin", "Contributor" }, attr.Roles);
        Assert.Empty(new RequireRoleAttribute().Roles);
    }

    // ── SEC-4: the auth bypass must not be reachable outside local dev ───────
    // AzureAd:SkipAuth short-circuits JWT validation before every other check and
    // hands Admin to any caller sending X-Slypn-Dev-User. Nothing but a correctly
    // set app setting used to stand between that and a live site.

    /// <summary>A fake environment for the guard to read, so tests never touch the real one.</summary>
    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    private static readonly Func<string, string?> NoAzureMarkers = Env();

    [Theory]
    [InlineData("dev")]
    [InlineData("local")]
    [InlineData("development")]
    [InlineData("DEV")]
    [InlineData(null)]   // Otel__Env omitted — OtelOptions.Env itself defaults to "dev"
    [InlineData("")]
    public void SkipAuth_is_allowed_on_a_local_machine(string? env)
    {
        StartupGuards.EnsureSkipAuthIsLocalOnly(skipAuth: true, otelEnv: env, NoAzureMarkers);
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("production")]
    [InlineData("staging")]
    public void SkipAuth_refuses_to_start_outside_a_local_environment(string env)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.EnsureSkipAuthIsLocalOnly(skipAuth: true, otelEnv: env, NoAzureMarkers));

        Assert.Contains("AzureAd:SkipAuth", ex.Message);
        Assert.Contains(env, ex.Message);
    }

    [Fact]
    public void WEBSITE_HOSTNAME_is_not_an_azure_marker()
    {
        // Regression guard, and the reason this test exists at all: Core Tools sets
        // WEBSITE_HOSTNAME locally to emulate App Service, so treating it as a deployment
        // marker refused to start every `func start` and took the whole e2e job with it.
        // Do not add it back.
        StartupGuards.EnsureSkipAuthIsLocalOnly(
            skipAuth: true, otelEnv: "dev", Env(("WEBSITE_HOSTNAME", "localhost:7071")));
    }

    [Theory]
    [InlineData("WEBSITE_INSTANCE_ID")]
    [InlineData("WEBSITE_SITE_NAME")]
    public void SkipAuth_refuses_to_start_on_an_azure_host_even_when_env_says_dev(string marker)
    {
        // The PR-preview case, and the reason an environment-name check is not enough on
        // its own: previews are real, publicly reachable deployments that deliberately
        // run with Otel__Env=dev.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.EnsureSkipAuthIsLocalOnly(
                skipAuth: true, otelEnv: "dev", Env((marker, "some-value"))));

        Assert.Contains(marker, ex.Message);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("prod")]
    public void SkipAuth_false_never_blocks_startup(string env)
    {
        StartupGuards.EnsureSkipAuthIsLocalOnly(
            skipAuth: false, otelEnv: env, Env(("WEBSITE_SITE_NAME", "swa-slypn-prod")));
    }

    [Fact]
    public void A_blank_azure_marker_is_not_treated_as_a_deployment()
    {
        // Absent and present-but-empty must behave the same, or a stray empty variable
        // would break every local dev machine that happened to have one set.
        StartupGuards.EnsureSkipAuthIsLocalOnly(
            skipAuth: true, otelEnv: "dev", Env(("WEBSITE_SITE_NAME", "   ")));
    }
}
