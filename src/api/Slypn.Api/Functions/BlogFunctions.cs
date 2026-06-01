using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
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
    [OpenApiOperation(operationId: "blog.list", tags: new[] { "blog" }, Summary = "List blog posts", Description = "Returns published blog posts, optionally filtered by status.")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Optional status filter.")]
    public async Task<HttpResponseData> GetBlogPosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog")] HttpRequestData req,
        CancellationToken ct)
    {
        var status = QueryParam(req, "status") ?? "published";
        var posts = await repo.ListBlogPostsAsync(status, ct);
        return await Ok(req, posts);
    }
}
