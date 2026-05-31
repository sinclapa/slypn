using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record CommunityEvent(
    string Id,
    string Title,
    string Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    string Description,
    string? SignupUrl)
{
    /// <summary>Partition key for the events container — "yyyy-MM" of StartsAt (UTC).</summary>
    public string YearMonth => StartsAt.UtcDateTime.ToString("yyyy-MM");

    [JsonPropertyName("_etag")]
    public string? Etag { get; init; }
}
