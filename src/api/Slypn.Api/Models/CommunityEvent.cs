namespace Slypn.Api.Models;

public sealed record CommunityEvent(
    string Id,
    string Title,
    string Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    string Description,
    string? SignupUrl);
