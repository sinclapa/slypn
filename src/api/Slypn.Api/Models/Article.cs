namespace Slypn.Api.Models;

public sealed record Article(
    string Id,
    string Slug,
    string Title,
    string Summary,
    string Body,
    string Author,
    DateTime PublishedAt,
    int ReadingMinutes,
    string Category,
    IReadOnlyList<string> Tags,
    string Status = "published");
