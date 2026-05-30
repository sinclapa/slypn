using System.Net;
using HttpMultipartParser;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class MediaFunctions(IBlobService blob)
{
    /// <summary>
    /// POST /api/media — multipart/form-data with a single "file" part.
    /// Returns { name, url } where `url` is a 15-minute read SAS.
    /// </summary>
    [Function("UploadMedia")]
    public async Task<HttpResponseData> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "media")] HttpRequestData req)
    {
        if (!blob.IsConfigured)
        {
            var unavailable = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            await unavailable.WriteStringAsync("BlobService is not configured.");
            return unavailable;
        }

        var contentType = req.Headers.TryGetValues("Content-Type", out var ct)
            ? ct.FirstOrDefault()
            : null;
        if (contentType is null || !contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return await Reject(req, HttpStatusCode.UnsupportedMediaType,
                "Expected multipart/form-data with a 'file' part.");
        }

        MultipartFormDataParser parsed;
        try
        {
            parsed = await MultipartFormDataParser.ParseAsync(req.Body);
        }
        catch (Exception ex)
        {
            return await Reject(req, HttpStatusCode.BadRequest, $"Could not parse multipart body: {ex.Message}");
        }

        var file = parsed.Files.FirstOrDefault(f => f.Name == "file") ?? parsed.Files.FirstOrDefault();
        if (file is null)
        {
            return await Reject(req, HttpStatusCode.BadRequest, "No file part in the upload.");
        }

        if (!blob.AllowedContentTypes.Contains(file.ContentType))
        {
            return await Reject(req, HttpStatusCode.UnsupportedMediaType,
                $"Content type '{file.ContentType}' not allowed. Allowed: {string.Join(", ", blob.AllowedContentTypes)}.");
        }

        var blobName = await blob.UploadMediaAsync(file.Data, file.ContentType, default);
        var readUrl  = blob.GetMediaReadUrl(blobName);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            name = blobName,
            url  = readUrl.ToString(),
        });
        return response;
    }

    private static async Task<HttpResponseData> Reject(HttpRequestData req, HttpStatusCode code, string message)
    {
        var resp = req.CreateResponse(code);
        await resp.WriteStringAsync(message);
        return resp;
    }
}
