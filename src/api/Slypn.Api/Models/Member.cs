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
    /// having a row here. The members table doubles as the newsletter subscriber list, so
    /// existence alone says nothing about whether someone may sign in.
    ///
    /// Holding a role is the real test — every invite assigns exactly one
    /// (<c>MemberInviteInput</c> requires it) and the dev-persona seed does too, while the
    /// anonymous newsletter subscribe is the one write path that produces neither a role nor
    /// any status but "subscribed". The status check guards against a future write path that
    /// forgets to assign one.
    ///
    /// [JsonIgnore] is load-bearing: members persist as a JSON blob in the entity's Json
    /// column, so without it this computed value would be written into storage.
    /// </summary>
    [JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool IsInvited =>
        Roles.Count > 0 && !string.Equals(Status, "subscribed", StringComparison.OrdinalIgnoreCase);
}
