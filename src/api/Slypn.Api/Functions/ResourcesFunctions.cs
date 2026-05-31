using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class ResourcesFunctions(IContentRepository repo, ILogger<ResourcesFunctions> log)
{
    [Function("GetResources")]
    public async Task<HttpResponseData> GetResources(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "resources")] HttpRequestData req,
        CancellationToken ct)
    {
        var items = await repo.ListResourcesAsync(ct);
        return await Ok(req, items);
    }

    [Function("CreateResource")]
    [RequireRole("Admin")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "resources")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ResourceInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var r = await repo.CreateResourceAsync(input!, ct);
            return await Created(req, r, r.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("ReplaceResource")]
    [RequireRole("Admin")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "resources/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<ResourceInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var r = await repo.ReplaceResourceAsync(id, input!, IfMatch(req), ct);
            return await Ok(req, r, r.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("DeleteResource")]
    [RequireRole("Admin")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "resources/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var category = QueryParam(req, "category");
        if (string.IsNullOrWhiteSpace(category))
            return await BadRequest(req, "DELETE /api/resources/{id} requires ?category=<partitionKey>.");
        try
        {
            await repo.DeleteResourceAsync(id, category, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }
}
