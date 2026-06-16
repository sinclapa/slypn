namespace Slypn.Api.Services;

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

    /// <summary>Delete the body blob. No-op if it doesn't exist.</summary>
    Task DeleteAsync(string prefix, string id, CancellationToken ct);
}
