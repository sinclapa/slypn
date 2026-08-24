namespace Slypn.Api.Infrastructure;

/// <summary>
/// Local-dev test personas used only when <see cref="EntraOptions.SkipAuth"/> is true.
/// The web client sends the persona key in the <c>X-Slypn-Dev-User</c> header; the
/// <see cref="JwtMiddleware"/> synthesises a matching principal and the
/// <c>TableBootstrapper</c> seeds a matching member record.
///
/// Keep keys/OIDs/roles in sync with src/web/src/lib/devPersonas.ts.
/// </summary>
public sealed record DevPersona(string Key, string Oid, string Email, string Name, string[] Roles);

public static class DevPersonas
{
    public const string HeaderName = "X-Slypn-Dev-User";

    public const string DefaultKey = "admin";

    public static readonly IReadOnlyList<DevPersona> All =
    [
        new("admin",        "11111111-1111-1111-1111-111111111111", "slypn.test.admin@cookingcode.com",        "Test Admin",         ["Admin"]),
        new("admin2",       "44444444-4444-4444-4444-444444444444", "slypn.test.admin2@cookingcode.com",       "Test Admin 2",       ["Admin"]),
        new("contributor",  "22222222-2222-2222-2222-222222222222", "slypn.test.contributor@cookingcode.com",  "Test Contributor",   ["Contributor"]),
        new("contributor2", "55555555-5555-5555-5555-555555555555", "slypn.test.contributor2@cookingcode.com", "Test Contributor 2", ["Contributor"]),
        new("member",       "33333333-3333-3333-3333-333333333333", "slypn.test.member@cookingcode.com",        "Test Member",        ["Member"]),
    ];

    private static readonly Dictionary<string, DevPersona> ByKey =
        All.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a persona by key, falling back to the default (admin) when unknown/null.</summary>
    public static DevPersona Resolve(string? key) =>
        key is not null && ByKey.TryGetValue(key.Trim(), out var persona)
            ? persona
            : ByKey[DefaultKey];
}
