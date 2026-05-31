using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record Newsletter(
    string Id,
    string Title,
    DateOnly IssueDate,
    string Summary,
    IReadOnlyList<string> Topics)
{
    /// <summary>Partition key — four-digit year of IssueDate.</summary>
    public string Year => IssueDate.Year.ToString("D4");

    [JsonPropertyName("_etag")]
    public string? Etag { get; init; }
}
