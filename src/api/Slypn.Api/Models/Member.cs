using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record Member(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string Status,
    DateTime InvitedAt,
    DateTime? AcceptedAt = null,
    string? InvitedBy = null,
    string? Oid = null)
{
    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }
}
