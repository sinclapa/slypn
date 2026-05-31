using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;

namespace Slypn.Api.Services;

/// <summary>
/// Reads + writes for SLYPN public content. Cosmos when configured; mock
/// otherwise (mock writes return false / null — writes need real persistence).
///
/// Optimistic concurrency is propagated via Cosmos's _etag. Write methods
/// take a nullable If-Match etag; null means "don't check".
/// </summary>
public interface IContentRepository
{
    bool SupportsWrites { get; }

    // Reads ------------------------------------------------------------------
    Task<IReadOnlyList<Article>>        ListArticlesAsync(string? status, CancellationToken ct);
    Task<Article?>                      GetArticleBySlugAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<CommunityEvent>> ListEventsAsync(bool upcomingOnly, CancellationToken ct);
    Task<IReadOnlyList<Resource>>       ListResourcesAsync(CancellationToken ct);
    Task<IReadOnlyList<Newsletter>>     ListNewslettersAsync(CancellationToken ct);

    // Writes -----------------------------------------------------------------
    Task<Article>     CreateArticleAsync     (ArticleInput input, CancellationToken ct);
    Task<Article>     ReplaceArticleAsync    (string id, ArticleInput input, string? ifMatch, CancellationToken ct);
    Task              DeleteArticleAsync     (string id, string status, string? ifMatch, CancellationToken ct);

    Task<CommunityEvent> CreateEventAsync    (EventInput input, CancellationToken ct);
    Task<CommunityEvent> ReplaceEventAsync   (string id, EventInput input, string? ifMatch, CancellationToken ct);
    Task                 DeleteEventAsync    (string id, string yearMonth, string? ifMatch, CancellationToken ct);

    Task<Resource>   CreateResourceAsync     (ResourceInput input, CancellationToken ct);
    Task<Resource>   ReplaceResourceAsync    (string id, ResourceInput input, string? ifMatch, CancellationToken ct);
    Task             DeleteResourceAsync     (string id, string category, string? ifMatch, CancellationToken ct);

    Task<Newsletter> CreateNewsletterAsync   (NewsletterInput input, CancellationToken ct);
    Task<Newsletter> ReplaceNewsletterAsync  (string id, NewsletterInput input, string? ifMatch, CancellationToken ct);
    Task             DeleteNewsletterAsync   (string id, string year, string? ifMatch, CancellationToken ct);

    // Members --------------------------------------------------------------
    Task<Member?>             GetMemberByEmailAsync(string email, CancellationToken ct);
    Task<IReadOnlyList<Member>> ListMembersAsync(CancellationToken ct);
    Task<Member>              UpsertMemberAsync(Member member, string? ifMatch, CancellationToken ct);
}
