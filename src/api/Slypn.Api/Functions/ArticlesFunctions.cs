using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class ArticlesFunctions(IContentRepository repo, ILogger<ArticlesFunctions> log)
{
    [Function("GetArticles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(QueryParam(req, "status"), ct);
        return await Ok(req, articles);
    }

    [Function("GetArticleBySlug")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug, CancellationToken ct)
    {
        var article = await repo.GetArticleBySlugAsync(slug, ct);
        if (article is null) return req.CreateResponse(HttpStatusCode.NotFound);
        return await Ok(req, article, article.Etag);
    }

    [Function("CreateArticle")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var article = await repo.CreateArticleAsync(input!, ct);
            return await Created(req, article, article.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("ReplaceArticle")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "articles/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var article = await repo.ReplaceArticleAsync(id, input!, IfMatch(req), ct);
            return await Ok(req, article, article.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("DeleteArticle")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "articles/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var status = QueryParam(req, "status");
        if (string.IsNullOrWhiteSpace(status))
            return await BadRequest(req, "DELETE /api/articles/{id} requires ?status=<partitionKey>.");
        try
        {
            await repo.DeleteArticleAsync(id, status, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }
}
