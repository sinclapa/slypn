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

public sealed class DraftsFunctions(IContentRepository repo, IHtmlSanitizer sanitizer, ILogger<DraftsFunctions> log)
{
    [Function("ListMyDrafts")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "drafts.listMine", tags: new[] { "drafts" }, Summary = "List my drafts", Description = "Returns drafts for the authenticated author.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft[]), Description = "List of drafts")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "drafts")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var authorId = context.GetUserOid();
        if (authorId is null) return await BadRequest(req, "Token missing oid claim.");
        try
        {
            var drafts = await repo.ListDraftsByAuthorAsync(authorId, ct);
            return await Ok(req, drafts);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("GetDraft")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "drafts.get", tags: new[] { "drafts" }, Summary = "Get draft", Description = "Returns a draft owned by the authenticated author.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Draft id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "Draft")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Not found")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "drafts/{id}")] HttpRequestData req,
        FunctionContext context,
        string id,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var authorId = context.GetUserOid();
        if (authorId is null) return await BadRequest(req, "Token missing oid claim.");
        try
        {
            var draft = await repo.GetDraftAsync(id, authorId, ct);
            if (draft is null) return req.CreateResponse(System.Net.HttpStatusCode.NotFound);
            return await Ok(req, draft, draft.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Upsert — the client owns the id. Authors can only write to drafts
    /// under their own oid; the partition key is forced from the JWT.
    /// </summary>
    [Function("UpsertDraft")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "drafts.upsert", tags: new[] { "drafts" }, Summary = "Upsert draft", Description = "Creates or replaces a draft for the authenticated author.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Draft id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(DraftInput), Required = true, Description = "Draft payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Draft), Description = "Saved draft")]
    public async Task<HttpResponseData> Upsert(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "drafts/{id}")] HttpRequestData req,
        FunctionContext context,
        string id,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var authorId = context.GetUserOid();
        if (authorId is null) return await BadRequest(req, "Token missing oid claim.");

        var (input, err) = await ReadValidatedAsync<DraftInput>(req, ct);
        if (err is not null) return err;

        var principal = context.GetPrincipal();
        var authorName = principal?.FindFirst("name")?.Value ?? "Member";

        var now = DateTime.UtcNow;
        Draft? existing;
        try { existing = await repo.GetDraftAsync(id, authorId, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }

        var draft = new Draft(
            Id:               id,
            AuthorId:         authorId,
            AuthorName:       authorName,
            Type:             input!.Type,
            Title:            input.Title,
            Slug:             input.Slug,
            Summary:          input.Summary,
            Body:             sanitizer.Sanitize(input.Body),
            Category:         input.Category,
            Tags:             input.Tags,
            ReadingMinutes:   input.ReadingMinutes,
            CreatedAt:        existing?.CreatedAt ?? now,
            UpdatedAt:        now,
            RevisionFeedback: input.RevisionFeedback?.Trim() ?? existing?.RevisionFeedback,
            ReplacesArticleId: existing?.ReplacesArticleId);

        try
        {
            var saved = existing is null
                ? await repo.UpsertDraftAsync(draft, null, ct)
                : await repo.UpsertDraftAsync(draft, IfMatch(req), ct);
            return await Ok(req, saved, saved.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteDraft")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "drafts.delete", tags: new[] { "drafts" }, Summary = "Delete draft", Description = "Deletes a draft owned by the authenticated author.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Draft id.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "drafts/{id}")] HttpRequestData req,
        FunctionContext context,
        string id,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var authorId = context.GetUserOid();
        if (authorId is null) return await BadRequest(req, "Token missing oid claim.");
        try
        {
            await repo.DeleteDraftAsync(id, authorId, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Submit the author's own draft for admin review. Promotes it to an
    /// in-review article with the draft's id (so re-submits are idempotent)
    /// and removes the draft.
    /// </summary>
    [Function("SubmitDraft")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "drafts.submit", tags: new[] { "drafts" }, Summary = "Submit draft", Description = "Submits a draft for review and promotes it to an article.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Draft id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Article), Description = "Submitted article")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "drafts/{id}/submit")] HttpRequestData req,
        FunctionContext context,
        string id,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var authorId = context.GetUserOid();
        if (authorId is null) return await BadRequest(req, "Token missing oid claim.");

        try
        {
            var article = await repo.SubmitDraftAsync(id, authorId, ct);
            return await Created(req, article, article.Etag);
        }
        catch (InvalidOperationException ex)
        {
            return await BadRequest(req, ex.Message);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
