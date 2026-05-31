using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class DraftsFunctions(IContentRepository repo, ILogger<DraftsFunctions> log)
{
    [Function("ListMyDrafts")]
    [RequireRole("Admin", "Contributor")]
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
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("GetDraft")]
    [RequireRole("Admin", "Contributor")]
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
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    /// <summary>
    /// Upsert — the client owns the id. Authors can only write to drafts
    /// under their own oid; the partition key is forced from the JWT.
    /// </summary>
    [Function("UpsertDraft")]
    [RequireRole("Admin", "Contributor")]
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
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }

        var draft = new Draft(
            Id:             id,
            AuthorId:       authorId,
            AuthorName:     authorName,
            Type:           input!.Type,
            Title:          input.Title,
            Slug:           input.Slug,
            Summary:        input.Summary,
            Body:           input.Body,
            Category:       input.Category,
            Tags:           input.Tags,
            ReadingMinutes: input.ReadingMinutes,
            CreatedAt:      existing?.CreatedAt ?? now,
            UpdatedAt:      now);

        try
        {
            var saved = existing is null
                ? await repo.UpsertDraftAsync(draft, null, ct)
                : await repo.UpsertDraftAsync(draft, IfMatch(req), ct);
            return await Ok(req, saved, saved.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("DeleteDraft")]
    [RequireRole("Admin", "Contributor")]
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
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }
}
