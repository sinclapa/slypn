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

public sealed class NewslettersFunctions(IContentRepository repo, ILogger<NewslettersFunctions> log)
{
    [Function("GetNewsletters")]
    [OpenApiOperation(operationId: "newsletters.list", tags: new[] { "newsletters" }, Summary = "List newsletters", Description = "Returns all newsletters.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Newsletter[]), Description = "List of newsletters")]
    public async Task<HttpResponseData> GetNewsletters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "newsletters")] HttpRequestData req,
        CancellationToken ct)
    {
        var items = await repo.ListNewslettersAsync(ct);
        return await Ok(req, items);
    }

    [Function("CreateNewsletter")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "newsletters.create", tags: new[] { "newsletters" }, Summary = "Create newsletter", Description = "Creates a newsletter issue.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(NewsletterInput), Required = true, Description = "Newsletter payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Newsletter), Description = "Created newsletter")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("ReplaceNewsletter")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "newsletters.replace", tags: new[] { "newsletters" }, Summary = "Replace newsletter", Description = "Replaces an existing newsletter.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Newsletter id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(NewsletterInput), Required = true, Description = "Newsletter payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Newsletter), Description = "Updated newsletter")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteNewsletter")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "newsletters.delete", tags: new[] { "newsletters" }, Summary = "Delete newsletter", Description = "Deletes a newsletter using its id and partition key year.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Newsletter id.")]
    [OpenApiParameter(name: "year", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Newsletter partition key year.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    /// <summary>
    /// Public anonymous newsletter subscribe. Stores the email as a Member row
    /// with Status="subscribed" so we get free dedupe (UpsertMemberAsync is
    /// idempotent on a given id). No role assignment — subscribers aren't
    /// SLYPN members in the auth sense.
    /// </summary>
    [Function("SubscribeToNewsletter")]
    [OpenApiOperation(operationId: "newsletter.subscribe", tags: new[] { "newsletters" }, Summary = "Subscribe to newsletter", Description = "Subscribes an email address to the newsletter.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SubscribeInput), Required = true, Description = "Subscription payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Subscription result")]
    public async Task<HttpResponseData> Subscribe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "newsletter/subscribe")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var (input, err) = await ReadValidatedAsync<SubscribeInput>(req, ct);
        if (err is not null) return err;

        var email = input!.Email.Trim().ToLowerInvariant();

        Member? existing;
        try { existing = await repo.GetMemberByEmailAsync(email, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }

        var now = DateTime.UtcNow;
        var displayName = input.DisplayName?.Trim();

        var record = existing is null
            ? new Member(
                Id:          Guid.NewGuid().ToString("N"),
                Email:       email,
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? email : displayName!,
                Roles:       Array.Empty<string>(),
                Status:      "subscribed",
                InvitedAt:   now)
            : existing with
            {
                // Promote any earlier "invited" -> still "subscribed" but keep
                // their roles + oid if they're an actual member. For pure
                // subscribers (no roles, no oid), we just refresh the timestamp.
                Status      = existing.Roles.Count > 0 || existing.Oid is not null
                                ? existing.Status
                                : "subscribed",
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName!,
            };

        try
        {
            var saved = await repo.UpsertMemberAsync(record, existing?.Etag, ct);
            return await Created(req, new { saved.Email, saved.Status });
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
