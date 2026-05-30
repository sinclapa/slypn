namespace Slypn.Api.Models;

public sealed record BlogPost(
    string Id,
    string Slug,
    string Title,
    string Excerpt,
    string Body,
    string Author,
    DateTime PublishedAt);
