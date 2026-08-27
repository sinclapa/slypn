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

/// <summary>Blog reads. Blog posts are Article rows with Type == "blog", so these cover the
/// type filter as much as the handlers themselves.</summary>
public class BlogFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── Blog ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Blog_list_returns_200()
    {
        var repo = new FakeContentRepository();
        repo.Blogs.Add(new Article("b1", "s", "T", "Sum", "B", "A", DateTime.UtcNow, 3, "News") { Type = "blog" });
        var fn = new BlogFunctions(repo);
        var rctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetBlogPosts(TestHttp.Get(rctx, "http://localhost/api/blog"), rctx, Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // Same regression guard as the articles list: ?status= used to let an
    // anonymous caller pull unpublished posts out of the public endpoint.
    [Theory]
    [InlineData("http://localhost/api/blog")]
    [InlineData("http://localhost/api/blog?status=in-review")]
    public async Task Blog_public_list_always_asks_for_published(string url)
    {
        var repo = new FakeContentRepository();
        var fn = new BlogFunctions(repo);

        var rctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetBlogPosts(TestHttp.Get(rctx, url), rctx, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("published", repo.LastBlogStatus);
    }

    [Fact]
    public async Task Blog_getBySlug_404s_when_missing_and_200s_when_found()
    {
        var repo = new FakeContentRepository();
        var fn = new BlogFunctions(repo);
        var ctx = new TestFunctionContext();

        var missing = (TestHttpResponseData)await fn.GetBlogPostBySlug(
            TestHttp.Get(ctx, "http://localhost/api/blog/nope"), ctx, "nope", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        repo.BlogPostBySlug = new Article("b1", "s", "T", "Sum", "B", "A", DateTime.UtcNow, 3, "News") { Type = "blog" };
        var found = (TestHttpResponseData)await fn.GetBlogPostBySlug(
            TestHttp.Get(ctx, "http://localhost/api/blog/s"), ctx, "s", Ct);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    [Fact]
    public async Task Blog_getBySlug_strips_the_author_oid_and_reports_canEdit()
    {
        var repo = new FakeContentRepository();
        var fn = new BlogFunctions(repo);
        repo.BlogPostBySlug = new Article("b1", "s", "T", "Sum", "B", "A", DateTime.UtcNow, 3, "News")
        { Type = "blog", AuthorId = "oid-author" };

        var anon = new TestFunctionContext();
        var anonResp = (TestHttpResponseData)await fn.GetBlogPostBySlug(
            TestHttp.Get(anon, "http://localhost/api/blog/s"), anon, "s", Ct);
        Assert.DoesNotContain("oid-author", anonResp.ReadBodyAsString());
        Assert.False(anonResp.ReadBodyAs<Article>()!.CanEdit);

        var author = new TestFunctionContext().WithUser("oid-author", "Ann", "Contributor");
        var authorResp = (TestHttpResponseData)await fn.GetBlogPostBySlug(
            TestHttp.Get(author, "http://localhost/api/blog/s"), author, "s", Ct);
        Assert.True(authorResp.ReadBodyAs<Article>()!.CanEdit);
    }

    [Fact]
    public void Blog_public_reads_carry_OptionalAuth()
    {
        foreach (var name in new[] { nameof(BlogFunctions.GetBlogPosts), nameof(BlogFunctions.GetBlogPostBySlug) })
        {
            var attrs = typeof(BlogFunctions).GetMethod(name)!
                .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.OptionalAuthAttribute), inherit: false);
            Assert.True(attrs.Length == 1, name + " is missing [OptionalAuth]");
        }
    }

    [Fact]
    public async Task Blog_pending_asks_for_in_review_and_requires_a_role()
    {
        var repo = new FakeContentRepository();
        repo.Blogs.Add(new Article("b1", "s", "T", "Sum", "B", "A", DateTime.UtcNow, 3, "News") { Type = "blog" });
        var fn = new BlogFunctions(repo);

        var ctx = new TestFunctionContext().WithUser("oid-admin", "Ada", "Admin");
        var resp = (TestHttpResponseData)await fn.GetPendingBlogPosts(
            TestHttp.Get(ctx, "http://localhost/api/review/blog"), ctx, Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("in-review", repo.LastBlogStatus);

        var attr = typeof(BlogFunctions)
            .GetMethod(nameof(BlogFunctions.GetPendingBlogPosts))!
            .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.RequireRoleAttribute), inherit: false)
            .Cast<Slypn.Api.Infrastructure.RequireRoleAttribute>()
            .SingleOrDefault();
        Assert.NotNull(attr);
        Assert.Equal(new[] { "Admin", "Contributor" }, attr!.Roles);
    }
}
