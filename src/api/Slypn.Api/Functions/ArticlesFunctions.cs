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

public sealed class ArticlesFunctions(IContentRepository repo, IHtmlSanitizer sanitizer, ILogger<ArticlesFunctions> log)
{
    /// <summary>
    /// Public article list. Always published — the caller cannot widen this.
    ///
    /// The status filter used to come from the query string, which meant an
    /// anonymous caller could read in-review submissions with ?status=, and a
    /// bare GET (no parameter at all) returned EVERY partition because a null
    /// status means "no filter" in the repository. Unpublished work is now only
    /// reachable through GetPendingArticles below, which carries a role gate.
    /// </summary>
    [Function("GetArticles")]
    [OpenApiOperation(operationId: "articles.list", tags: new[] { "articles" }, Summary = "List articles", Description = "Returns published articles.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article[]), Description = "List of published articles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(PublishedStatus, ct);
        return await Ok(req, articles);
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
        CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(InReviewStatus, ct);
        return await Ok(req, articles);
    }

    [Function("GetArticleBySlug")]
    [OpenApiOperation(operationId: "articles.getBySlug", tags: new[] { "articles" }, Summary = "Get article by slug", Description = "Returns a single article identified by slug.")]
    [OpenApiParameter(name: "slug", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article slug.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Article")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Not found")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug, CancellationToken ct)
    {
        var article = await repo.GetArticleWithNeighboursAsync(slug, ct);
        if (article is null) return req.CreateResponse(HttpStatusCode.NotFound);
        return await Ok(req, article, article.Etag);
    }

    [Function("CreateArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.create", tags: new[] { "articles" }, Summary = "Create article", Description = "Creates a new article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Article), Description = "Created article")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("ReplaceArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.replace", tags: new[] { "articles" }, Summary = "Replace article", Description = "Replaces an existing article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Updated article")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.delete", tags: new[] { "articles" }, Summary = "Delete article", Description = "Deletes an article or blog post using its id and partition key status.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Article partition key status.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Admin approves an in-review article. Moves it to status=published
    /// and stamps PublishedAt with the approval time.
    /// </summary>
    [Function("PublishArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.publish", tags: new[] { "articles" }, Summary = "Publish article", Description = "Moves an article or blog post to published status.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Published article")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Create a draft revision of a published article. The published version stays live;
    /// on approval the revision replaces it in place. Returns the editable draft.
    /// </summary>
    [Function("EditPublishedArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.edit", tags: new[] { "articles" }, Summary = "Edit published", Description = "Creates a draft revision of a published article or blog post for approval.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Published article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Draft), Description = "Created draft revision")]
    public async Task<HttpResponseData> Edit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/edit")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var editorOid = context.GetUserOid();
        if (editorOid is null) return await BadRequest(req, "Token missing oid claim.");
        var editorName = context.GetUserName() ?? "Member";
        try
        {
            var draft = await repo.CreateRevisionDraftAsync(id, editorOid, editorName, ct);
            return await Created(req, draft, draft.Etag);
        }
        catch (InvalidOperationException ex) { return await BadRequest(req, ex.Message); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Request deletion of a published article (pending admin approval). The article stays
    /// live until an admin approves the deletion via DELETE.
    /// </summary>
    [Function("RequestArticleDeletion")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.requestDeletion", tags: new[] { "articles" }, Summary = "Request deletion", Description = "Flags a published article or blog post for deletion, pending admin approval.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Published article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Updated article")]
    public async Task<HttpResponseData> RequestDeletion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/request-deletion")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var requesterOid = context.GetUserOid();
        if (requesterOid is null) return await BadRequest(req, "Token missing oid claim.");
        try
        {
            var article = await repo.RequestArticleDeletionAsync(id, requesterOid, ct);
            return await Ok(req, article, article.Etag);
        }
        catch (InvalidOperationException ex) { return await BadRequest(req, ex.Message); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>Admin clears a pending deletion request, keeping the article published.</summary>
    [Function("CancelArticleDeletion")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.cancelDeletion", tags: new[] { "articles" }, Summary = "Keep article", Description = "Clears a pending deletion request on a published article or blog post.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Published article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Updated article")]
    public async Task<HttpResponseData> CancelDeletion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/cancel-deletion")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        try
        {
            var article = await repo.CancelArticleDeletionAsync(id, ct);
            return await Ok(req, article, article.Etag);
        }
        catch (InvalidOperationException ex) { return await BadRequest(req, ex.Message); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Admin returns an in-review article to the author as a draft with revision feedback.
    /// The in-review article is deleted and a draft is created so the author can edit and resubmit.
    /// </summary>
    [Function("ReviseArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "articles.revise", tags: new[] { "articles" }, Summary = "Request revision", Description = "Returns an in-review article or blog post to the author as a draft with feedback.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(RejectionInput), Required = true, Description = "Revision feedback.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "Draft with revision feedback")]
    public async Task<HttpResponseData> Revise(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/revise")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var (input, err) = await ReadValidatedAsync<RejectionInput>(req, ct);
        if (err is not null) return err;

        try
        {
            var draft = await repo.ReviseArticleAsync(id, input!.Feedback.Trim(), ct);
            return await Ok(req, draft, draft.Etag);
        }
        catch (InvalidOperationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
