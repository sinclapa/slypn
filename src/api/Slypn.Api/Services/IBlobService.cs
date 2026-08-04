namespace Slypn.Api.Services;

public interface IBlobService
{
    bool IsConfigured { get; }

    /// <summary>Allowed media MIME types (lowercase).</summary>
    IReadOnlySet<string> AllowedContentTypes { get; }

    /// <summary>
    /// Uploads a media blob. Caller is responsible for MIME validation
    /// (this method also validates as a safety net). Returns the blob name
    /// (a Guid-based identifier with an appropriate extension).
    /// </summary>
    Task<string> UploadMediaAsync(Stream content, string contentType, CancellationToken ct);

    /// <summary>
    /// Returns a short-lived (default 15 min) read URL for the given blob name.
    /// Shared-key SAS, permanently — Free-tier SWA has no managed identity to
    /// grant Blob Data Contributor to for a user-delegation SAS instead.
    /// </summary>
    Uri GetMediaReadUrl(string blobName, TimeSpan? validFor = null);
}
