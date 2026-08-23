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
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

/// <summary>
/// Admin view of the newsletter subscriber list. Subscribers live in their own table, so these
/// are separate from <see cref="MembersFunctions"/> — a subscriber has no roles and cannot sign in,
/// and removing one has none of the Entra side effects that removing a member does.
/// </summary>
public sealed class SubscribersFunctions(
    IContentRepository repo,
    ILogger<SubscribersFunctions> log)
{
    [Function("ListSubscribers")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "subscribers.list", tags: new[] { "subscribers" }, Summary = "List subscribers", Description = "Returns all newsletter subscribers, newest first.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Subscriber[]), Description = "List of subscribers")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscribers")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        try
        {
            var subscribers = await repo.ListSubscribersAsync(ct);
            return await Ok(req, subscribers);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteSubscriber")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "subscribers.delete", tags: new[] { "subscribers" }, Summary = "Delete subscriber", Description = "Removes an address from the newsletter subscriber list.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Subscriber id.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "subscribers/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        try
        {
            await repo.DeleteSubscriberAsync(id, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
