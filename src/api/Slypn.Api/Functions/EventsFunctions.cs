using Azure;
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
    [Function("GetEvent")]
    [OpenApiOperation(operationId: "events.get", tags: new[] { "events" }, Summary = "Get event", Description = "Returns a single event by id.")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Event id.")]
    public async Task<HttpResponseData> GetEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        try
        {
            var ev = await repo.GetEventByIdAsync(id, ct);
            if (ev is null) return await NotFound(req, "Event not found.");
            return await Ok(req, ev, ev.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

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
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "events.create", tags: new[] { "events" }, Summary = "Create event", Description = "Creates a new event. Admins and Contributors may create events.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(EventInput), Required = true, Description = "Event payload.")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "events")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<EventInput>(req, ct);
        if (err is not null) return err;
        try
        {
            var ev = await repo.CreateEventAsync(
                input!,
                context.GetUserOid(),
                context.GetUserName(),
                ct);
            return await Created(req, ev, ev.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("ReplaceEvent")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "events.replace", tags: new[] { "events" }, Summary = "Replace event", Description = "Replaces an existing event. Admins may edit any event; Contributors may only edit their own.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Event id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(EventInput), Required = true, Description = "Event payload.")]
    public async Task<HttpResponseData> Replace(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "events/{id}")] HttpRequestData req,
        string id, FunctionContext context, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        var (input, err) = await ReadValidatedAsync<EventInput>(req, ct);
        if (err is not null) return err;

        var existing = await repo.GetEventByIdAsync(id, ct);
        if (existing is null) return await NotFound(req, "Event not found.");
        if (!context.IsAdmin() && existing.CreatedBy != context.GetUserOid())
            return await Forbidden(req, "You can only edit your own events.");

        try
        {
            var ev = await repo.ReplaceEventAsync(
                id, existing.YearMonth, input!,
                existing.CreatedBy, existing.CreatedByName,
                IfMatch(req), ct);
            return await Ok(req, ev, ev.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteEvent")]
    [RequireRole("Admin", "Contributor")]
    [OpenApiOperation(operationId: "events.delete", tags: new[] { "events" }, Summary = "Delete event", Description = "Deletes an event. Admins may delete any event; Contributors may only delete their own.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Event id.")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "events/{id}")] HttpRequestData req,
        string id, FunctionContext context, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var existing = await repo.GetEventByIdAsync(id, ct);
        if (existing is null) return await NotFound(req, "Event not found.");
        if (!context.IsAdmin() && existing.CreatedBy != context.GetUserOid())
            return await Forbidden(req, "You can only delete your own events.");

        try
        {
            await repo.DeleteEventAsync(id, existing.YearMonth, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
