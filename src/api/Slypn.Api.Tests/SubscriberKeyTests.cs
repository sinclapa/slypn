using Slypn.Api.Models;
using Xunit;

namespace Slypn.Api.Tests;

public class SubscriberKeyTests
{
    // Pins the key derivation. The SEC-5 migration (Slypn.Seed/MigrateSubscribers.cs) recomputes
    // this hash independently to place migrated rows, so a change here that isn't mirrored there
    // would strand every migrated subscriber under a key the API never looks up.
    [Fact]
    public void KeyFor_is_stable()
    {
        Assert.Equal(
            "72497f475e4f76d0b28f57c73a084ece576d170874eba3ee2609d9afe4b71aab",
            Subscriber.KeyFor("someone@example.com"));
    }

    [Fact]
    public void KeyFor_normalises_case_and_whitespace()
    {
        // Why the endpoint can dedupe without a lookup: these are all one subscriber.
        var expected = Subscriber.KeyFor("someone@example.com");
        Assert.Equal(expected, Subscriber.KeyFor("  Someone@Example.COM  "));
    }

    [Fact]
    public void KeyFor_avoids_characters_table_storage_rejects_in_a_row_key()
    {
        // A raw address could carry / \ # ? in a quoted local part; the hash never can.
        var key = Subscriber.KeyFor("weird/local#part?@example.com");
        Assert.Matches("^[0-9a-f]{64}$", key);
    }
}
