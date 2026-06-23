using System.Net;
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
        new(id, "slug", "Title", "Summary", "Body", "Author", DateTime.UtcNow, 5, "Community", new[] { "t" }) { Etag = "e1" };

    [Fact]
    public async Task GetArticles_returns_200_with_list()
    {
        var (fn, repo) = Make();
        repo.Articles.Add(Sample());
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticles(TestHttp.Get(ctx, "http://localhost/api/articles"), Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = resp.ReadBodyAs<List<Article>>();
        Assert.Single(body!);
    }

    [Fact]
    public async Task GetArticleBySlug_returns_404_when_missing()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(TestHttp.Get(ctx, "http://localhost/api/articles/x"), "x", Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetArticleBySlug_returns_200_when_found()
    {
        var (fn, repo) = Make();
        repo.ArticleBySlug = Sample();
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetArticleBySlug(TestHttp.Get(ctx, "http://localhost/api/articles/slug"), "slug", Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Create_returns_503_when_writes_disabled()
    {
        var (fn, repo) = Make();
        repo.Writes = false;
        var ctx = new TestFunctionContext();
        var req = TestHttp.Json(ctx, "POST", "http://localhost/api/articles", new { });
        var resp = (TestHttpResponseData)await fn.Create(req, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Create_returns_400_on_invalid_body()
    {
        var (fn, _) = Make();
        var ctx = new TestFunctionContext();
        var req = TestHttp.Json(ctx, "POST", "http://localhost/api/articles", new { title = "x" });
        var resp = (TestHttpResponseData)await fn.Create(req, Ct);
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
        var resp = (TestHttpResponseData)await fn.Create(req, Ct);
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
        var (fn, _) = Make();
        var noUser = new TestFunctionContext();
        var bad = (TestHttpResponseData)await fn.Edit(TestHttp.Raw(noUser, "POST", "http://localhost/api/articles/a1/edit", ""), noUser, "a1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var withUser = new TestFunctionContext().WithUser("oid-1", "Editor", "Contributor");
        var ok = (TestHttpResponseData)await fn.Edit(TestHttp.Raw(withUser, "POST", "http://localhost/api/articles/a1/edit", ""), withUser, "a1", Ct);
        Assert.Contains((int)ok.StatusCode, new[] { 200, 201 });
    }
}
