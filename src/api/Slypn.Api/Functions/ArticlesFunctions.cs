using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class ArticlesFunctions(IContentRepository repo, IHtmlSanitizer sanitizer, ILogger<ArticlesFunctions> log)
{
    [Function("GetArticles")]
    [OpenApiOperation(operationId: "articles.list", tags: new[] { "articles" }, Summary = "List articles", Description = "Returns articles filtered by the optional status query parameter.")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Optional article status filter.")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(QueryParam(req, "status"), ct);
        return await Ok(req, articles);
    }

    [Function("GetArticleBySlug")]
    [OpenApiOperation(operationId: "articles.getBySlug", tags: new[] { "articles" }, Summary = "Get article by slug", Description = "Returns a single article identified by slug.")]
    [OpenApiParameter(name: "slug", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article slug.")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug, CancellationToken ct)
    {
        var article = await repo.GetArticleBySlugAsync(slug, ct);
        if (article is null) return req.CreateResponse(HttpStatusCode.NotFound);
        return await Ok(req, article, article.Etag);
    }

    [Function("CreateArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.create", tags: new[] { "articles" }, Summary = "Create article", Description = "Creates a new article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;
        input!.Body = sanitizer.Sanitize(input.Body);
        try
        {
            var article = await repo.CreateArticleAsync(input, ct);
            return await Created(req, article, article.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("ReplaceArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.replace", tags: new[] { "articles" }, Summary = "Replace article", Description = "Replaces an existing article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "articles/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;
        input!.Body = sanitizer.Sanitize(input.Body);
        try
        {
            var article = await repo.ReplaceArticleAsync(id, input, IfMatch(req), ct);
            return await Ok(req, article, article.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("DeleteArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.delete", tags: new[] { "articles" }, Summary = "Delete article", Description = "Deletes an article using its id and partition key status.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Article partition key status.")]
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

    /// <summary>
    /// Admin approves an in-review article. Moves it to status=published
    /// and stamps PublishedAt with the approval time.
    /// </summary>
    [Function("PublishArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.publish", tags: new[] { "articles" }, Summary = "Publish article", Description = "Moves an article to published status.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    public async Task<HttpResponseData> Publish(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/publish")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        try
        {
            var article = await repo.PublishArticleAsync(id, ct);
            return await Ok(req, article, article.Etag);
        }
        catch (InvalidOperationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    /// <summary>
    /// Admin rejects an in-review article with required feedback. Moves it to
    /// status=rejected so the author can see the feedback alongside their draft.
    /// </summary>
    [Function("RejectArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.reject", tags: new[] { "articles" }, Summary = "Reject article", Description = "Rejects an article and records reviewer feedback.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(RejectionInput), Required = true, Description = "Rejection feedback.")]
    public async Task<HttpResponseData> Reject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/reject")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var (input, err) = await ReadValidatedAsync<RejectionInput>(req, ct);
        if (err is not null) return err;

        try
        {
            var article = await repo.RejectArticleAsync(id, input!.Feedback.Trim(), ct);
            return await Ok(req, article, article.Etag);
        }
        catch (InvalidOperationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }
}
