using Slypn.Api.Infrastructure;
using Xunit;

namespace Slypn.Api.Tests;

public class FunctionContextExtensionsTests
{
    [Fact]
    public void Returns_null_when_no_principal()
    {
        var ctx = new TestFunctionContext();
        Assert.Null(ctx.GetPrincipal());
        Assert.Null(ctx.GetUserOid());
        Assert.Null(ctx.GetUserName());
        Assert.False(ctx.IsAdmin());
    }

    [Fact]
    public void Reads_oid_name_and_admin_role_from_principal()
    {
        var ctx = new TestFunctionContext().WithUser("oid-9", "Grace", "Admin");
        Assert.Equal("oid-9", ctx.GetUserOid());
        Assert.Equal("Grace", ctx.GetUserName());
        Assert.True(ctx.IsAdmin());
    }

    [Fact]
    public void IsAdmin_false_for_non_admin_role()
    {
        var ctx = new TestFunctionContext().WithUser("oid-1", "Member User", "Member");
        Assert.False(ctx.IsAdmin());
        Assert.Equal("Member User", ctx.GetUserName());
    }
}
