namespace Slypn.Api.Models;

public sealed record Newsletter(
    string Id,
    string Title,
    DateOnly IssueDate,
    string Summary,
    IReadOnlyList<string> Topics);
