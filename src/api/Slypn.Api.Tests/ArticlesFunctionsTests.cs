using System.Net;
using Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Slypn.Api.Functions;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

/// <summary>Reading articles: the public list, by-slug, and the review queue.</summary>
public class ArticlesFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static (ArticlesFunctions fn, FakeContentRepository repo) Make()
    {
        var repo = new FakeContentRepository();
        return (new ArticlesFunctions(repo), repo);
    }

    private static Article Sample(string id = "a1") =>
        new(id, "slug", "Title", "Summary", "Body", "Author", DateTime.UtcNow, 5, "Community") { Etag = "e1" };

    // ── SEC-2: publishing is Admin-only ─────────────────────────────────────
    // Create/Replace took Status straight from the request body, validated only
    // against the enum in ArticleInput. A Contributor could POST status=published
    // and go live without review, or PUT over an existing published article —
    // even though Publish and Delete are both [RequireRole("Admin")].

    private static TestFunctionContext Author() =>
        new TestFunctionContext().WithUser("oid-author", "Ann", "Contributor");

    private static TestFunctionContext Contributor() =>
        new TestFunctionContext().WithUser("oid-contrib", "Carla", "Contributor");

    private static TestFunctionContext Admin() =>
        new TestFunctionContext().WithUser("oid-admin", "Ada", "Admin");

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
}
