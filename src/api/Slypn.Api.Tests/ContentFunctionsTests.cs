using System.Net;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

/// <summary>Mutating content, whatever type it is: create, replace, delete, and every workflow
/// transition. These are the handlers that moved to /api/content.</summary>
public class ContentFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static (ContentFunctions fn, FakeContentRepository repo) Make()
    {
        var repo = new FakeContentRepository();
        var fn = new ContentFunctions(repo, new HtmlSanitizer(), NullLogger<ContentFunctions>.Instance);
        return (fn, repo);
    }

    private static object ValidArticleInput() => new
    {
        slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
        body = "Body content long enough.", author = "Jane", readingMinutes = 5,
        category = "Community", status = "draft", type = "article",
    };

    private static object ArticleInputWith(string status) => new
    {
        slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
        body = "Body content long enough.", author = "Jane", readingMinutes = 5,
        category = "Community", status, type = "article",
    };

    private static Article Sample(string id = "a1") =>
        new(id, "slug", "Title", "Summary", "Body", "Author", DateTime.UtcNow, 5, "Community") { Etag = "e1" };

    private static TestFunctionContext Author() =>
        new TestFunctionContext().WithUser("oid-author", "Ann", "Contributor");

    private static TestFunctionContext Contributor() =>
        new TestFunctionContext().WithUser("oid-contrib", "Carla", "Contributor");

    private static TestFunctionContext Admin() =>
        new TestFunctionContext().WithUser("oid-admin", "Ada", "Admin");

    [Fact]
    public async Task Create_returns_503_when_writes_disabled()
    {
        var (fn, repo) = Make();
        repo.Writes = false;
        var ctx = new TestFunctionContext();
        var req = TestHttp.Json(ctx, "POST", "http://localhost/api/articles", new { });
        var resp = (TestHttpResponseData)await fn.Create(req, ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Create_returns_400_on_invalid_body()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var req = TestHttp.Json(ctx, "POST", "http://localhost/api/articles", new { title = "x" });
        var resp = (TestHttpResponseData)await fn.Create(req, ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_returns_201_on_valid_body()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var input = new
        {
            slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
            body = "Body content long enough.", author = "Jane", readingMinutes = 5,
            category = "Community", status = "draft", type = "article",
        };
        var req = TestHttp.Json(ctx, "POST", "http://localhost/api/articles", input);
        var resp = (TestHttpResponseData)await fn.Create(req, ctx, Ct);
        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Equal("new-id", resp.ReadBodyAs<Article>()!.Id);
    }

    [Fact]
    public async Task Delete_requires_status_query()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/articles/a1", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var ok = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/articles/a1?status=published", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Publish_returns_200()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Publish(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/publish", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_requires_oid_and_returns_201_with_user()
    {
        var (fn, repo) = Make();
        // The ownership check reads the live row first, so it must exist and be ours.
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-1" };
        var noUser = new TestFunctionContext();
        var bad = (TestHttpResponseData)await fn.Edit(TestHttp.Raw(noUser, "POST", "http://localhost/api/articles/a1/edit", ""), noUser, "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var withUser = new TestFunctionContext().WithUser("oid-1", "Editor", "Contributor");
        var ok = (TestHttpResponseData)await fn.Edit(TestHttp.Raw(withUser, "POST", "http://localhost/api/articles/a1/edit", ""), withUser, "a1", Ct);
        Assert.Contains((int)ok.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Replace_503_when_writes_disabled_then_200_on_valid()
    {
        var (fn, repo) = Make();
        repo.Writes = false;
        var ctx = new TestFunctionContext();
        var input = new
        {
            slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
            body = "Body content long enough.", author = "Jane", readingMinutes = 5,
            category = "Community", status = "draft",
        };

        var disabled = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", input), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", input), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Replace_400_on_invalid_body()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", new { }), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task RequestDeletion_requires_oid_and_validates_writes()
    {
        var (fn, repo) = Make();
        var noUser = new TestFunctionContext();
        var noOid = (TestHttpResponseData)await fn.RequestDeletion(TestHttp.Raw(noUser, "POST", "http://localhost/api/articles/a1/request-deletion", ""), noUser, "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noOid.StatusCode);

        var ctx = new TestFunctionContext().WithUser("oid-1", "Author", "Contributor");
        // The ownership check reads the live row first, so it must exist and be ours.
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-1" };
        repo.Writes = false;
        var disabled = (TestHttpResponseData)await fn.RequestDeletion(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/request-deletion", ""), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.RequestDeletion(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/request-deletion", ""), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task CancelDeletion_503_then_200()
    {
        var (fn, repo) = Make();
        var ctx = new TestFunctionContext();
        repo.Writes = false;
        var disabled = (TestHttpResponseData)await fn.CancelDeletion(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/cancel-deletion", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.CancelDeletion(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/cancel-deletion", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Revise_503_invalid_input_then_200()
    {
        var (fn, repo) = Make();
        var ctx = new TestFunctionContext();
        repo.Writes = false;
        var disabled = (TestHttpResponseData)await fn.Revise(TestHttp.Json(ctx, "POST", "http://localhost/api/articles/a1/revise", new { feedback = "pls fix" }), "a1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var badInput = (TestHttpResponseData)await fn.Revise(TestHttp.Json(ctx, "POST", "http://localhost/api/articles/a1/revise", new { }), "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badInput.StatusCode);

        var ok = (TestHttpResponseData)await fn.Revise(TestHttp.Json(ctx, "POST", "http://localhost/api/articles/a1/revise", new { feedback = "Please revise the introduction section." }), "a1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Publish_503_when_writes_disabled()
    {
        var (fn, repo) = Make();
        repo.Writes = false;
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Publish(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/publish", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Publish_400_when_repo_throws_InvalidOperation()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new InvalidOperationException("Not in review");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Publish(TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/publish", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_412_when_storage_precondition_fails()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/articles", ValidArticleInput()), ctx, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Create_409_when_storage_conflict()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(409, "Conflict");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/articles", ValidArticleInput()), ctx, Ct);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Create_500_on_unknown_storage_error()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(500, "Internal storage error");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/articles", ValidArticleInput()), ctx, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_404_on_storage_not_found()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(404, "Not found");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/articles/a1?status=published", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_400_on_invalid_json()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var req = TestHttp.Raw(ctx, "POST", "http://localhost/api/articles", "not-json");
        var resp = (TestHttpResponseData)await fn.Create(req, ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Replace_412_when_repo_precondition_fails()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", ValidArticleInput(),
                new Dictionary<string, string> { ["If-Match"] = "\"etag-1\"" }),
            ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Publish_412_when_repo_fails()
    {
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Publish(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/publish", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_400_when_repo_rejects_and_412_when_storage_fails()
    {
        var (fn, repo) = Make();
        var ctx = new TestFunctionContext().WithUser("oid-1", "Ed", "Contributor");
        // The ownership check reads the live row first, so it must exist and be ours.
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-1" };

        repo.ThrowOnWrite = new InvalidOperationException("Article not published");
        var bad = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var err = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    [Fact]
    public async Task RequestDeletion_400_when_repo_rejects_and_412_when_storage_fails()
    {
        var (fn, repo) = Make();
        var ctx = new TestFunctionContext().WithUser("oid-1", "Author", "Contributor");
        // The ownership check reads the live row first, so it must exist and be ours.
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-1" };

        repo.ThrowOnWrite = new InvalidOperationException("Not eligible");
        var bad = (TestHttpResponseData)await fn.RequestDeletion(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/request-deletion", ""), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var err = (TestHttpResponseData)await fn.RequestDeletion(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/request-deletion", ""), ctx, "a1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    [Fact]
    public async Task CancelDeletion_400_when_repo_rejects_and_412_when_storage_fails()
    {
        var (fn, repo) = Make();
        var ctx = new TestFunctionContext();

        repo.ThrowOnWrite = new InvalidOperationException("Not pending deletion");
        var bad = (TestHttpResponseData)await fn.CancelDeletion(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/cancel-deletion", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var err = (TestHttpResponseData)await fn.CancelDeletion(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/cancel-deletion", ""), "a1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    [Fact]
    public async Task Revise_400_when_repo_rejects_and_412_when_storage_fails()
    {
        var (fn, repo) = Make();
        var ctx = new TestFunctionContext();
        var validFeedback = new { feedback = "Please revise the introduction section." };

        repo.ThrowOnWrite = new InvalidOperationException("Article not in-review");
        var bad = (TestHttpResponseData)await fn.Revise(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles/a1/revise", validFeedback), "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var err = (TestHttpResponseData)await fn.Revise(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles/a1/revise", validFeedback), "a1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    // ── Ownership on the revision + deletion endpoints ──────────────────────────
    // The UI hides these controls, but the routes are reachable directly, so the rule
    // has to hold here. ThrowOnWrite proves the refusal happens BEFORE the repository
    // is touched — otherwise a 403 could be a storage error in disguise.

    // ── Withdraw from review ────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_returns_the_authors_own_submission_to_drafts()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Withdraw(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/withdraw", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // It is the in-review row we looked at, not the published one.
        Assert.Equal("in-review", repo.LastArticleLookupStatus);
        // A self-withdraw carries no feedback — there is no reviewer leaving a note.
        Assert.Null(resp.ReadBodyAs<Draft>()!.RevisionFeedback);
    }

    [Fact]
    public async Task Withdraw_forbids_another_contributor()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Withdraw(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/withdraw", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Withdraw_forbids_an_admin_who_did_not_write_it()
    {
        // Deliberate: an Admin returning someone else's work uses /revise, which
        // requires feedback. This endpoint has no admin bypass on purpose.
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Admin();

        var resp = (TestHttpResponseData)await fn.Withdraw(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/withdraw", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("/revise", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Withdraw_404s_when_nothing_is_awaiting_review()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = null;
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Withdraw(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/withdraw", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Withdraw_forbids_legacy_content_with_no_recorded_author()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = null };
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Withdraw(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/withdraw", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public void Withdraw_requires_a_role()
    {
        var attr = typeof(ContentFunctions).GetMethod(nameof(ContentFunctions.Withdraw))!
            .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.RequireRoleAttribute), inherit: false)
            .Cast<Slypn.Api.Infrastructure.RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(new[] { "Admin", "Contributor" }, attr!.Roles);
    }

    [Fact]
    public async Task Edit_hands_back_the_submission_when_a_revision_is_already_awaiting_review()
    {
        // Nothing to edit until an admin has dealt with it, and a second draft would put
        // two competing revisions of one article in the queue. 200 tells the client to
        // show the submission — read-only — rather than open a fresh editor.
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.Articles.Add(Sample("pending") with { AuthorId = "oid-author", ReplacesArticleId = "a1" });
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("pending", resp.ReadBodyAs<Article>()!.Id);
    }

    [Fact]
    public async Task Edit_finds_an_in_review_revision_of_a_blog_post_too()
    {
        // ListArticlesAsync is filtered to articles, so checking it alone would miss a
        // blog revision and mint a competing draft.
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author", Type = "blog" };
        repo.Blogs.Add(Sample("pending-blog") with { AuthorId = "oid-author", Type = "blog", ReplacesArticleId = "a1" });
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("pending-blog", resp.ReadBodyAs<Article>()!.Id);
    }

    [Fact]
    public async Task Edit_ignores_another_authors_in_review_revision()
    {
        // Someone else revising the same article must not block this author, and must
        // not have their submission handed over either.
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.Articles.Add(Sample("theirs") with { AuthorId = "someone-else", ReplacesArticleId = "a1" });
        repo.RevisionResumes = false;
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_ignores_an_in_review_item_that_replaces_a_different_article()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.Articles.Add(Sample("other") with { AuthorId = "oid-author", ReplacesArticleId = "a-different-one" });
        repo.RevisionResumes = false;
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_returns_201_when_it_mints_a_new_revision()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.RevisionResumes = false;
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_returns_200_when_the_author_already_has_a_revision_on_the_go()
    {
        // The client branches on this to send them to the editor instead of opening a
        // second window onto work already in progress.
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.RevisionResumes = true;
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_allows_the_author_of_the_published_article()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        var ctx = Author();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Equal("published", repo.LastArticleLookupStatus);
    }

    [Fact]
    public async Task Edit_forbids_a_contributor_who_is_not_the_author()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("your own", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Edit_allows_an_admin_regardless_of_author()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "someone-else" };
        var ctx = Admin();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/edit", ""), ctx, "a1", Ct);

        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Edit_is_admin_only_on_legacy_content_that_has_no_author()
    {
        // Everything published before AuthorId existed carries none. A null author must
        // match nobody rather than everybody — including an anonymous caller's null oid.
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = null };
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var contributor = Contributor();

        var refused = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(contributor, "POST", "http://localhost/api/articles/a1/edit", ""), contributor, "a1", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        repo.ThrowOnWrite = null;
        var admin = Admin();
        var allowed = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(admin, "POST", "http://localhost/api/articles/a1/edit", ""), admin, "a1", Ct);
        Assert.Contains((int)allowed.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Edit_404s_when_the_published_article_is_missing()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = null;
        var ctx = Admin();

        var resp = (TestHttpResponseData)await fn.Edit(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/nope/edit", ""), ctx, "nope", Ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task RequestDeletion_forbids_a_contributor_who_is_not_the_author()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample() with { AuthorId = "oid-author" };
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.RequestDeletion(
            TestHttp.Raw(ctx, "POST", "http://localhost/api/articles/a1/request-deletion", ""), ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Content type on create/replace ──────────────────────────────────────────

    [Fact]
    public async Task Create_refuses_content_with_no_type()
    {
        // The route is type-agnostic, so the body has to say what it is making. Defaulting
        // to "article" is the bug this exists to prevent, not a convenience.
        var (fn, repo) = Make();
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Admin();

        var noType = new
        {
            slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
            body = "Body content long enough.", author = "Jane", readingMinutes = 5,
            category = "Community", status = "draft",
        };

        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles", noType), ctx, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("type", resp.ReadBodyAsString());
    }

    [Theory]
    [InlineData("article")]
    [InlineData("blog")]
    public async Task Create_accepts_either_content_type(string type)
    {
        var (fn, _) = Make();
        var ctx = Admin();
        var payload = new
        {
            slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
            body = "Body content long enough.", author = "Jane", readingMinutes = 5,
            category = "Community", status = "draft", type,
        };

        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles", payload), ctx, Ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Theory]
    [InlineData("published")]
    [InlineData("rejected")]
    public async Task Create_forbids_non_admin_setting_admin_only_status(string status)
    {
        var (fn, repo) = Make();
        // Throwing on write proves the refusal happens BEFORE the repository is
        // touched — a 403 here cannot be the storage error path in disguise.
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles", ArticleInputWith(status)), ctx, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("Only an Admin", resp.ReadBodyAsString());
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("in-review")]
    public async Task Create_allows_non_admin_to_file_for_review(string status)
    {
        var (fn, _) = Make();
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles", ArticleInputWith(status)), ctx, Ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Create_allows_admin_to_publish_directly()
    {
        var (fn, _) = Make();
        var ctx = Admin();

        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/articles", ArticleInputWith("published")), ctx, Ct);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Replace_forbids_non_admin_setting_published()
    {
        var (fn, _) = Make();
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", ArticleInputWith("published")),
            ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Replace_forbids_non_admin_overwriting_a_published_article()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample("a1");           // a1 exists in the published partition
        repo.ThrowOnWrite = new RequestFailedException(500, "should not be reached");
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", ArticleInputWith("draft")),
            ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("published", repo.LastArticleLookupStatus);
        Assert.Contains("/edit", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Replace_allows_admin_to_overwrite_a_published_article()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = Sample("a1");
        var ctx = Admin();

        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", ArticleInputWith("published")),
            ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Replace_allows_non_admin_when_the_article_is_not_published()
    {
        var (fn, repo) = Make();
        repo.ArticleByIdAndStatus = null;                   // nothing in the published partition
        var ctx = Contributor();

        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/articles/a1", ArticleInputWith("in-review")),
            ctx, "a1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
