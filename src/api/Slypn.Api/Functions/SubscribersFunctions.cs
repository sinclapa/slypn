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
    public Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscribers")] HttpRequestData req,
        CancellationToken ct)
        => WithStorageAsync(req, repo, log, async () => await Ok(req, await repo.ListSubscribersAsync(ct)));

    [Function("DeleteSubscriber")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "subscribers.delete", tags: new[] { "subscribers" }, Summary = "Delete subscriber", Description = "Removes an address from the newsletter subscriber list.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Subscriber id.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
    public Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "subscribers/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
        => WithStorageAsync(req, repo, log, async () =>
        {
            await repo.DeleteSubscriberAsync(id, IfMatch(req), ct);
            return NoContent(req);
        });

    /// <summary>
    /// Public anonymous newsletter subscribe. It creates a subscriber, so it sits on
    /// POST /subscribers beside the admin list and delete rather than under a verb of its
    /// own. An anonymous POST next to an Admin-gated GET on one path is deliberate: the
    /// auth belongs to the operation, not to the resource.
    ///
    /// Writes to the <c>subscribers</c> table, which is separate from <c>members</c> on
    /// purpose: subscribing is anonymous, so a subscriber row must never be mistaken for
    /// evidence that someone was invited. Dedupe comes from the row key being derived from
    /// the email, so repeat subscribes upsert the same row.
    /// </summary>
    [Function("SubscribeToNewsletter")]
    [OpenApiOperation(operationId: "subscribers.create", tags: new[] { "subscribers" }, Summary = "Subscribe to newsletter", Description = "Subscribes an email address to the newsletter.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SubscribeInput), Required = true, Description = "Subscription payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Subscription result")]
    public async Task<HttpResponseData> Subscribe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscribers")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var (input, err) = await ReadValidatedAsync<SubscribeInput>(req, ct);
        if (err is not null) return err;

        var email = input!.Email.Trim().ToLowerInvariant();

        Subscriber? existing;
        try { existing = await repo.GetSubscriberByEmailAsync(email, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }

        var displayName = input.DisplayName?.Trim();
        var record = new Subscriber(
            Id:           Subscriber.KeyFor(email),
            Email:        email,
            DisplayName:  string.IsNullOrWhiteSpace(displayName) ? existing?.DisplayName ?? email : displayName,
            // Keep the date they first signed up rather than resetting it on every resubmit.
            SubscribedAt: existing?.SubscribedAt ?? DateTime.UtcNow);

        try
        {
            var saved = await repo.UpsertSubscriberAsync(record, existing?.Etag, ct);
            return await Created(req, new { saved.Email });
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
