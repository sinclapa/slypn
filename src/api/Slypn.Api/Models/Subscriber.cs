using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

/// <summary>
/// A newsletter subscriber. Deliberately *not* a <see cref="Member"/>: subscribing is anonymous and
/// says nothing about whether someone may sign in. The two used to share the members table, which
/// is how an anonymous subscribe once bought its way past the CIAM sign-up gate.
///
/// <c>Id</c> is derived from the email (see <see cref="KeyFor"/>), so re-subscribing the same
/// address upserts the same row instead of creating a duplicate.
/// </summary>
public sealed record Subscriber(
    string Id,
    string Email,
    string DisplayName,
    DateTime SubscribedAt)
{
    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }

    /// <summary>
    /// Storage id for an address: the SHA-256 of the normalised email, hex encoded. Deterministic,
    /// so a repeat subscribe upserts the same row — the dedupe the members table used to provide,
    /// but as a point read rather than a partition scan. Hashing also keeps the id clear of the
    /// characters Table Storage forbids in a RowKey (/ \ # ?).
    /// </summary>
    public static string KeyFor(string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())))
            .ToLowerInvariant();
}
