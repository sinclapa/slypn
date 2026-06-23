using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Models;
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

    // ── Newsletters ──────────────────────────────────────────────────────────────
    private static NewslettersFunctions NewslettersFn(FakeContentRepository repo) =>
        new(repo, NullLogger<NewslettersFunctions>.Instance);

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
}
