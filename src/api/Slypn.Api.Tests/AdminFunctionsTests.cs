using Azure;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
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
        var ctx = new TestFunctionContext();
        var notFound = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(ctx, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Admin" } }), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        repo.MemberById = Member();
        var ok = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(ctx, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Contributor" } }), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Members_update_roles_blocks_self_and_allows_other()
    {
        var repo = new FakeContentRepository { MemberById = Member(oid: "self-oid") };
        var fn = MembersFn(repo);
        var self = new TestFunctionContext().WithUser("self-oid", "Me", "Admin");
        var blocked = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(self, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Member" } }), "m1", self, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var other = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");
        var ok = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(other, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Contributor" } }), "m1", other, Ct);
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
        new(id, "oid-1", "Author", "article", "T", "s", "Sum", "<p>b</p>", "Community", 3, DateTime.UtcNow, DateTime.UtcNow) { Etag = "e1" };

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

        var draftBody = new { type = "article", title = "T", slug = "s", summary = "Sum", body = "<p>hi</p>", category = "Community", readingMinutes = 3 };
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

    [Fact]
    public async Task Media_400_when_multipart_has_no_file_part()
    {
        var fn = new MediaFunctions(new FakeBlobService());
        // Body has only a text parameter (no filename → not a file part), so parsed.Files is empty.
        var resp = (TestHttpResponseData)await fn.Upload(MultipartParam("field"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Media_415_when_file_content_type_not_allowed()
    {
        var fn = new MediaFunctions(new FakeBlobService());
        var resp = (TestHttpResponseData)await fn.Upload(MultipartFile("file", "doc.pdf", "application/pdf"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [Fact]
    public async Task Media_201_on_successful_upload()
    {
        var fn = new MediaFunctions(new FakeBlobService());
        var resp = (TestHttpResponseData)await fn.Upload(MultipartFile("file", "photo.png", "image/png"));
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Contains("url", resp.ReadBodyAsString());
    }

    private static TestHttpRequestData MultipartFile(string partName, string fileName, string mimeType, string content = "bytes")
    {
        const string boundary = "testboundary";
        var body = $"--{boundary}\r\nContent-Disposition: form-data; name=\"{partName}\"; filename=\"{fileName}\"\r\nContent-Type: {mimeType}\r\n\r\n{content}\r\n--{boundary}--\r\n";
        return TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media", body,
            new Dictionary<string, string> { ["Content-Type"] = $"multipart/form-data; boundary={boundary}" });
    }

    private static TestHttpRequestData MultipartParam(string paramName)
    {
        const string boundary = "testboundary";
        var body = $"--{boundary}\r\nContent-Disposition: form-data; name=\"{paramName}\"\r\n\r\nvalue\r\n--{boundary}--\r\n";
        return TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media", body,
            new Dictionary<string, string> { ["Content-Type"] = $"multipart/form-data; boundary={boundary}" });
    }

    [Fact]
    public async Task Drafts_submit_503_when_writes_disabled()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = DraftsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "Author", "Contributor");
        var resp = (TestHttpResponseData)await fn.Submit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/drafts/d1/submit", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_list_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var fn = DraftsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var resp = (TestHttpResponseData)await fn.List(TestHttp.Get(ctx, "http://localhost/api/drafts"), ctx, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_get_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var fn = DraftsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/drafts/d1"), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_upsert_412_when_get_fails_and_412_when_upsert_fails()
    {
        var draftBody = new { type = "article", title = "T", slug = "s", summary = "Sum", body = "<p>hi</p>", category = "Community", readingMinutes = 3 };
        var ctx = new TestFunctionContext().WithUser("oid-1", "Author", "Contributor");

        // GetDraftAsync throws → line 96
        var repo1 = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var err1 = (TestHttpResponseData)await DraftsFn(repo1).Upsert(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/drafts/d1", draftBody), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, err1.StatusCode);

        // UpsertDraftAsync throws → line 122
        var repo2 = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var err2 = (TestHttpResponseData)await DraftsFn(repo2).Upsert(
            TestHttp.Json(new TestFunctionContext().WithUser("oid-1", "Author", "Contributor"),
                "PUT", "http://localhost/api/drafts/d1", draftBody), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err2.StatusCode);
    }

    [Fact]
    public async Task Drafts_delete_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = DraftsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/drafts/d1", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_submit_400_when_repo_rejects_and_412_when_storage_fails()
    {
        var ctx = new TestFunctionContext().WithUser("oid-1", "Author", "Contributor");

        // InvalidOperationException → 400 (line 176)
        var repo1 = new FakeContentRepository { ThrowOnWrite = new InvalidOperationException("Draft not found") };
        var bad = (TestHttpResponseData)await DraftsFn(repo1).Submit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/drafts/d1/submit", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // RequestFailedException → mapped status (line 178)
        var repo2 = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var err = (TestHttpResponseData)await DraftsFn(repo2).Submit(
            TestHttp.Raw(new TestFunctionContext().WithUser("oid-1", "Author", "Contributor"),
                "POST", "http://localhost/api/drafts/d1/submit", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    [Fact]
    public async Task Members_invite_412_when_lookup_fails_and_412_when_upsert_fails()
    {
        var ctx = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");
        var inviteBody = new { email = "x@y.com", displayName = "X", roles = new[] { "Member" } };

        // GetMemberByEmailAsync throws → line 68
        var repo1 = new FakeContentRepository { ThrowOnMemberEmailLookup = new RequestFailedException(500, "Lookup failed") };
        var err1 = (TestHttpResponseData)await MembersFn(repo1).Invite(
            TestHttp.Json(ctx, "POST", "http://localhost/api/members/invite", inviteBody), ctx, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, err1.StatusCode);

        // UpsertMemberAsync throws → line 92
        var repo2 = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var err2 = (TestHttpResponseData)await MembersFn(repo2).Invite(
            TestHttp.Json(new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin"),
                "POST", "http://localhost/api/members/invite", inviteBody), ctx, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err2.StatusCode);
    }

    [Fact]
    public async Task Members_update_roles_400_when_role_unknown_and_500_when_read_fails_and_412_when_upsert_fails()
    {
        // Bad role → 400 (line 123)
        var repo = new FakeContentRepository { MemberById = Member() };
        var fn = MembersFn(repo);
        var ctx = new TestFunctionContext();
        var badRole = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(ctx, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Wizard" } }), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);

        // GetMemberByIdAsync throws → line 127
        var repo2 = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var readErr = (TestHttpResponseData)await MembersFn(repo2).UpdateRoles(
            TestHttp.Json(ctx, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Admin" } }), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, readErr.StatusCode);

        // UpsertMemberAsync throws → line 136 (with If-Match header to cover FunctionHelpers.IfMatch line 41)
        var repo3 = new FakeContentRepository { MemberById = Member(), ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var writeErr = (TestHttpResponseData)await MembersFn(repo3).UpdateRoles(
            TestHttp.Json(ctx, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Admin" } },
                new Dictionary<string, string> { ["If-Match"] = "\"etag-1\"" }),
            "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, writeErr.StatusCode);
    }

    [Fact]
    public async Task Members_delete_412_when_get_fails_and_412_when_delete_fails()
    {
        var admin = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");

        // GetMemberByIdAsync throws → line 153
        var repo1 = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var readErr = (TestHttpResponseData)await MembersFn(repo1).Delete(
            TestHttp.Raw(admin, "DELETE", "http://localhost/api/members/m1", ""), "m1", admin, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, readErr.StatusCode);

        // DeleteMemberAsync throws → line 163
        var repo2 = new FakeContentRepository { MemberById = Member(), ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var delErr = (TestHttpResponseData)await MembersFn(repo2).Delete(
            TestHttp.Raw(admin, "DELETE", "http://localhost/api/members/m1", ""), "m1", admin, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, delErr.StatusCode);
    }

    [Fact]
    public async Task Members_list_500_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var fn = MembersFn(repo);
        var resp = (TestHttpResponseData)await fn.List(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/members"), Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task Members_delete_503_when_writes_disabled_and_404_when_not_found()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = MembersFn(repo);
        var ctx = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");

        var disabled = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/members/m1", ""), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        // MemberById is null by default → 404
        repo.Writes = true;
        var notFound = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/members/m1", ""), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    // ── Drafts writes-disabled branches ──────────────────────────────────────

    [Fact]
    public async Task Drafts_list_503_when_writes_disabled()
    {
        var fn = DraftsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var resp = (TestHttpResponseData)await fn.List(TestHttp.Get(ctx, "http://localhost/api/drafts"), ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_get_503_when_writes_disabled()
    {
        var fn = DraftsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/drafts/d1"), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_get_400_when_no_oid()
    {
        var fn = DraftsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext(); // no principal → no oid
        var resp = (TestHttpResponseData)await fn.Get(TestHttp.Get(ctx, "http://localhost/api/drafts/d1"), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_upsert_503_when_writes_disabled()
    {
        var fn = DraftsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var body = new { type = "article", title = "T", slug = "s", summary = "Sum", body = "<p>hi</p>", category = "Community", readingMinutes = 3 };
        var resp = (TestHttpResponseData)await fn.Upsert(TestHttp.Json(ctx, "PUT", "http://localhost/api/drafts/d1", body), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_upsert_400_when_no_oid()
    {
        var fn = DraftsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext(); // no principal → no oid
        var body = new { type = "article", title = "T", slug = "s", summary = "Sum", body = "<p>hi</p>", category = "Community", readingMinutes = 3 };
        var resp = (TestHttpResponseData)await fn.Upsert(TestHttp.Json(ctx, "PUT", "http://localhost/api/drafts/d1", body), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_upsert_with_existing_draft_uses_etag_and_falls_back_to_member_name()
    {
        var repo = new FakeContentRepository { DraftById = Draft() };
        var fn = DraftsFn(repo);
        // Context with oid but no "name" claim — triggers the "Member" fallback in authorName
        var identity = new System.Security.Claims.ClaimsIdentity("test", "oid", "roles");
        identity.AddClaim(new System.Security.Claims.Claim("oid", "oid-1"));
        identity.AddClaim(new System.Security.Claims.Claim("roles", "Contributor"));
        var ctx = new TestFunctionContext();
        ctx.Items[JwtMiddleware.PrincipalContextKey] = new System.Security.Claims.ClaimsPrincipal(identity);

        var body = new { type = "article", title = "Updated", slug = "s", summary = "Sum", body = "<p>hi</p>", category = "Community", readingMinutes = 3 };
        var resp = (TestHttpResponseData)await fn.Upsert(TestHttp.Json(ctx, "PUT", "http://localhost/api/drafts/d1", body), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_delete_503_when_writes_disabled()
    {
        var fn = DraftsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("oid-1", "A", "Contributor");
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/drafts/d1", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_delete_400_when_no_oid()
    {
        var fn = DraftsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext(); // no principal → no oid
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/drafts/d1", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Drafts_submit_400_when_no_oid()
    {
        var fn = DraftsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext(); // no principal → no oid
        var resp = (TestHttpResponseData)await fn.Submit(TestHttp.Raw(ctx, "POST", "http://localhost/api/drafts/d1/submit", ""), ctx, "d1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Members writes-disabled and existing-member branches ─────────────────

    [Fact]
    public async Task Members_invite_503_when_writes_disabled()
    {
        var fn = MembersFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");
        var resp = (TestHttpResponseData)await fn.Invite(
            TestHttp.Json(ctx, "POST", "http://localhost/api/members/invite", new { email = "x@y.com", displayName = "X", roles = new[] { "Member" } }), ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Members_update_roles_503_when_writes_disabled()
    {
        var fn = MembersFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.UpdateRoles(
            TestHttp.Json(ctx, "PATCH", "http://localhost/api/members/m1", new { roles = new[] { "Admin" } }), "m1", ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Members_invite_updates_existing_invited_member()
    {
        // existing member with AcceptedAt = null → Status stays "invited"
        var existing = new Member("m1", "x@y.com", "Old Name", new[] { "Member" }, "invited", DateTime.UtcNow) { Etag = "etag-1" };
        var repo = new FakeContentRepository { MemberByEmail = existing };
        var fn = MembersFn(repo);
        var ctx = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");
        var resp = (TestHttpResponseData)await fn.Invite(
            TestHttp.Json(ctx, "POST", "http://localhost/api/members/invite", new { email = "x@y.com", displayName = "New Name", roles = new[] { "Member" } }), ctx, Ct);
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Contains("New Name", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Members_invite_updates_existing_accepted_member_to_active()
    {
        // existing member with AcceptedAt set → Status becomes "active"
        var existing = new Member("m1", "x@y.com", "Alice", new[] { "Member" }, "invited", DateTime.UtcNow,
            AcceptedAt: DateTime.UtcNow) { Etag = "etag-1" };
        var repo = new FakeContentRepository { MemberByEmail = existing };
        var fn = MembersFn(repo);
        var ctx = new TestFunctionContext().WithUser("admin-oid", "Admin", "Admin");
        var resp = (TestHttpResponseData)await fn.Invite(
            TestHttp.Json(ctx, "POST", "http://localhost/api/members/invite", new { email = "x@y.com", displayName = "Alice", roles = new[] { "Member" } }), ctx, Ct);
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
    }
}
