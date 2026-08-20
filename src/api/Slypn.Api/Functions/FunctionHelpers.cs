using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Web;
using Azure;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

internal static class FunctionHelpers
{
    /// <summary>Article partition keys. Public list endpoints are pinned to
    /// <see cref="PublishedStatus"/>; anything else needs a role-gated route.</summary>
    public const string PublishedStatus = "published";
    public const string InReviewStatus  = "in-review";

    public static async Task<(T? Value, HttpResponseData? Error)> ReadValidatedAsync<T>(
        HttpRequestData req, CancellationToken ct) where T : class
    {
        T? body;
        try
        {
            body = await req.ReadFromJsonAsync<T>(ct);
        }
        catch (Exception ex)
        {
            return (null, await BadRequest(req, $"Invalid JSON: {ex.Message}"));
        }
        if (body is null) return (null, await BadRequest(req, "Empty request body."));

        var ctx = new ValidationContext(body);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(body, ctx, errors, validateAllProperties: true))
        {
            var msg = string.Join("; ", errors.Select(e => e.ErrorMessage));
            return (null, await BadRequest(req, msg));
        }
        return (body, null);
    }

    public static string? IfMatch(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("If-Match", out var vals)) return null;
        // Strip the RFC 7232 surrounding quotes before handing to Cosmos.
        return vals.FirstOrDefault()?.Trim().Trim('"');
    }

    public static string? QueryParam(HttpRequestData req, string name) =>
        HttpUtility.ParseQueryString(req.Url.Query)[name];

    /// <summary>RFC 7232: ETag values must be wrapped in double quotes.</summary>
    private static string QuoteEtag(string etag)
    {
        var clean = etag.Trim().Trim('"');
        return $"\"{clean}\"";
    }

    public static async Task<HttpResponseData> Ok<T>(HttpRequestData req, T value, string? etag = null)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        if (!string.IsNullOrEmpty(etag)) resp.Headers.Add("ETag", QuoteEtag(etag));
        await resp.WriteAsJsonAsync(value);
        return resp;
    }

    public static async Task<HttpResponseData> Created<T>(HttpRequestData req, T value, string? etag = null)
    {
        var resp = req.CreateResponse(HttpStatusCode.Created);
        if (!string.IsNullOrEmpty(etag)) resp.Headers.Add("ETag", QuoteEtag(etag));
        await resp.WriteAsJsonAsync(value);
        return resp;
    }

    public static HttpResponseData NoContent(HttpRequestData req) =>
        req.CreateResponse(HttpStatusCode.NoContent);

    public static async Task<HttpResponseData> NotFound(HttpRequestData req, string message = "Not found.")
        => await Reject(req, HttpStatusCode.NotFound, message);

    public static async Task<HttpResponseData> Forbidden(HttpRequestData req, string message = "Forbidden.")
        => await Reject(req, HttpStatusCode.Forbidden, message);

    public static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        await resp.WriteStringAsync(message);
        return resp;
    }

    public static async Task<HttpResponseData> WritesDisabled(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
        await resp.WriteStringAsync(
            "Writes require configured storage. Wire Azurite via scripts/start.ps1 or supply Storage__ConnectionString.");
        return resp;
    }

    public static async Task<HttpResponseData> MapStorageException(
        HttpRequestData req, RequestFailedException ex, ILogger? log = null)
    {
        log?.LogWarning(ex, "Storage write failed with status {Status}", ex.Status);
        return ex.Status switch
        {
            (int)HttpStatusCode.PreconditionFailed => await Reject(req, HttpStatusCode.PreconditionFailed,
                "ETag mismatch — refetch and retry."),
            (int)HttpStatusCode.NotFound           => await Reject(req, HttpStatusCode.NotFound,
                "Item not found."),
            (int)HttpStatusCode.Conflict           => await Reject(req, HttpStatusCode.Conflict,
                "An item with this id already exists."),
            _ => await Reject(req, HttpStatusCode.InternalServerError,
                $"Storage error: {ex.Message}"),
        };
    }

    private static async Task<HttpResponseData> Reject(HttpRequestData req, HttpStatusCode code, string message)
    {
        var resp = req.CreateResponse(code);
        await resp.WriteStringAsync(message);
        return resp;
    }
}
