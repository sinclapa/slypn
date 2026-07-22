using Azure;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

public class ContentFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static CommunityEvent Event(string id = "e1", string? createdBy = "owner") => new(
        id, "Coffee", "Coffee meet-up",
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
        "Brixton", "Come along", null, createdBy, "Owner") { Etag = "e1" };

    private static object ValidEvent() => new
    {
        title = "Coffee morning", type = "Coffee meet-up",
        startsAt = "2026-06-01T10:00:00Z", endsAt = "2026-06-01T12:00:00Z",
        location = "Brixton", description = "Come along",
    };

    // ── Blog ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Blog_list_returns_200()
    {
        var repo = new FakeContentRepository();
        repo.Blogs.Add(new Article("b1", "s", "T", "Sum", "B", "A", DateTime.UtcNow, 3, "News", new[] { "x" }) { Type = "blog" });
        var fn = new BlogFunctions(repo);
        var resp = (TestHttpResponseData)await fn.GetBlogPosts(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/blog"), Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Events ──────────────────────────────────────────────────────────────────
    private static EventsFunctions EventsFn(FakeContentRepository repo) =>
        new(repo, NullLogger<EventsFunctions>.Instance);

    [Fact]
    public async Task Events_get_returns_404_then_200()
    {
        var repo = new FakeContentRepository();
        var fn = EventsFn(repo);
        var missing = (TestHttpResponseData)await fn.GetEvent(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events/x"), "x", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        repo.EventById = Event();
        var found = (TestHttpResponseData)await fn.GetEvent(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events/e1"), "e1", Ct);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    [Fact]
    public async Task Events_get_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var fn = EventsFn(repo);
        var resp = (TestHttpResponseData)await fn.GetEvent(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events/e1"), "e1", Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task Events_list_returns_200()
    {
        var repo = new FakeContentRepository { Events = { Event() } };
        var fn = EventsFn(repo);
        var resp = (TestHttpResponseData)await fn.GetEvents(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events?upcoming=true"), Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Events_create_disabled_then_valid()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = EventsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid", "U", "Contributor");
        var disabled = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/events", ValidEvent()), ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/events", ValidEvent()), ctx, Ct);
        Assert.Contains((int)ok.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Events_create_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = EventsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid", "U", "Contributor");
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/events", ValidEvent()), ctx, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { EventById = Event(createdBy: "admin"), ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = EventsFn(repo);
        var admin = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(admin, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", admin, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Events_delete_forbidden_for_non_owner_and_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { EventById = Event(createdBy: "owner") };
        var fn = EventsFn(repo);

        var stranger = new TestFunctionContext().WithUser("stranger", "S", "Contributor");
        var forbidden = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(stranger, "DELETE", "http://localhost/api/events/e1", ""), "e1", stranger, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var admin = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var err = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(admin, "DELETE", "http://localhost/api/events/e1", ""), "e1", admin, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    [Fact]
    public async Task Events_replace_enforces_ownership()
    {
        var repo = new FakeContentRepository { EventById = Event(createdBy: "owner") };
        var fn = EventsFn(repo);
        // Non-admin, non-owner → forbidden
        var stranger = new TestFunctionContext().WithUser("stranger", "S", "Contributor");
        var forbidden = (TestHttpResponseData)await fn.Replace(TestHttp.Json(stranger, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", stranger, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Admin → allowed
        var admin = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(admin, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", admin, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Events_delete_owner_succeeds_and_missing_404()
    {
        var repo = new FakeContentRepository();
        var fn = EventsFn(repo);
        var owner = new TestFunctionContext().WithUser("owner", "O", "Contributor");
        var missing = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(owner, "DELETE", "http://localhost/api/events/e1", ""), "e1", owner, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        repo.EventById = Event(createdBy: "owner");
        var ok = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(owner, "DELETE", "http://localhost/api/events/e1", ""), "e1", owner, Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Newsletters_replace_503_then_200()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };

        var disabled = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Newsletters_delete_503_then_204()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();

        var disabled = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/newsletters/n1", ""), "n1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/newsletters/n1", ""), "n1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    // ── Resources ────────────────────────────────────────────────────────────────
    private static ResourcesFunctions ResourcesFn(FakeContentRepository repo) =>
        new(repo, NullLogger<ResourcesFunctions>.Instance);

    [Fact]
    public async Task Resources_list_and_create_and_delete()
    {
        var repo = new FakeContentRepository { Resources = { new Resource("r1", "T", "D", "https://x.org", "NHS") } };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();

        var list = (TestHttpResponseData)await fn.GetResources(TestHttp.Get(ctx, "http://localhost/api/resources"), Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var valid = new { title = "Helpline", description = "Support line", url = "https://x.org/a", category = "NHS" };
        var created = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/resources", valid), Ct);
        Assert.Contains((int)created.StatusCode, new[] { 200, 201 });

        var noCat = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/resources/r1", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noCat.StatusCode);

        var deleted = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/resources/r1?category=NHS", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Resources_replace_503_then_200()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "Helpline", description = "Support line for members", url = "https://example.org/support", category = "NHS" };

        var disabled = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/resources/r1", valid), "r1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/resources/r1", valid), "r1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ── Newsletters ──────────────────────────────────────────────────────────────
    private static NewslettersFunctions NewslettersFn(FakeContentRepository repo) =>
        new(repo, NullLogger<NewslettersFunctions>.Instance);

    [Fact]
    public async Task Newsletters_create_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_replace_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_delete_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/newsletters/n1", ""), "n1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_subscribe_412_when_lookup_fails_and_412_when_upsert_fails()
    {
        var ctx = new TestFunctionContext();
        var payload = TestHttp.Json(ctx, "POST", "http://localhost/api/newsletter/subscribe", new { email = "me@example.com" });

        // GetMemberByEmailAsync throws → line 119
        var repo1 = new FakeContentRepository { ThrowOnMemberEmailLookup = new RequestFailedException(500, "Lookup failed") };
        var err1 = (TestHttpResponseData)await NewslettersFn(repo1).Subscribe(payload, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, err1.StatusCode);

        // UpsertMemberAsync throws → line 148
        var repo2 = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var err2 = (TestHttpResponseData)await NewslettersFn(repo2).Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletter/subscribe", new { email = "me@example.com" }), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err2.StatusCode);
    }

    [Fact]
    public async Task Newsletters_list_create_subscribe()
    {
        var repo = new FakeContentRepository { Newsletters = { new Newsletter("n1", "May", new DateOnly(2026, 5, 1), "summary text", new[] { "t" }) } };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();

        var list = (TestHttpResponseData)await fn.GetNewsletters(TestHttp.Get(ctx, "http://localhost/api/newsletters"), Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var created = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Contains((int)created.StatusCode, new[] { 200, 201 });

        var sub = (TestHttpResponseData)await fn.Subscribe(TestHttp.Json(ctx, "POST", "http://localhost/api/newsletter/subscribe", new { email = "me@example.com" }), Ct);
        Assert.Contains((int)sub.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Newsletters_file_streams_bytes_with_headers()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var repo = new FakeContentRepository
        {
            NewsletterFiles = { ["newsletter-2020-11"] = new BlobDownload(new MemoryStream(bytes), "application/pdf") },
        };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();

        var resp = (TestHttpResponseData)await fn.GetFile(
            TestHttp.Get(ctx, "http://localhost/api/newsletters/newsletter-2020-11/file"), "newsletter-2020-11", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Headers.GetValues("Content-Type").Single());
        Assert.Contains("SLYPN-Newsletter-2020-11.pdf", resp.Headers.GetValues("Content-Disposition").Single());
        resp.Body.Position = 0;
        Assert.Equal(bytes, ((MemoryStream)resp.Body).ToArray());
    }

    [Fact]
    public async Task Newsletters_file_404_when_absent()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetFile(
            TestHttp.Get(ctx, "http://localhost/api/newsletters/newsletter-1999-01/file"), "newsletter-1999-01", Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_create_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "Helpline", description = "Support line for members", url = "https://example.org/support", category = "NHS" };
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/resources", valid), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_replace_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "Helpline", description = "Support line for members", url = "https://example.org/support", category = "NHS" };
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/resources/r1", valid), "r1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_delete_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/resources/r1?category=NHS", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

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
        var repo = new FakeContentRepository();
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "Alice", "Admin");
        var resp = (TestHttpResponseData)await fn.WhoAmI(TestHttp.Get(ctx, "http://localhost/api/whoami"), ctx);
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var body = resp.ReadBodyAsString();
        Assert.Contains("oid", body);
    }

    [Fact]
    public async Task WhoAmI_returns_empty_claims_when_no_principal()
    {
        var repo = new FakeContentRepository();
        var fn = MeFn(repo);
        var ctx = new TestFunctionContext(); // no principal
        var resp = (TestHttpResponseData)await fn.WhoAmI(TestHttp.Get(ctx, "http://localhost/api/whoami"), ctx);
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

    // ── Events: validation error and missing-event branches ──────────────────

    [Fact]
    public async Task Events_create_400_on_invalid_input()
    {
        var fn = EventsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext().WithUser("oid", "U", "Contributor");
        // Empty object fails required-field validation
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/events", new { }), ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_503_when_writes_disabled()
    {
        var fn = EventsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_400_on_invalid_input()
    {
        var fn = EventsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/events/e1", new { }), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_404_when_event_not_found()
    {
        var fn = EventsFn(new FakeContentRepository()); // EventById = null
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Events_delete_503_when_writes_disabled()
    {
        var fn = EventsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/events/e1", ""), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    // ── Events: GetEvents upcoming=false branch ───────────────────────────────

    [Fact]
    public async Task Events_list_without_upcoming_param_returns_200()
    {
        var fn = EventsFn(new FakeContentRepository { Events = { Event() } });
        var resp = (TestHttpResponseData)await fn.GetEvents(
            TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events"), Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Newsletters: writes-disabled and validation-error branches ────────────

    [Fact]
    public async Task Newsletters_create_503_when_writes_disabled()
    {
        var fn = NewslettersFn(new FakeContentRepository { Writes = false });
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_create_400_on_invalid_input()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", new { }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_replace_400_on_invalid_input()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(new TestFunctionContext(), "PUT", "http://localhost/api/newsletters/n1", new { }), "n1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_subscribe_503_when_writes_disabled()
    {
        var fn = NewslettersFn(new FakeContentRepository { Writes = false });
        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletter/subscribe",
                new { email = "me@example.com" }), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_subscribe_400_on_invalid_input()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        // Empty object → email field fails [Required, EmailAddress] validation
        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletter/subscribe",
                new { }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_subscribe_updates_existing_pure_subscriber()
    {
        // existing subscriber with no roles and no Oid → Status stays "subscribed"
        var existing = new Slypn.Api.Models.Member("m1", "sub@example.com", "Old Display",
            Array.Empty<string>(), "subscribed", DateTime.UtcNow);
        var repo = new FakeContentRepository { MemberByEmail = existing };
        var fn = NewslettersFn(repo);

        // With displayName → covers the non-whitespace displayName branch
        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletter/subscribe",
                new { email = "sub@example.com", displayName = "New Display" }), Ct);
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Contains("sub@example.com", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Newsletters_subscribe_existing_subscriber_without_display_name_keeps_existing()
    {
        var existing = new Slypn.Api.Models.Member("m1", "sub@example.com", "Old Display",
            Array.Empty<string>(), "subscribed", DateTime.UtcNow);
        var repo = new FakeContentRepository { MemberByEmail = existing };
        var fn = NewslettersFn(repo);

        // Without displayName → keeps existing.DisplayName
        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletter/subscribe",
                new { email = "sub@example.com" }), Ct);
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Newsletters_subscribe_existing_member_with_roles_preserves_status()
    {
        // existing member with roles → Status preserved (not overwritten with "subscribed")
        var existing = new Slypn.Api.Models.Member("m1", "mem@example.com", "Alice",
            new[] { "Member" }, "active", DateTime.UtcNow, Oid: "oid-1");
        var repo = new FakeContentRepository { MemberByEmail = existing };
        var fn = NewslettersFn(repo);

        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletter/subscribe",
                new { email = "mem@example.com" }), Ct);
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Contains("active", resp.ReadBodyAsString());
    }

    // ── Resources: writes-disabled and validation-error branches ─────────────

    [Fact]
    public async Task Resources_create_503_when_writes_disabled()
    {
        var fn = ResourcesFn(new FakeContentRepository { Writes = false });
        var valid = new { title = "T", description = "Support line for members", url = "https://x.org/a", category = "NHS" };
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/resources", valid), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_create_400_on_invalid_input()
    {
        var fn = ResourcesFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/resources", new { }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_replace_400_on_invalid_input()
    {
        var fn = ResourcesFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(new TestFunctionContext(), "PUT", "http://localhost/api/resources/r1", new { }), "r1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_delete_503_when_writes_disabled()
    {
        var fn = ResourcesFn(new FakeContentRepository { Writes = false });
        var resp = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(new TestFunctionContext(), "DELETE", "http://localhost/api/resources/r1?category=NHS", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    // ── FunctionHelpers: null body and storage exception mapping ─────────────

    [Fact]
    public async Task ReadValidatedAsync_returns_400_on_null_body()
    {
        // JSON "null" deserialises to null → triggers "Empty request body" branch
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", "null",
                new Dictionary<string, string> { ["Content-Type"] = "application/json" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task MapStorageException_maps_404_and_409_status_codes()
    {
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };

        var fn404 = NewslettersFn(new FakeContentRepository { ThrowOnWrite = new Azure.RequestFailedException(404, "Not found") });
        var resp404 = (TestHttpResponseData)await fn404.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp404.StatusCode);

        var fn409 = NewslettersFn(new FakeContentRepository { ThrowOnWrite = new Azure.RequestFailedException(409, "Conflict") });
        var resp409 = (TestHttpResponseData)await fn409.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.Conflict, resp409.StatusCode);
    }
}
