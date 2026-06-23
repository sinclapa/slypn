using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

public class AdminFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── Members ──────────────────────────────────────────────────────────────
    private static MembersFunctions MembersFn(FakeContentRepository repo) =>
        new(repo, new FakeInviteService(), new FakeEntraUserService(), NullLogger<MembersFunctions>.Instance);

    private static Member Member(string id = "m1", string? oid = "oidA") =>
        new(id, "a@b.com", "Alice", new[] { "Member" }, "active", DateTime.UtcNow, Oid: oid) { Etag = "e1" };

    [Fact]
    public async Task Members_list_disabled_then_ok()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = MembersFn(repo);
        var disabled = (TestHttpResponseData)await fn.List(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/members"), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        repo.Members.Add(Member());
        var ok = (TestHttpResponseData)await fn.List(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/members"), Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Members_invite_rejects_bad_role_and_accepts_valid()
    {
        var repo = new FakeContentRepository();
        var fn = MembersFn(repo);
        var ctx = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");

        var bad = (TestHttpResponseData)await fn.Invite(
            TestHttp.Json(ctx, "POST", "http://localhost/api/members/invite", new { email = "x@y.com", displayName = "X", roles = new[] { "Wizard" } }), ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = (TestHttpResponseData)await fn.Invite(
            TestHttp.Json(ctx, "POST", "http://localhost/api/members/invite", new { email = "x@y.com", displayName = "X", roles = new[] { "Member" } }), ctx, Ct);
        Assert.Contains((int)ok.StatusCode, new[] { 200, 201 });
        Assert.Contains("redeem", ok.ReadBodyAsString());
    }

    [Fact]
    public async Task Members_update_roles_404_then_ok()
    {
        var repo = new FakeContentRepository();
        var fn = MembersFn(repo);
        var notFound = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(new TestFunctionContext(), "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Admin" } }), "m1", Ct);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        repo.MemberById = Member();
        var ok = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(new TestFunctionContext(), "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Contributor" } }), "m1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Members_delete_blocks_self_and_allows_other()
    {
        var repo = new FakeContentRepository { MemberById = Member(oid: "self-oid") };
        var fn = MembersFn(repo);
        var self = new TestFunctionContext().WithUser("self-oid", "Me", "Admin");
        var blocked = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(self, "DELETE", "http://localhost/api/members/m1", ""), "m1", self, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var other = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");
        var ok = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(other, "DELETE", "http://localhost/api/members/m1", ""), "m1", other, Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    // ── Drafts ──────────────────────────────────────────────────────────────
    private static DraftsFunctions DraftsFn(FakeContentRepository repo) =>
        new(repo, new HtmlSanitizer(), NullLogger<DraftsFunctions>.Instance);

    private static Draft Draft(string id = "d1") =>
        new(id, "oid-1", "Author", "article", "T", "s", "Sum", "<p>b</p>", "Community", new[] { "x" }, 3, DateTime.UtcNow, DateTime.UtcNow) { Etag = "e1" };

    [Fact]
    public async Task Drafts_list_requires_oid()
    {
        var repo = new FakeContentRepository();
        var fn = DraftsFn(repo);
        var noUser = new TestFunctionContext();
        var bad = (TestHttpResponseData)await fn.List(TestHttp.Get(noUser, "http://localhost/api/drafts"), noUser, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var ok = (TestHttpResponseData)await fn.List(TestHttp.Get(ctx, "http://localhost/api/drafts"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Drafts_get_404_then_200()
    {
        var repo = new FakeContentRepository();
        var fn = DraftsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var missing = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/drafts/d1"), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        repo.DraftById = Draft();
        var ok = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/drafts/d1"), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Drafts_upsert_delete_submit()
    {
        var repo = new FakeContentRepository();
        var fn = DraftsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "Author", "Contributor");

        var draftBody = new { type = "article", title = "T", slug = "s", summary = "Sum", body = "<p>hi</p>", category = "Community", tags = new[] { "x" }, readingMinutes = 3 };
        var upsert = (TestHttpResponseData)await fn.Upsert(TestHttp.Json(ctx, "PUT", "http://localhost/api/drafts/d1", draftBody), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

        var del = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/drafts/d1", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var submit = (TestHttpResponseData)await fn.Submit(TestHttp.Raw(ctx, "POST", "http://localhost/api/drafts/d1/submit", ""), ctx, "d1", Ct);
        Assert.Contains((int)submit.StatusCode, new[] { 200, 201 });
    }

    // ── Media ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task Media_503_when_unconfigured()
    {
        var fn = new MediaFunctions(new FakeBlobService { Configured = false });
        var resp = (TestHttpResponseData)await fn.Upload(TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/media", ""));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Media_415_when_not_multipart()
    {
        var fn = new MediaFunctions(new FakeBlobService());
        var req = new TestHttpRequestData(new TestFunctionContext(), "POST", "http://localhost/api/media", "x",
            new Dictionary<string, string> { ["Content-Type"] = "application/json" });
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }
}
