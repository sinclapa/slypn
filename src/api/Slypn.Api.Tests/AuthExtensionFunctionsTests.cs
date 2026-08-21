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

    [Fact]
    public async Task Blocks_on_extension_id_mismatch()
    {
        var fn = Make(new FakeContentRepository { Writes = false }, new SignupGateOptions { ExtensionId = "correct-ext-id" });
        var body = "{\"data\":{\"tenantId\":null,\"customAuthenticationExtensionId\":\"wrong-ext-id\"," +
                   "\"authenticationContext\":{\"user\":{\"mail\":\"a@b.com\"}}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Parses_email_from_identity_array_with_emailAddress_signInType()
    {
        // Tests FirstIdentityEmail: email comes via identities array rather than user.mail
        var repo = new FakeContentRepository { MemberByEmail = new Member("m1", "identity@example.com", "A", new[] { "Member" }, "invited", DateTime.UtcNow) };
        var fn = Make(repo);
        var body = "{\"data\":{\"tenantId\":null,\"authenticationContext\":{\"user\":{" +
                   "\"identities\":[{\"signInType\":\"emailAddress\",\"issuerAssignedId\":\"identity@example.com\"}]}}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Blocks_when_member_lookup_throws()
    {
        var repo = new FakeContentRepository { ThrowOnMemberEmailLookup = new Exception("Cosmos unavailable") };
        var fn = Make(repo);
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("a@b.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Blocks_on_malformed_json_body()
    {
        var fn = Make(new FakeContentRepository { Writes = false });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", "{ this is not valid json !");
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Blocks_when_all_identities_are_non_email_and_have_no_at_symbol()
    {
        // Identity with non-emailAddress signInType and no '@' in the value — FirstIdentityEmail returns null.
        var fn = Make(new FakeContentRepository { Writes = false });
        var body = "{\"data\":{\"tenantId\":null,\"authenticationContext\":{\"user\":{" +
                   "\"identities\":[{\"signInType\":\"federated\",\"issuerAssignedId\":\"opaqueId99\"}]}}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Blocks_when_no_k_param_provided_but_secret_is_configured()
    {
        // gate.Secret is set but the URL has no ?k= → provided is null → FixedTimeEquals returns false
        var fn = Make(new FakeContentRepository { Writes = false }, new SignupGateOptions { Secret = "expected" });
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", Body("a@b.com"));
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    [Fact]
    public async Task Allows_email_parsed_from_userSignUpInfo_attributes()
    {
        // No authenticationContext.user.mail → falls through to userSignUpInfo.attributes.email
        var repo = new FakeContentRepository { MemberByEmail = new Member("m1", "attr@example.com", "A", new[] { "Member" }, "invited", DateTime.UtcNow) };
        var fn = Make(repo);
        var body = "{\"data\":{\"tenantId\":null," +
                   "\"userSignUpInfo\":{\"attributes\":{\"email\":{\"value\":\"attr@example.com\"}}}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Allows_email_parsed_from_userDetails_mail()
    {
        // No authenticationContext or userSignUpInfo → falls through to userDetails.mail
        var repo = new FakeContentRepository { MemberByEmail = new Member("m1", "detail@example.com", "A", new[] { "Member" }, "invited", DateTime.UtcNow) };
        var fn = Make(repo);
        var body = "{\"data\":{\"tenantId\":null," +
                   "\"userDetails\":{\"mail\":\"detail@example.com\"}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Allows_email_parsed_from_attributes_email()
    {
        // Falls through to the last fallback: data.attributes.email
        var repo = new FakeContentRepository { MemberByEmail = new Member("m1", "attrs@example.com", "A", new[] { "Member" }, "invited", DateTime.UtcNow) };
        var fn = Make(repo);
        var body = "{\"data\":{\"tenantId\":null," +
                   "\"attributes\":{\"email\":\"attrs@example.com\"}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Allows_identity_email_with_at_symbol_in_issuer_assigned_id()
    {
        // issuerAssignedId contains '@' but signInType is not emailAddress — still treated as email
        var repo = new FakeContentRepository { MemberByEmail = new Member("m1", "at@example.com", "A", new[] { "Member" }, "invited", DateTime.UtcNow) };
        var fn = Make(repo);
        var body = "{\"data\":{\"tenantId\":null,\"authenticationContext\":{\"user\":{" +
                   "\"identities\":[{\"signInType\":\"federated\",\"issuerAssignedId\":\"at@example.com\"}]}}}}";
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", body);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task Blocks_and_truncates_long_payload_when_email_not_found()
    {
        // body longer than 4000 chars → log truncates to body[..4000]
        var fn = Make(new FakeContentRepository());
        var longPayload = "{\"data\":{\"tenantId\":null,\"authenticationContext\":{\"user\":{\"mail\":null," +
                          "\"identities\":[{\"signInType\":\"federated\",\"issuerAssignedId\":\"no-email-here\"}]," +
                          "\"padding\":\"" + new string('x', 4100) + "\"}}}}";;
        var req = TestHttp.Raw(new TestFunctionContext(), "POST",
            "http://localhost/api/auth/allow-signup", longPayload);
        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);
        Assert.Contains("showBlockPage", Read(resp));
    }

    // ── SEC-1: subscribing is not an invitation ─────────────────────────────
    // The members table doubles as the newsletter subscriber list, and the gate
    // used to allow any address with a row. Anyone could POST to the anonymous
    // /api/newsletter/subscribe and then sign up through CIAM.

    private static Member Subscriber(string email) =>
        new("s1", email, email, Array.Empty<string>(), "subscribed", DateTime.UtcNow);

    private static Member Invited(string email) =>
        new("m9", email, "Invited Person", new[] { "Member" }, "invited", DateTime.UtcNow);

    [Fact]
    public async Task Blocks_a_newsletter_subscriber_who_was_never_invited()
    {
        var repo = new FakeContentRepository { MemberByEmail = Subscriber("subscriber@x.com") };
        var fn = Make(repo);
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("subscriber@x.com"));

        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);

        Assert.Contains("showBlockPage", Read(resp));
        Assert.DoesNotContain("continueWithDefaultBehavior", Read(resp));
    }

    [Fact]
    public async Task A_subscriber_and_an_unknown_address_get_the_identical_block_page()
    {
        // No oracle: if the subscriber saw a different message, the gate would confirm
        // which addresses hold a row in the members table to anyone who asked.
        var subscriberResp = (TestHttpResponseData)await Make(
                new FakeContentRepository { MemberByEmail = Subscriber("subscriber@x.com") })
            .AllowSignup(
                TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("subscriber@x.com")),
                Ct);

        var unknownResp = (TestHttpResponseData)await Make(new FakeContentRepository())
            .AllowSignup(
                TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("nobody@x.com")),
                Ct);

        Assert.Equal(Read(unknownResp), Read(subscriberResp));
    }

    [Fact]
    public async Task Still_allows_an_invited_address_that_also_subscribed()
    {
        // Subscribe preserves the roles of an existing member, so an invitee who also
        // signed up for the newsletter keeps their role and must still get through.
        var repo = new FakeContentRepository { MemberByEmail = Invited("both@x.com") };
        var fn = Make(repo);
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/auth/allow-signup", Body("both@x.com"));

        var resp = (TestHttpResponseData)await fn.AllowSignup(req, Ct);

        Assert.Contains("continueWithDefaultBehavior", Read(resp));
    }
}
