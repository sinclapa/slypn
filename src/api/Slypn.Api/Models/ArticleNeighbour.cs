namespace Slypn.Api.Models;

/// <summary>Lightweight reference to an adjacent article for prev/next navigation.</summary>
public sealed record ArticleNeighbour(string Slug, string Title);
