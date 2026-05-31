namespace Slypn.Seed;

/// <summary>
/// Local mirror of <c>Slypn.Api.Models.Newsletter</c>. The Cosmos schema
/// is the source of truth — keep these field names in sync.
/// </summary>
public sealed record SeedNewsletter(
    string Id,
    string Title,
    DateOnly IssueDate,
    string Summary,
    IReadOnlyList<string> Topics,
    string Year);
