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

public sealed class ResourcesFunctions(IContentRepository repo, ILogger<ResourcesFunctions> log)
{
    [Function("GetResources")]
    [OpenApiOperation(operationId: "resources.list", tags: new[] { "resources" }, Summary = "List resources", Description = "Returns all resources.")]
    public async Task<HttpResponseData> GetResources(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "resources")] HttpRequestData req,
        CancellationToken ct)
    {
        var items = await repo.ListResourcesAsync(ct);
        return await Ok(req, items);
    }

    [Function("CreateResource")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "resources.create", tags: new[] { "resources" }, Summary = "Create resource", Description = "Creates a new resource.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ResourceInput), Required = true, Description = "Resource payload.")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("ReplaceResource")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "resources.replace", tags: new[] { "resources" }, Summary = "Replace resource", Description = "Replaces an existing resource.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Resource id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(ResourceInput), Required = true, Description = "Resource payload.")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteResource")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "resources.delete", tags: new[] { "resources" }, Summary = "Delete resource", Description = "Deletes a resource using its id and partition key category.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Resource id.")]
    [OpenApiParameter(name: "category", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Resource partition key category.")]
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
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
