using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Xunit;

namespace Slypn.Api.Tests;

public class AuthExtensionFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AuthExtensionFunctions Make(FakeContentRepository repo, SignupGateOptions? gate = null) =>
        new(repo, Options.Create(gate ?? new SignupGateOptions()), NullLogger<AuthExtensionFunctions>.Instance);

    private static string Body(string? email, string? tenant = null)
    {
        var tenantJson = tenant is null ? "null" : "\"" + tenant + "\"";
        var mailJson = email is null ? "null" : "\"" + email + "\"";
        return "{\"data\":{\"tenantId\":" + tenantJson +
               ",\"authenticationContext\":{\"user\":{\"mail\":" + mailJson + "}}}}";
    }

    private static string Read(TestHttpResponseData r) => r.ReadBodyAsString();

    [Fact]
    public async Task Allows_signup_when_storage_unconfigured()
    {
        var fn = Make(new FakeContentRepository { Writes = false });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("a@b.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Allows_invited_email()
    {
        var repo = new FakeContentRepository { MemberByEmail = new Member("m1", "a@b.com", "A", new[] { "Member" }, "invited", DateTime.UtcNow) };
        var fn = Make(repo);
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("a@b.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Blocks_uninvited_email()
    {
        var fn = Make(new FakeContentRepository()); // writes enabled, no member
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("nobody@x.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Blocks_when_email_missing()
    {
        var fn = Make(new FakeContentRepository { Writes = false });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body(null));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Blocks_when_shared_secret_mismatches()
    {
        var fn = Make(new FakeContentRepository { Writes = false }, new SignupGateOptions { Secret = "expected" });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup?k=wrong", Body("a@b.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Passes_secret_check_with_correct_key()
    {
        var fn = Make(new FakeContentRepository { Writes = false }, new SignupGateOptions { Secret = "expected" });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup?k=expected", Body("a@b.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Blocks_on_tenant_mismatch()
    {
        var fn = Make(new FakeContentRepository { Writes = false }, new SignupGateOptions { TenantId = "expected-tenant" });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("a@b.com", tenant: "other-tenant"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }
}
