namespace Slypn.Api.Models;

/// <summary>Lightweight reference to an adjacent event for prev/next navigation.</summary>
public sealed record EventNeighbour(string Id, string Title, DateTimeOffset StartsAt);
