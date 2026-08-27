using System.Net;
using Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

/// <summary>
/// Reading articles. Mutations live in <see cref="ContentFunctions"/>: they are type-agnostic, so
/// filing them under "articles" was telling the caller something untrue about what they operate on.
/// </summary>
public sealed class ArticlesFunctions(IContentRepository repo)
{
    [Function("GetArticles")]
    [OptionalAuth]
    [OpenApiOperation(operationId: "articles.list", tags: new[] { "articles" }, Summary = "List articles", Description = "Returns published articles.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article[]), Description = "List of published articles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(PublishedStatus, ct);
        return await Ok(req, articles.Select(a => a.ForPublic(context)).ToList());
    }

    /// <summary>
    /// Articles awaiting review. Separate route rather than a query parameter on
    /// the public list, because [RequireRole] is a static per-function attribute:
    /// JwtMiddleware never populates a principal for an unattributed function, so
    /// a handler cannot decide "authenticate only when status != published".
    /// A distinct route keeps the security boundary visible in the route table.
    ///
    /// Under /review rather than /articles/pending: `articles/{slug}` also matches
    /// `articles/pending`, and the parameterised route wins, so the gated endpoint
    /// silently resolved to a slug lookup and 404'd.
    /// </summary>

    [Function("GetPendingArticles")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.pending", tags: new[] { "articles" }, Summary = "List articles awaiting review", Description = "Returns articles with status in-review. Requires Admin or Contributor.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article[]), Description = "List of in-review articles")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "Missing or invalid token")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Caller lacks the required role")]
    public async Task<HttpResponseData> GetPendingArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/articles")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(InReviewStatus, ct);
        return await Ok(req, ArticleVisibility.VisibleInReview(articles, context));
    }

    /// <summary>
    /// In-review items the caller may act on: everything for an Admin, own work only
    /// for a Contributor. Filtering here rather than in the browser — the client-side
    /// filter in EditorView is a display convenience, not the boundary.
    /// </summary>

    [Function("GetArticleBySlug")]
    [OptionalAuth]
    [OpenApiOperation(operationId: "articles.getBySlug", tags: new[] { "articles" }, Summary = "Get article by slug", Description = "Returns a single article identified by slug.")]
    [OpenApiParameter(name: "slug", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article slug.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Article")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Not found")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        FunctionContext context,
        string slug, CancellationToken ct)
    {
        var article = await repo.GetArticleWithNeighboursAsync(slug, ct);
        if (article is null) return req.CreateResponse(HttpStatusCode.NotFound);
        return await Ok(req, article.ForPublic(context), article.Etag);
    }
}
