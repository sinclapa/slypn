using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record Draft(
    string Id,
    string AuthorId,
    string AuthorName,
    string Type,        // "article" | "blog"
    string Title,
    string Slug,
    string Summary,
    string Body,
    string Category,
    int ReadingMinutes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? RevisionFeedback = null,
    string? ReplacesArticleId = null)
{
    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }
}
