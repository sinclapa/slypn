using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class BlogFunctions(IContentRepository repo)
{
    /// <summary>
    /// Public blog list — articles container, filtered to type=blog &amp; status=published
    /// unless ?status= overrides. Blog posts share the Article shape; the client
    /// distinguishes by Type if it needs to.
    /// </summary>
    [Function("GetBlogPosts")]
    [OptionalAuth]
    [OpenApiOperation(operationId: "blog.list", tags: new[] { "blog" }, Summary = "List blog posts", Description = "Returns published blog posts.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article[]), Description = "List of published blog posts")]
    public async Task<HttpResponseData> GetBlogPosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        // Pinned to published: ?status= previously let an anonymous caller read
        // in-review submissions. See GetPendingBlogPosts for the gated route.
        var posts = await repo.ListBlogPostsAsync(PublishedStatus, ct);
        return await Ok(req, posts.Select(p => p.ForPublic(context)).ToList());
    }

    /// <summary>Blog posts awaiting review. Role-gated counterpart to the public list.</summary>
    [Function("GetPendingBlogPosts")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "blog.pending", tags: new[] { "blog" }, Summary = "List blog posts awaiting review", Description = "Returns blog posts with status in-review. Requires Admin or Contributor.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article[]), Description = "List of in-review blog posts")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Missing or invalid token")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Caller lacks the required role")]
    public async Task<HttpResponseData> GetPendingBlogPosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/blog")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        var posts = await repo.ListBlogPostsAsync(InReviewStatus, ct);
        return await Ok(req, ArticlesFunctions.VisibleInReview(posts, context));
    }
}
