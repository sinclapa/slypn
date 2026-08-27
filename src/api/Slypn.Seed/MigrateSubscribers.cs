using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace Slypn.Seed;

/// <summary>
/// One-off SEC-5 migration: moves legacy newsletter subscribers out of the <c>members</c> table
/// into <c>subscribers</c>.
///
/// Before SEC-5, the subscribe endpoint — then POST /api/newsletter/subscribe, now POST
/// /api/subscribers — stored each address as a members row with Status="subscribed", so
/// subscribers and invited members shared one table. Subscribers have their own table now;
/// these rows have to follow.
///
/// Idempotent and re-runnable: the destination row key is derived from the address, so a repeat
/// run rewrites the same rows, and rows already migrated no longer match the source filter.
/// </summary>
public static class MigrateSubscribers
{
    private const string MembersTable         = "members";
    private const string SubscribersTable     = "subscribers";
    private const string MembersPartition     = "member";
    private const string SubscribersPartition = "subscriber";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Only the fields the filter and the copy need; the rest of the members row is discarded.
    private sealed record MemberRow(
        string? Id, string? Email, string? DisplayName,
        List<string>? Roles, string? Status, DateTime? InvitedAt, string? Oid);

    private sealed record SubscriberRow(
        string Id, string Email, string DisplayName, DateTime SubscribedAt);

    public static async Task<int> RunAsync(string connectionString, bool dryRun, TextWriter output)
    {
        var service     = new TableServiceClient(connectionString);
        var members     = service.GetTableClient(MembersTable);
        var subscribers = service.GetTableClient(SubscribersTable);

        if (!dryRun) await subscribers.CreateIfNotExistsAsync();

        var moved = 0;
        var left  = 0;

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {MembersPartition}");
        await foreach (var entity in members.QueryAsync<TableEntity>(filter: filter))
        {
            var json = entity.GetString("Json");
            if (string.IsNullOrEmpty(json)) { left++; continue; }

            MemberRow? row;
            try { row = JsonSerializer.Deserialize<MemberRow>(json, JsonOpts); }
            catch (JsonException) { left++; continue; }

            if (row is null || !IsLegacySubscriber(row) || string.IsNullOrWhiteSpace(row.Email))
            {
                left++;
                continue;
            }

            var email = row.Email.Trim().ToLowerInvariant();
            var key   = KeyFor(email);

            if (dryRun)
            {
                await output.WriteLineAsync($"  would move {email} -> {SubscribersTable}/{key}");
                moved++;
                continue;
            }

            // SubscribedAt carries over from InvitedAt: on a subscriber row that timestamp was only
            // ever set by the subscribe endpoint, so it really is the date they signed up.
            var subscriber = new SubscriberRow(
                Id:           key,
                Email:        email,
                DisplayName:  string.IsNullOrWhiteSpace(row.DisplayName) ? email : row.DisplayName,
                SubscribedAt: row.InvitedAt ?? DateTime.UtcNow);

            await subscribers.UpsertEntityAsync(
                new TableEntity(SubscribersPartition, key) { ["Json"] = JsonSerializer.Serialize(subscriber, JsonOpts) },
                TableUpdateMode.Replace);

            // Only after the copy landed.
            try { await members.DeleteEntityAsync(entity.PartitionKey, entity.RowKey); }
            catch (RequestFailedException ex) when (ex.Status == 404) { /* already gone */ }

            await output.WriteLineAsync($"  - moved {email} -> {SubscribersTable}/{key}");
            moved++;
        }

        var verb = dryRun ? "Would move" : "Moved";
        await output.WriteLineAsync($"{verb} {moved} subscriber row(s); left {left} members row(s) alone.");
        return 0;
    }

    /// <summary>
    /// The exact inverse of <c>Member.IsInvited</c>, plus "has never signed in". A member who
    /// somehow carries the legacy status but holds a role or an OID is a real member and stays put.
    /// </summary>
    private static bool IsLegacySubscriber(MemberRow row) =>
        string.Equals(row.Status, "subscribed", StringComparison.OrdinalIgnoreCase)
        && (row.Roles is null || row.Roles.Count == 0)
        && string.IsNullOrEmpty(row.Oid);

    /// <summary>
    /// Must stay identical to <c>Slypn.Api.Models.Subscriber.KeyFor</c> — the API finds a migrated
    /// row by recomputing this from the address. Pinned on both sides by
    /// <c>SubscriberKeyTests.KeyFor_is_stable</c>: sha256("someone@example.com") =
    /// 72497f475e4f76d0b28f57c73a084ece576d170874eba3ee2609d9afe4b71aab.
    /// </summary>
    private static string KeyFor(string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())))
            .ToLowerInvariant();
}
