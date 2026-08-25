using System.Net;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

public class ArticlesFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static (ArticlesFunctions fn, FakeContentRepository repo) Make()
    {
        var repo = new FakeContentRepository();
        var fn = new ArticlesFunctions(repo, new HtmlSanitizer(), NullLogger<ArticlesFunctions>.Instance);
        return (fn, repo);
    }

    private static Article Sample(string id = "a1") =>
        new(id, "slug", "Title", "Summary", "Body", "Author", DateTime.UtcNow, 5, "Community") { Etag = "e1" };

    // ── Public list is pinned to published ──────────────────────────────────
    // Regression guards. The status filter used to come from the query string,
    // so ?status=in-review exposed unpublished submissions, and a bare GET
    // passed null — which the repository treats as "no filter", returning every
    // partition. Asserting the status the handler ASKS for catches a regression
    // that a 200-only assertion would sail straight past.

    [Theory]
    [InlineData("http://localhost/api/articles")]
    [InlineData("http://localhost/api/articles?status=in-review")]
    [InlineData("http://localhost/api/articles?status=draft")]
    [InlineData("http://localhost/api/articles?status=rejected")]
    public async Task GetArticles_always_asks_for_published(string url)
    {
        var (fn, repo) = Make();
        var rctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticles(TestHttp.Get(rctx, url), rctx, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("published", repo.LastArticlesStatus);
    }

    [Fact]
    public async Task GetPendingArticles_asks_for_in_review()
    {
        var (fn, repo) = Make();
        repo.Articles.Add(Sample());

        var ctx = Admin();
        var resp = (TestHttpResponseData)await fn.GetPendingArticles(
            TestHttp.Get(ctx, "http://localhost/api/review/articles"), ctx, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("in-review", repo.LastArticlesStatus);
        Assert.Single(resp.ReadBodyAs<List<Article>>()!);
    }

    [Fact]
    public void GetPendingArticles_requires_a_role()
    {
        // The gate is the whole point of the separate route: without the
        // attribute JwtMiddleware lets the call through unauthenticated.
        var attr = typeof(ArticlesFunctions)
            .GetMethod(nameof(ArticlesFunctions.GetPendingArticles))!
            .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.RequireRoleAttribute), inherit: false)
            .Cast<Slypn.Api.Infrastructure.RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(new[] { "Admin", "Contributor" }, attr!.Roles);
    }

    [Fact]
    public async Task GetArticles_returns_200_with_list()
    {
        var (fn, repo) = Make();
        repo.Articles.Add(Sample());
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticles(TestHttp.Get(ctx, "http://localhost/api/articles"), ctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = resp.ReadBodyAs<List<Article>>();
        Assert.Single(body!);
    }

    [Fact]
    public async Task GetArticleBySlug_returns_404_when_missing()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(TestHttp.Get(ctx, "http://localhost/api/articles/x"), ctx, "x", Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetArticleBySlug_returns_200_when_found()
    {
        var (fn, repo) = Make();
        repo.ArticleBySlug = Sample();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(TestHttp.Get(ctx, "http://localhost/api/articles/slug"), ctx, "slug", Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

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
            category = "Community", status = "draft",
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

    private static object ValidArticleInput() => new
    {
        slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
        body = "Body content long enough.", author = "Jane", readingMinutes = 5,
        category = "Community", status = "draft",
    };

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
    public async Task GetArticleBySlug_with_neighbours_returns_200()
    {
        var (fn, repo) = Make();
        repo.ArticleBySlug = Sample();
        var ctx = new TestFunctionContext();
        var rctx = ctx;
        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(
            TestHttp.Get(rctx, "http://localhost/api/articles/slug"), rctx, "slug", Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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

    // ── SEC-2: publishing is Admin-only ─────────────────────────────────────
    // Create/Replace took Status straight from the request body, validated only
    // against the enum in ArticleInput. A Contributor could POST status=published
    // and go live without review, or PUT over an existing published article —
    // even though Publish and Delete are both [RequireRole("Admin")].

    private static object ArticleInputWith(string status) => new
    {
        slug = "valid-slug", title = "A valid title", summary = "A long enough summary.",
        body = "Body content long enough.", author = "Jane", readingMinutes = 5,
        category = "Community", status,
    };

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
        var attr = typeof(ArticlesFunctions).GetMethod(nameof(ArticlesFunctions.Withdraw))!
            .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.RequireRoleAttribute), inherit: false)
            .Cast<Slypn.Api.Infrastructure.RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(new[] { "Admin", "Contributor" }, attr!.Roles);
    }

    private static TestFunctionContext Author() =>
        new TestFunctionContext().WithUser("oid-author", "Ann", "Contributor");

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

    // ── canEdit / authorId on public reads ──────────────────────────────────────

    [Fact]
    public async Task GetArticleBySlug_never_leaks_the_author_oid()
    {
        var (fn, repo) = Make();
        repo.ArticleBySlug = Sample() with { AuthorId = "oid-secret" };
        var anon = new TestFunctionContext();

        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(
            TestHttp.Get(anon, "http://localhost/api/articles/slug"), anon, "slug", Ct);

        // Asserted on the raw body: deserialising into Article would hide the leak.
        var body = resp.ReadBodyAsString();
        Assert.DoesNotContain("authorId", body);
        Assert.DoesNotContain("oid-secret", body);
    }

    [Theory]
    [InlineData("oid-author", "Contributor", true)]   // the author
    [InlineData("oid-other", "Contributor", false)]   // a different contributor
    [InlineData("oid-admin", "Admin", true)]          // any admin
    [InlineData("oid-author", "Member", false)]       // authored it, but lost the role
    public async Task GetArticleBySlug_reports_canEdit_for_the_caller(string oid, string role, bool expected)
    {
        var (fn, repo) = Make();
        repo.ArticleBySlug = Sample() with { AuthorId = "oid-author" };
        var ctx = new TestFunctionContext().WithUser(oid, "U", role);

        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(
            TestHttp.Get(ctx, "http://localhost/api/articles/slug"), ctx, "slug", Ct);

        Assert.Equal(expected, resp.ReadBodyAs<Article>()!.CanEdit);
    }

    [Fact]
    public async Task GetArticleBySlug_reports_canEdit_false_when_anonymous()
    {
        var (fn, repo) = Make();
        repo.ArticleBySlug = Sample() with { AuthorId = "oid-author" };
        var anon = new TestFunctionContext();

        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(
            TestHttp.Get(anon, "http://localhost/api/articles/slug"), anon, "slug", Ct);

        Assert.False(resp.ReadBodyAs<Article>()!.CanEdit);
    }

    [Fact]
    public void Public_reads_carry_OptionalAuth()
    {
        // Without it JwtMiddleware never populates a principal on these routes, so
        // canEdit would be silently false for everyone — including the author.
        foreach (var name in new[] { nameof(ArticlesFunctions.GetArticles), nameof(ArticlesFunctions.GetArticleBySlug) })
        {
            var attrs = typeof(ArticlesFunctions).GetMethod(name)!
                .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.OptionalAuthAttribute), inherit: false);
            Assert.True(attrs.Length == 1, name + " is missing [OptionalAuth]");
        }
    }

    [Fact]
    public async Task GetPendingArticles_shows_a_contributor_only_their_own_submissions()
    {
        var (fn, repo) = Make();
        repo.Articles.Add(Sample("mine") with { AuthorId = "oid-contrib" });
        repo.Articles.Add(Sample("theirs") with { AuthorId = "oid-someone-else" });

        var ctx = Contributor();
        var mine = (TestHttpResponseData)await fn.GetPendingArticles(
            TestHttp.Get(ctx, "http://localhost/api/review/articles"), ctx, Ct);
        Assert.Equal("mine", Assert.Single(mine.ReadBodyAs<List<Article>>()!).Id);

        var admin = Admin();
        var all = (TestHttpResponseData)await fn.GetPendingArticles(
            TestHttp.Get(admin, "http://localhost/api/review/articles"), admin, Ct);
        Assert.Equal(2, all.ReadBodyAs<List<Article>>()!.Count);
    }

    private static TestFunctionContext Contributor() =>
        new TestFunctionContext().WithUser("oid-contrib", "Carla", "Contributor");

    private static TestFunctionContext Admin() =>
        new TestFunctionContext().WithUser("oid-admin", "Ada", "Admin");

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
