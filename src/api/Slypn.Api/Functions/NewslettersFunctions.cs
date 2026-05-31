using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class NewslettersFunctions(IContentRepository repo, ILogger<NewslettersFunctions> log)
{
    [Function("GetNewsletters")]
    public async Task<HttpResponseData> GetNewsletters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "newsletters")] HttpRequestData req,
        CancellationToken ct)
    {
        var items = await repo.ListNewslettersAsync(ct);
        return await Ok(req, items);
    }

    [Function("CreateNewsletter")]
    [RequireRole("Admin")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "newsletters")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<NewsletterInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var n = await repo.CreateNewsletterAsync(input!, ct);
            return await Created(req, n, n.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("ReplaceNewsletter")]
    [RequireRole("Admin")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "newsletters/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<NewsletterInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var n = await repo.ReplaceNewsletterAsync(id, input!, IfMatch(req), ct);
            return await Ok(req, n, n.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("DeleteNewsletter")]
    [RequireRole("Admin")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "newsletters/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var year = QueryParam(req, "year");
        if (string.IsNullOrWhiteSpace(year))
            return await BadRequest(req, "DELETE /api/newsletters/{id} requires ?year=YYYY.");
        try
        {
            await repo.DeleteNewsletterAsync(id, year, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }
}
