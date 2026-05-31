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
    IReadOnlyList<string> Tags,
    string Status = "published")
{
    /// <summary>Author's Entra oid — set on submit; carries through workflow transitions.</summary>
    public string? AuthorId { get; init; }

    /// <summary>Set when an admin rejects an in-review article. Null otherwise.</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Cosmos optimistic-concurrency token. Echoed in the ETag HTTP header for clients.</summary>
    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }
}
