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
/// Everything that changes a piece of content, whatever type it is.
///
/// Blog posts are Article rows with Type == "blog", so create, replace, delete and every workflow
/// transition are the same operation either way — which is why they live on /api/content rather than
/// under /api/articles, where the route said "article" while the handler meant "content". The reads
/// stay split by type, in <see cref="ArticlesFunctions"/> and <see cref="BlogFunctions"/>, because a
/// reader genuinely is asking for one or the other.
/// </summary>
public sealed class ContentFunctions(IContentRepository repo, IHtmlSanitizer sanitizer, ILogger<ContentFunctions> log)
{
    private static readonly HashSet<string> AuthorSettableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "draft", InReviewStatus };

    private const string EditOwnershipRefusal =
        "You can only edit your own published content. Ask an Admin to make the change, "
        + "or to reassign the item to you.";

    private const string WithdrawOwnershipRefusal =
        "You can only withdraw your own submissions. An Admin returns someone else's work "
        + "with POST /api/content/{id}/revise, which carries feedback.";

    private const string MissingTypeRefusal =
        "Creating content requires \"type\": \"article\" or \"blog\".";

    private const string DeletionOwnershipRefusal =
        "You can only request deletion of your own published content.";

    private const string PublishedReplaceRefusal =
        "This article is published — only an Admin can replace it. "
        + "Use POST /api/content/{id}/edit to propose a revision, which keeps the live version up until an Admin approves.";

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
              + "then ask an Admin to publish it via POST /api/content/{id}/publish.";

    /// <summary>
    /// Public article list. Always published — the caller cannot widen this.
    ///
    /// The status filter used to come from the query string, which meant an
    /// anonymous caller could read in-review submissions with ?status=, and a
    /// bare GET (no parameter at all) returned EVERY partition because a null
    /// status means "no filter" in the repository. Unpublished work is now only
    /// reachable through GetPendingArticles below, which carries a role gate.
    /// </summary>

    private async Task<Article?> FindOwnRevisionInReviewAsync(string articleId, string editorOid, CancellationToken ct)
    {
        var articles = await repo.ListArticlesAsync(InReviewStatus, ct);
        var blogs    = await repo.ListBlogPostsAsync(InReviewStatus, ct);
        return articles.Concat(blogs).FirstOrDefault(a =>
            a.ReplacesArticleId == articleId && a.AuthorId == editorOid);
    }
    /// <summary>
    /// Request deletion of a published article (pending admin approval). The article stays
    /// live until an admin approves the deletion via DELETE.
    /// </summary>

    /// <summary>
    /// The two failures every mutation here shares. The repository throws
    /// InvalidOperationException when the request does not make sense for the item's current
    /// state — not published, not in review, nothing to change — which is the caller's problem
    /// to fix, so a 400 rather than a 500. Storage failures map to the status they deserve.
    ///
    /// The SupportsWrites guard deliberately stays at each call site, so it keeps running
    /// before anything else the handler does.
    /// </summary>
    private async Task<HttpResponseData> MapContentErrors(
        HttpRequestData req, Func<Task<HttpResponseData>> operation)
    {
        try { return await operation(); }
        catch (InvalidOperationException ex) { return await BadRequest(req, ex.Message); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("CreateArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "content.create", tags: new[] { "content" }, Summary = "Create article", Description = "Creates a new article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Article), Description = "Created article")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Non-Admin caller asked for a status only an Admin may set")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;

        // Required here rather than on the model, because it is required on create and
        // optional on replace — the same shape as Delete's ?status= check below. Refusing
        // beats defaulting: an implicit "article" is precisely how a blog post used to get
        // converted without anyone asking.
        if (string.IsNullOrWhiteSpace(input!.Type))
            return await BadRequest(req, MissingTypeRefusal);

        if (StatusRefusal(context, input.Status) is { } refusal)
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
    [OpenApiOperation(operationId: "content.replace", tags: new[] { "content" }, Summary = "Replace article", Description = "Replaces an existing article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ArticleInput), Required = true, Description = "Article payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Updated article")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Non-Admin caller asked for an Admin-only status, or targeted a published article")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "content/{id}")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ArticleInput>(req, ct);
        if (err is not null) return err;

        if (StatusRefusal(context, input!.Status) is { } refusal)
            return await Forbidden(req, refusal);

        input.Body = sanitizer.Sanitize(input.Body);
        return await MapContentErrors(req, async () =>
        {
            // Live content is Admin-only. Contributors revise a published article through
            // POST /api/content/{id}/edit, which drafts the change instead of overwriting it.
            if (!context.IsAdmin() && await repo.GetArticleAsync(id, PublishedStatus, ct) is not null)
                return await Forbidden(req, PublishedReplaceRefusal);

            var article = await repo.ReplaceArticleAsync(id, input, IfMatch(req), ct);
            return await Ok(req, article, article.Etag);
        });
    }

    [Function("DeleteArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "content.delete", tags: new[] { "content" }, Summary = "Delete article", Description = "Deletes an article or blog post using its id and partition key status.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiParameter(name: "status", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Article partition key status.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "content/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var status = QueryParam(req, "status");
        if (string.IsNullOrWhiteSpace(status))
            return await BadRequest(req, "DELETE /api/content/{id} requires ?status=<partitionKey>.");
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
    [OpenApiOperation(operationId: "content.publish", tags: new[] { "content" }, Summary = "Publish article", Description = "Moves an article or blog post to published status.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Published article")]
    public async Task<HttpResponseData> Publish(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content/{id}/publish")] HttpRequestData req,
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
    [OpenApiOperation(operationId: "content.edit", tags: new[] { "content" }, Summary = "Edit published", Description = "Creates a draft revision of a published article or blog post for approval.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Published article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Draft), Description = "A new draft revision")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "An in-progress revision this author already had, returned untouched")]
    public async Task<HttpResponseData> Edit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content/{id}/edit")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var editorOid = context.GetUserOid();
        if (editorOid is null) return await BadRequest(req, "Token missing oid claim.");
        var editorName = context.GetUserName() ?? "Member";
        return await MapContentErrors(req, async () =>
        {
            // Admins may revise anything; a Contributor only their own work. Checked
            // here rather than in the UI alone — the endpoint is reachable directly.
            var published = await repo.GetArticleAsync(id, PublishedStatus, ct);
            if (published is null) return await NotFound(req, "Published article not found.");
            if (!ArticleVisibility.MayEdit(published, context))
                return await Forbidden(req, EditOwnershipRefusal);

            // Already submitted a revision of this item? There is nothing to edit until an
            // admin has dealt with it, and minting a second draft would put two competing
            // revisions of one article in the queue. Hand back the submission so the client
            // can show it — read-only — rather than opening a fresh editor.
            var awaitingReview = await FindOwnRevisionInReviewAsync(id, editorOid, ct);
            if (awaitingReview is not null) return await Ok(req, awaitingReview.ForPublic(context));

            var (draft, resumed) = await repo.CreateRevisionDraftAsync(id, editorOid, editorName, ct);
            // 200 when we handed back a revision this author already had on the go, 201 when
            // one was minted. The client sends them to the editor in the first case rather
            // than opening a second window onto work already in progress.
            return resumed
                ? await Ok(req, draft, draft.Etag)
                : await Created(req, draft, draft.Etag);
        });
    }


    /// <summary>The caller's own in-review revision of this article, if they have one.
    /// Checks both content types: a blog post is an Article row with Type == "blog", and
    /// ListArticlesAsync is filtered to articles, so it alone would miss blog revisions.</summary>

    [Function("RequestArticleDeletion")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "content.requestDeletion", tags: new[] { "content" }, Summary = "Request deletion", Description = "Flags a published article or blog post for deletion, pending admin approval.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Published article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Updated article")]
    public async Task<HttpResponseData> RequestDeletion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content/{id}/request-deletion")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var requesterOid = context.GetUserOid();
        if (requesterOid is null) return await BadRequest(req, "Token missing oid claim.");
        return await MapContentErrors(req, async () =>
        {
            var published = await repo.GetArticleAsync(id, PublishedStatus, ct);
            if (published is null) return await NotFound(req, "Published article not found.");
            if (!ArticleVisibility.MayEdit(published, context))
                return await Forbidden(req, DeletionOwnershipRefusal);

            var article = await repo.RequestArticleDeletionAsync(id, requesterOid, ct);
            return await Ok(req, article, article.Etag);
        });
    }

    /// <summary>Admin clears a pending deletion request, keeping the article published.</summary>

    [Function("CancelArticleDeletion")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "content.cancelDeletion", tags: new[] { "content" }, Summary = "Keep article", Description = "Clears a pending deletion request on a published article or blog post.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Published article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Article), Description = "Updated article")]
    public async Task<HttpResponseData> CancelDeletion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content/{id}/cancel-deletion")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        return await MapContentErrors(req, async () =>
        {
            var article = await repo.CancelArticleDeletionAsync(id, ct);
            return await Ok(req, article, article.Etag);
        });
    }

    /// <summary>
    /// The author pulls their own submission back out of review so they can keep working
    /// on it. Same transition as Revise — the in-review article becomes a draft again,
    /// keeping its body and any ReplacesArticleId — but self-service and with no feedback
    /// note, because there is no reviewer telling them what to change.
    ///
    /// Deliberately author-only, with no Admin bypass: an Admin acting on someone else's
    /// submission should use POST /api/content/{id}/revise, which requires feedback and so
    /// leaves the author a reason. Silently yanking someone's work back is not a thing we
    /// want to be easy.
    /// </summary>

    [Function("WithdrawArticle")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "content.withdraw", tags: new[] { "content" }, Summary = "Withdraw from review", Description = "Returns the caller's own in-review article or blog post to their drafts so they can edit it again.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "In-review article id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "The submission, back as an editable draft")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "Not the author")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Not awaiting review")]
    public async Task<HttpResponseData> Withdraw(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content/{id}/withdraw")] HttpRequestData req,
        FunctionContext context,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var callerOid = context.GetUserOid();
        if (callerOid is null) return await BadRequest(req, "Token missing oid claim.");

        return await MapContentErrors(req, async () =>
        {
            var inReview = await repo.GetArticleAsync(id, InReviewStatus, ct);
            if (inReview is null) return await NotFound(req, "No submission awaiting review with that id.");

            // Author only — not MayEdit. An Admin who did not write it has /revise.
            if (inReview.AuthorId is not { Length: > 0 } author || author != callerOid)
                return await Forbidden(req, WithdrawOwnershipRefusal);

            var draft = await repo.ReviseArticleAsync(id, feedback: null, ct);
            return await Ok(req, draft, draft.Etag);
        });
    }

    /// <summary>
    /// Admin returns an in-review article to the author as a draft with revision feedback.
    /// The in-review article is deleted and a draft is created so the author can edit and resubmit.
    /// </summary>

    [Function("ReviseArticle")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "content.revise", tags: new[] { "content" }, Summary = "Request revision", Description = "Returns an in-review article or blog post to the author as a draft with feedback.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Article id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(RejectionInput), Required = true, Description = "Revision feedback.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "Draft with revision feedback")]
    public async Task<HttpResponseData> Revise(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "content/{id}/revise")] HttpRequestData req,
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
