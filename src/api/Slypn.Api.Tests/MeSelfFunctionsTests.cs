using Azure;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

/// <summary>The caller's own profile: WhoAmI, and OID linking on first sign-in.</summary>
public class MeSelfFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── Me ──────────────────────────────────────────────────────────────────────
    private static MeSelfFunctions MeFn(FakeContentRepository repo) =>
        new(repo, NullLogger<MeSelfFunctions>.Instance);

    [Fact]
    public async Task Me_returns_roles_for_linked_member()
    {
        var repo = new FakeContentRepository { MemberByOid = new Member("m1", "a@b.com", "A", new[] { "Admin" }, "active", DateTime.UtcNow, Oid: "oid-1") };
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Admin", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Me_returns_empty_when_writes_disabled()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Me_returns_empty_when_no_oid_in_context()
    {
        var repo = new FakeContentRepository();
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext(); // no principal → no oid
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.DoesNotContain("Admin", resp.ReadBodyAsString());
    }

    // SEC-1: a row with no roles is not a member, whatever its status says. Subscribers no
    // longer land in this table (SEC-5), but a legacy row that survived the migration must still
    // not be claimable — claiming it links the caller's OID and flips the row to "active", so
    // they would appear in Member Management as though an admin had invited them.
    [Fact]
    public async Task Me_does_not_claim_a_subscriber_row_for_the_caller()
    {
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = new Member("s1", "sub@x.com", "sub@x.com", Array.Empty<string>(), "subscribed", DateTime.UtcNow),
        };
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-attacker", "Mallory").WithEmail("sub@x.com");

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // The re-link must not even be attempted. ThrowOnWrite would be swallowed by the
        // catch inside TryRelinkByEmailAsync, so count the attempt instead.
        Assert.Equal(0, repo.MemberUpserts);

        // Status null, not "subscribed": the caller gets no profile at all rather than the
        // subscriber's row echoed back, which is what distinguishes fixed from broken here.
        var body = resp.ReadBodyAsString();
        Assert.Contains("\"status\":null", body);
        Assert.DoesNotContain("subscribed", body);
    }

    [Fact]
    public async Task Me_still_relinks_an_invited_member_whose_stored_oid_is_stale()
    {
        // The case the re-link exists for: personal Microsoft accounts are issued a
        // different oid than az-cli reports, so a seeded record misses the oid lookup.
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = new Member("m1", "admin@x.com", "Placeholder", new[] { "Admin" }, "invited", DateTime.UtcNow, Oid: "stale-oid"),
        };
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-real", "Ada").WithEmail("admin@x.com");

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Admin", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Me_returns_empty_when_member_not_found_by_oid_or_email()
    {
        var repo = new FakeContentRepository { MemberByOid = null, MemberByEmail = null };
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-unknown", "Ghost", "Member");
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = resp.ReadBodyAsString();
        Assert.Contains("[]", body);
    }

    [Fact]
    public async Task Me_links_oid_when_member_found_by_email_with_different_oid()
    {
        // Member was seeded with a placeholder OID that doesn't match the JWT.
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = new Slypn.Api.Models.Member("m1", "alice@example.com", "Alice",
                new[] { "Member" }, "invited", DateTime.UtcNow, Oid: "old-oid")
        };
        var fn = MeFn(repo);
        // Context has email + different OID → triggers OID linking.
        var identity = new System.Security.Claims.ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid",   "new-oid"));
        identity.AddClaim(new System.Security.Claims.Claim("name",  "Alice"));
        identity.AddClaim(new System.Security.Claims.Claim("email", "alice@example.com"));
        identity.AddClaim(new System.Security.Claims.Claim("roles", "Member"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Member", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Me_falls_back_to_byEmail_when_upsert_throws()
    {
        var member = new Slypn.Api.Models.Member("m1", "b@c.com", "Bob",
            new[] { "Contributor" }, "invited", DateTime.UtcNow, Oid: "old-oid");
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = member,
            ThrowOnWrite  = new Exception("Cosmos unavailable")
        };
        var fn = MeFn(repo);
        var identity = new System.Security.Claims.ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid",   "new-oid"));
        identity.AddClaim(new System.Security.Claims.Claim("email", "b@c.com"));
        identity.AddClaim(new System.Security.Claims.Claim("roles", "Contributor"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // Falls back to the byEmail member's roles.
        Assert.Contains("Contributor", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Me_returns_empty_when_email_not_found_after_oid_miss()
    {
        // OID not in repo, email also not found → no member.
        var repo = new FakeContentRepository { MemberByOid = null, MemberByEmail = null };
        var fn = MeFn(repo);
        var identity = new System.Security.Claims.ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid",   "oid-x"));
        identity.AddClaim(new System.Security.Claims.Claim("email", "nobody@x.com"));
        identity.AddClaim(new System.Security.Claims.Claim("roles", "Member"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("[]", resp.ReadBodyAsString());
    }

    // ── Me / WhoAmI ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task WhoAmI_returns_claims_for_current_principal()
    {
        var ctx = new TestFunctionContext().WithUser("oid-1", "Alice", "Admin");
        var resp = (TestHttpResponseData)await MeSelfFunctions.WhoAmI(TestHttp.Get(ctx, "http://localhost/api/whoami"), ctx);
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var body = resp.ReadBodyAsString();
        Assert.Contains("oid", body);
    }

    [Fact]
    public async Task WhoAmI_returns_empty_claims_when_no_principal()
    {
        var ctx = new TestFunctionContext(); // no principal
        var resp = (TestHttpResponseData)await MeSelfFunctions.WhoAmI(TestHttp.Get(ctx, "http://localhost/api/whoami"), ctx);
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Me: OID linking with preferred_username and null-name branches ────────

    [Fact]
    public async Task Me_links_oid_using_preferred_username_when_no_email_claim()
    {
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = new Slypn.Api.Models.Member("m1", "pref@example.com", "Pref User",
                new[] { "Member" }, "invited", DateTime.UtcNow, Oid: "old-oid")
        };
        var fn = MeFn(repo);
        // Principal with "preferred_username" but no "email" claim
        var identity = new System.Security.Claims.ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid",                "new-oid"));
        identity.AddClaim(new System.Security.Claims.Claim("name",              "Pref User"));
        identity.AddClaim(new System.Security.Claims.Claim("preferred_username", "pref@example.com"));
        identity.AddClaim(new System.Security.Claims.Claim("roles",             "Member"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Member", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Me_links_oid_uses_email_display_name_when_jwt_has_no_name()
    {
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = new Slypn.Api.Models.Member("m1", "user@example.com", "Stored Name",
                new[] { "Member" }, "invited", DateTime.UtcNow, Oid: "old-oid")
        };
        var fn = MeFn(repo);
        // Principal with email but no "name" claim → GetUserName() returns null → falls back to byEmail.DisplayName
        var identity = new System.Security.Claims.ClaimsIdentity("test", "oid", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid",   "new-oid"));
        identity.AddClaim(new System.Security.Claims.Claim("email", "user@example.com"));
        identity.AddClaim(new System.Security.Claims.Claim("roles", "Member"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Me_links_oid_with_accepted_member_keeps_existing_status()
    {
        // AcceptedAt is set → Status stays byEmail.Status, AcceptedAt stays as-is
        var repo = new FakeContentRepository
        {
            MemberByOid   = null,
            MemberByEmail = new Slypn.Api.Models.Member("m1", "acc@example.com", "Alice",
                new[] { "Member" }, "active", DateTime.UtcNow,
                AcceptedAt: DateTime.UtcNow, Oid: "old-oid")
        };
        var fn = MeFn(repo);
        var identity = new System.Security.Claims.ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid",   "new-oid"));
        identity.AddClaim(new System.Security.Claims.Claim("name",  "Alice"));
        identity.AddClaim(new System.Security.Claims.Claim("email", "acc@example.com"));
        identity.AddClaim(new System.Security.Claims.Claim("roles", "Member"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/me"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("active", resp.ReadBodyAsString());
    }
}
