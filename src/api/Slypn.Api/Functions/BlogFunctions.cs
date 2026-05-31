using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
    public async Task<HttpResponseData> GetBlogPosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog")] HttpRequestData req,
        CancellationToken ct)
    {
        var status = QueryParam(req, "status") ?? "published";
        var posts = await repo.ListBlogPostsAsync(status, ct);
        return await Ok(req, posts);
    }
}
