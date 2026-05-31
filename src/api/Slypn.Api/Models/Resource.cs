using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record Resource(
    string Id,
    string Title,
    string Description,
    string Url,
    string Category)
{
    [JsonPropertyName("_etag")]
    public string? Etag { get; init; }
}
