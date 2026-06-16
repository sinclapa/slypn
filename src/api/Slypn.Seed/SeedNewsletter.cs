namespace Slypn.Seed;

/// <summary>
/// Local mirror of <c>Slypn.Api.Models.Newsletter</c>. The Table Storage
/// <c>Json</c> column schema is the source of truth — keep these field names in
/// sync so the API can deserialise it into a Newsletter.
/// </summary>
public sealed record SeedNewsletter(
    string Id,
    string Title,
    DateOnly IssueDate,
    string Summary,
    IReadOnlyList<string> Topics,
    string Year);
