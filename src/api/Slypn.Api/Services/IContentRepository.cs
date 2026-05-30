using Slypn.Api.Models;

namespace Slypn.Api.Services;

/// <summary>
/// Read-side abstraction for public content. Uses Cosmos when configured,
/// falls back to <see cref="IMockDataService"/> otherwise so the API keeps
/// working on a fresh checkout. Write-side endpoints land in #15.
/// </summary>
public interface IContentRepository
{
    Task<IReadOnlyList<Article>>        ListArticlesAsync(string? status, CancellationToken ct);
    Task<Article?>                      GetArticleBySlugAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<CommunityEvent>> ListEventsAsync(bool upcomingOnly, CancellationToken ct);
    Task<IReadOnlyList<Resource>>       ListResourcesAsync(CancellationToken ct);
    Task<IReadOnlyList<Newsletter>>     ListNewslettersAsync(CancellationToken ct);
}
