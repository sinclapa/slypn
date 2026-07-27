namespace Slypn.Api.Services;

/// <summary>An open, binary blob download: the content stream plus its stored content type.</summary>
public sealed record BlobDownload(Stream Content, string ContentType);

/// <summary>
/// Stores large article/draft HTML bodies as blobs (one per content id), keeping
/// them out of Table Storage where a single property caps at 64 KB. Blobs are
/// addressed by a kind prefix (e.g. "articles", "drafts") plus the content id, so
/// status transitions that keep the id stable never move the blob.
/// </summary>
public interface IContentBodyStore
{
    bool IsConfigured { get; }

    /// <summary>Write (overwrite) the HTML body for the given content.</summary>
    Task PutAsync(string prefix, string id, string html, CancellationToken ct);

    /// <summary>Read the HTML body, or empty string if no blob exists.</summary>
    Task<string> GetAsync(string prefix, string id, CancellationToken ct);

    /// <summary>
    /// Open a binary file blob (e.g. a newsletter PDF/DOCX) for streaming, or null
    /// if no blob exists. The caller owns the returned stream and must dispose it.
    /// </summary>
    Task<BlobDownload?> TryOpenFileAsync(string prefix, string id, CancellationToken ct);

    /// <summary>Write (overwrite) a binary file blob (e.g. a newsletter PDF/DOCX).</summary>
    Task PutFileAsync(string prefix, string id, Stream content, string contentType, CancellationToken ct);

    /// <summary>Delete the body blob. No-op if it doesn't exist.</summary>
    Task DeleteAsync(string prefix, string id, CancellationToken ct);
}
