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

public sealed class EventsFunctions(IContentRepository repo, ILogger<EventsFunctions> log)
{
    [Function("GetEvents")]
    [OpenApiOperation(operationId: "events.list", tags: new[] { "events" }, Summary = "List events", Description = "Returns events with an optional upcoming filter.")]
    [OpenApiParameter(name: "upcoming", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Set to true to return only upcoming events.")]
    public async Task<HttpResponseData> GetEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events")] HttpRequestData req,
        CancellationToken ct)
    {
        var upcoming = string.Equals(QueryParam(req, "upcoming"), "true", StringComparison.OrdinalIgnoreCase);
        var events = await repo.ListEventsAsync(upcoming, ct);
        return await Ok(req, events);
    }

    [Function("CreateEvent")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "events.create", tags: new[] { "events" }, Summary = "Create event", Description = "Creates a new event.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(EventInput), Required = true, Description = "Event payload.")]
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
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "events.replace", tags: new[] { "events" }, Summary = "Replace event", Description = "Replaces an existing event.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Event id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(EventInput), Required = true, Description = "Event payload.")]
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
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "events.delete", tags: new[] { "events" }, Summary = "Delete event", Description = "Deletes an event using its id and partition key yearMonth.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Event id.")]
    [OpenApiParameter(name: "yearMonth", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Event partition key in YYYY-MM format.")]
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
