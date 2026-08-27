using System.Net;
using Azure;
using HttpMultipartParser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class NewslettersFunctions(
    IContentRepository repo,
    ILogger<NewslettersFunctions> log,
    IOptions<StorageOptions> storage)
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

    [Function("GetNewsletterFile")]
    [OpenApiOperation(operationId: "newsletters.file", tags: new[] { "newsletters" }, Summary = "Download newsletter file", Description = "Streams the attached issue file (PDF/DOCX) for a newsletter.")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Newsletter id.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/octet-stream", bodyType: typeof(byte[]), Description = "The newsletter file")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "No file for this newsletter")]
    public async Task<HttpResponseData> GetFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "newsletters/{id}/file")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        var file = await repo.OpenNewsletterFileAsync(id, ct);
        if (file is null) return await NotFound(req, "No file for this newsletter.");

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", file.ContentType);
        resp.Headers.Add("Content-Disposition", $"attachment; filename=\"{DownloadName(id, file.ContentType)}\"");
        await using (file.Content)
        {
            await file.Content.CopyToAsync(resp.Body, ct);
        }
        return resp;
    }

    /// <summary>Clean, canonical download name, e.g. newsletter-2020-11 + application/pdf -> SLYPN-Newsletter-2020-11.pdf.</summary>
    private static string DownloadName(string id, string contentType)
    {
        var stamp = id.StartsWith("newsletter-", StringComparison.OrdinalIgnoreCase)
            ? id["newsletter-".Length..]
            : id;
        var ext = contentType switch
        {
            "application/pdf" => "pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/msword" => "doc",
            _ => "bin",
        };
        return $"SLYPN-Newsletter-{stamp}.{ext}";
    }

    private static readonly HashSet<string> AllowedFileContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
    };

    [Function("UploadNewsletterFile")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "newsletters.uploadFile", tags: new[] { "newsletters" }, Summary = "Upload newsletter file", Description = "Uploads/replaces the attached issue file (PDF/DOCX) for a newsletter.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Newsletter id.")]
    [OpenApiRequestBody(contentType: "multipart/form-data", bodyType: typeof(object), Required = true, Description = "Multipart form body with a single file part named file.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Newsletter), Description = "Updated newsletter")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Newsletter not found")]
    public async Task<HttpResponseData> UploadFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "newsletters/{id}/file")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var contentType = req.Headers.TryGetValues("Content-Type", out var ctHeader)
            ? ctHeader.FirstOrDefault()
            : null;
        if (contentType is null || !contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return await Reject(req, HttpStatusCode.UnsupportedMediaType,
                "Expected multipart/form-data with a 'file' part.");
        }

        // Before ParseAsync, which would otherwise buffer the whole body into
        // memory and only then let the content-type allowlist reject it.
        if (UploadLimits.Validate(req, storage.Value.MaxNewsletterFileBytes) is { } refusal)
        {
            return await Reject(req, refusal.Code, refusal.Message);
        }

        MultipartFormDataParser parsed;
        try
        {
            parsed = await MultipartFormDataParser.ParseAsync(req.Body, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            return await BadRequest(req, $"Could not parse multipart body: {ex.Message}");
        }

        var file = parsed.Files.FirstOrDefault(f => f.Name == "file")
            ?? (parsed.Files.Count > 0 ? parsed.Files[0] : null);
        if (file is null)
        {
            return await BadRequest(req, "No file part in the upload.");
        }

        if (!AllowedFileContentTypes.Contains(file.ContentType))
        {
            return await Reject(req, HttpStatusCode.UnsupportedMediaType,
                $"Content type '{file.ContentType}' not allowed. Allowed: {string.Join(", ", AllowedFileContentTypes)}.");
        }

        try
        {
            var n = await repo.PutNewsletterFileAsync(id, file.Data, file.ContentType, file.FileName, IfMatch(req), ct);
            return await Ok(req, n, n.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    private static async Task<HttpResponseData> Reject(HttpRequestData req, HttpStatusCode code, string message)
    {
        var resp = req.CreateResponse(code);
        await resp.WriteStringAsync(message);
        return resp;
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
    [OpenApiOperation(operationId: "newsletters.delete", tags: new[] { "newsletters" }, Summary = "Delete newsletter", Description = "Deletes a newsletter by id.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Newsletter id.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "newsletters/{id}")] HttpRequestData req,
        string id, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        try
        {
            await repo.DeleteNewsletterAsync(id, IfMatch(req), ct);
            return NoContent(req);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }
}
