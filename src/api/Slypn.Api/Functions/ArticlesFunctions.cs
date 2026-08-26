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
    /// Statuses a non-Admin caller may set on an article. Publishing is deliberately
    /// Admin-only — it goes through POST /api/articles/{id}/publish — and "rejected" is the
    /// other half of that same review decision. Without this, Contributors could set
    /// status=published straight from the create/replace body and skip review entirely.
    /// </summary>
    private static readonly HashSet<string> AuthorSettableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "draft", InReviewStatus };

    private const string EditOwnershipRefusal =
        "You can only edit your own published content. Ask an Admin to make the change, "
        + "or to reassign the item to you.";

    private const string WithdrawOwnershipRefusal =
        "You can only withdraw your own submissions. An Admin returns someone else's work "
        + "with POST /api/articles/{id}/revise, which carries feedback.";

    private const string DeletionOwnershipRefusal =
        "You can only request deletion of your own published content.";

    private const string PublishedReplaceRefusal =
        "This article is published — only an Admin can replace it. "
        + "Use POST /api/articles/{id}/edit to propose a revision, which keeps the live version up until an Admin approves.";

    /// <summary>
    /// Returns the refusal message when <paramref name="status"/> is out of bounds for this
    /// caller, or null when the write may proceed.
    ///
    /// Refusing beats silently downgrading the status: an author who asked to publish should
    /// be told the request was denied, not left to discover a different status later.
    /// </summary>
    private static string? StatusRefusal(FunctionContext context, string status) =>
        context.IsAdmin() || AuthorSettableStatuses.Contains(status)
            ? null
            : $"Only an Admin can set status '{status}'. Submit the article for review instead, "
              + "then ask an Admin to publish it via POST /api/articles/{id}/publish.";

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
        return await Ok(req, VisibleInReview(articles, context));
    }

    /// <summary>
    /// In-review items the caller may act on: everything for an Admin, own work only
    /// for a Contributor. Filtering here rather than in the browser — the client-side
    /// filter in EditorView is a display convenience, not the boundary.
    /// </summary>
    internal static IReadOnlyList<Article> VisibleInReview(
        IReadOnlyList<Article> items, FunctionContext context) =>
        context.IsAdmin()
            ? items
            : context.GetUserOid() is { Length: > 0 } oid
                ? items.Where(a => a.AuthorId == oid).ToList()
                : [];

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

    [Function("CreateArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.create", tags: new[] { "articles" }, Summary = "Create article", Description = "Creates a new article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Article), Description = "Created article")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Non-Admin caller asked for a status only an Admin may set")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;

        if (StatusRefusal(context, input!.Status) is { } refusal)
            return await Forbidden(req, refusal);

        input.Body = sanitizer.Sanitize(input.Body);
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
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Non-Admin caller asked for an Admin-only status, or targeted a published article")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "articles/{id}")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;

        if (StatusRefusal(context, input!.Status) is { } refusal)
            return await Forbidden(req, refusal);

        input.Body = sanitizer.Sanitize(input.Body);
        try
        {
            // Live content is Admin-only. Contributors revise a published article through
            // POST /api/articles/{id}/edit, which drafts the change instead of overwriting it.
            if (!context.IsAdmin() && await repo.GetArticleAsync(id, PublishedStatus, ct) is not null)
                return await Forbidden(req, PublishedReplaceRefusal);

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
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Draft), Description = "A new draft revision")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "An in-progress revision this author already had, returned untouched")]
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
            // Admins may revise anything; a Contributor only their own work. Checked
            // here rather than in the UI alone — the endpoint is reachable directly.
            var published = await repo.GetArticleAsync(id, PublishedStatus, ct);
            if (published is null) return await NotFound(req, "Published article not found.");
            if (!ArticleVisibility.MayEdit(published, context))
                return await Forbidden(req, EditOwnershipRefusal);

            var (draft, resumed) = await repo.CreateRevisionDraftAsync(id, editorOid, editorName, ct);
            // 200 when we handed back a revision this author already had on the go, 201 when
            // one was minted. The client sends them to the editor in the first case rather
            // than opening a second window onto work already in progress.
            return resumed
                ? await Ok(req, draft, draft.Etag)
                : await Created(req, draft, draft.Etag);
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
            var published = await repo.GetArticleAsync(id, PublishedStatus, ct);
            if (published is null) return await NotFound(req, "Published article not found.");
            if (!ArticleVisibility.MayEdit(published, context))
                return await Forbidden(req, DeletionOwnershipRefusal);

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
    /// The author pulls their own submission back out of review so they can keep working
    /// on it. Same transition as Revise — the in-review article becomes a draft again,
    /// keeping its body and any ReplacesArticleId — but self-service and with no feedback
    /// note, because there is no reviewer telling them what to change.
    ///
    /// Deliberately author-only, with no Admin bypass: an Admin acting on someone else's
    /// submission should use POST /articles/{id}/revise, which requires feedback and so
    /// leaves the author a reason. Silently yanking someone's work back is not a thing we
    /// want to be easy.
    /// </summary>
    [Function("WithdrawArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "articles.withdraw", tags: new[] { "articles" }, Summary = "Withdraw from review", Description = "Returns the caller's own in-review article or blog post to their drafts so they can edit it again.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "In-review article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "The submission, back as an editable draft")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Not the author")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Not awaiting review")]
    public async Task<HttpResponseData> Withdraw(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles/{id}/withdraw")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var callerOid = context.GetUserOid();
        if (callerOid is null) return await BadRequest(req, "Token missing oid claim.");

        try
        {
            var inReview = await repo.GetArticleAsync(id, InReviewStatus, ct);
            if (inReview is null) return await NotFound(req, "No submission awaiting review with that id.");

            // Author only — not MayEdit. An Admin who did not write it has /revise.
            if (inReview.AuthorId is not { Length: > 0 } author || author != callerOid)
                return await Forbidden(req, WithdrawOwnershipRefusal);

            var draft = await repo.ReviseArticleAsync(id, feedback: null, ct);
            return await Ok(req, draft, draft.Etag);
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
