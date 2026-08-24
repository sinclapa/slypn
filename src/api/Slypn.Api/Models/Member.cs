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

    /// <summary>
    /// True when an admin actually invited this person, as opposed to their address merely
    /// having a row here.
    ///
    /// Newsletter subscribers now live in their own table, so no anonymous write path reaches
    /// this one any more — but the predicate stays as a regression guard. Holding a role is the
    /// real test: every invite assigns exactly one (<c>MemberInviteInput</c> requires it) and the
    /// dev-persona seed does too. The legacy "subscribed" status is still rejected in case a row
    /// predating the split survived the migration.
    ///
    /// [JsonIgnore] is load-bearing: members persist as a JSON blob in the entity's Json
    /// column, so without it this computed value would be written into storage.
    /// </summary>
    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool IsInvited =>
        Roles.Count > 0 && !string.Equals(Status, "subscribed", StringComparison.OrdinalIgnoreCase);
}
