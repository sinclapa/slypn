using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class EventsFunctions(IContentRepository repo, ILogger<EventsFunctions> log)
{
    [Function("GetEvents")]
    public async Task<HttpResponseData> GetEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events")] HttpRequestData req,
        CancellationToken ct)
    {
        var upcoming = string.Equals(QueryParam(req, "upcoming"), "true", StringComparison.OrdinalIgnoreCase);
        var events = await repo.ListEventsAsync(upcoming, ct);
        return await Ok(req, events);
    }

    [Function("CreateEvent")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "events")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<EventInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var ev = await repo.CreateEventAsync(input!, ct);
            return await Created(req, ev, ev.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("ReplaceEvent")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "events/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<EventInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var ev = await repo.ReplaceEventAsync(id, input!, IfMatch(req), ct);
            return await Ok(req, ev, ev.Etag);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("DeleteEvent")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "events/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var yearMonth = QueryParam(req, "yearMonth");
        if (string.IsNullOrWhiteSpace(yearMonth))
            return await BadRequest(req, "DELETE /api/events/{id} requires ?yearMonth=YYYY-MM.");
        try
        {
            await repo.DeleteEventAsync(id, yearMonth, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }
}
