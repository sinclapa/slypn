using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record Article(
    string Id,
    string Slug,
    string Title,
    string Summary,
    string Body,
    string Author,
    DateTime PublishedAt,
    int ReadingMinutes,
    string Category,
    string Status = "published")
{
    /// <summary>"article" or "blog". Defaults to "article" for rows that predate the field.</summary>
    public string Type { get; init; } = "article";

    /// <summary>Author's Entra oid — set on submit; carries through workflow transitions.
    /// Stripped from responses an anonymous caller can reach (see ArticleVisibility.ForPublic):
    /// it is an internal identifier, not public data. Omitted entirely when null, so the
    /// key itself does not appear on a public payload; a null round-trips to null either
    /// way, so storage is unaffected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthorId { get; init; }

    /// <summary>Whether the calling principal may open this item in the editor — an Admin, or
    /// the Contributor who authored it. Computed per request, never persisted: the conditional
    /// ignore keeps it out of the stored Json column, which is serialised from this same record.
    /// Null (and omitted) on responses where it was not computed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CanEdit { get; init; }

    /// <summary>Set when an admin rejects an in-review article. Null otherwise.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>On an in-review revision, the id of the published article it will replace on
    /// approval. Null for brand-new content.</summary>
    public string? ReplacesArticleId { get; init; }

    /// <summary>Set on a published article when a contributor requests its deletion (pending
    /// admin approval). The article stays live until an admin approves the deletion.</summary>
    public string? DeletionRequestedBy { get; init; }

    public DateTime? DeletionRequestedAt { get; init; }

    /// <summary>Cosmos optimistic-concurrency token. Echoed in the ETag HTTP header for clients.</summary>
    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }

    /// <summary>Older adjacent article. Populated only on single-item detail responses.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArticleNeighbour? Prev { get; init; }

    /// <summary>Newer adjacent article. Populated only on single-item detail responses.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArticleNeighbour? Next { get; init; }
}
