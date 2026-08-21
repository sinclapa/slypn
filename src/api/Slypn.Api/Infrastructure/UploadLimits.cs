using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Content-Length gate for multipart uploads.
/// <para>
/// The upload endpoints parse the whole body into memory, and the content-type
/// allowlist only applies once parsing has finished — so without this an
/// oversized body is fully buffered before anything can reject it. Both
/// endpoints are role-gated, making this a cost and availability concern on a
/// Free-tier plan rather than an external attack, but there was no upper bound
/// at all.
/// </para>
/// </summary>
internal static class UploadLimits
{
    /// <summary>
    /// Validates the declared length before the body is read. Returns
    /// <c>null</c> when the request is acceptable, otherwise the status and
    /// message to reject it with.
    /// </summary>
    /// <remarks>
    /// A missing or unparseable Content-Length is refused rather than waved
    /// through: allowing it would leave exactly the unbounded path this exists
    /// to close. Callers upload with a known length, so this costs them nothing.
    /// </remarks>
    internal static (HttpStatusCode Code, string Message)? Validate(HttpRequestData req, long maxBytes)
    {
        if (!req.Headers.TryGetValues("Content-Length", out var values)
            || !long.TryParse(values.FirstOrDefault(), out var declared))
        {
            return (HttpStatusCode.LengthRequired,
                "A Content-Length header is required so the upload can be size-checked before it is read.");
        }

        if (declared > maxBytes)
        {
            return (HttpStatusCode.RequestEntityTooLarge,
                $"Upload is {declared} bytes; the limit is {maxBytes} bytes.");
        }

        return null;
    }
}
