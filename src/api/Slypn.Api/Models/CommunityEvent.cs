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
    string? SignupUrl,
    string? CreatedBy     = null,
    string? CreatedByName = null)
{
    /// <summary>Partition key for the events container — "yyyy-MM" of StartsAt (UTC).</summary>
    public string YearMonth => StartsAt.UtcDateTime.ToString("yyyy-MM");

    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }

    /// <summary>Earlier adjacent event. Populated only on single-item detail responses.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EventNeighbour? Prev { get; init; }

    /// <summary>Later adjacent event. Populated only on single-item detail responses.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EventNeighbour? Next { get; init; }
}
