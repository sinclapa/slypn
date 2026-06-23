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
    [InlineData("contributor", "Contributor")]
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
        Assert.Equal(3, DevPersonas.All.Count);
        Assert.All(DevPersonas.All, p => Assert.Single(p.Roles));
    }

    [Fact]
    public void RequireRoleAttribute_captures_roles()
    {
        var attr = new RequireRoleAttribute("Admin", "Contributor");
        Assert.Equal(new[] { "Admin", "Contributor" }, attr.Roles);
        Assert.Empty(new RequireRoleAttribute().Roles);
    }
}
