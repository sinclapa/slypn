using Microsoft.Azure.Cosmos;
using Slypn.Api.Models;

namespace Slypn.Api.Services;

public sealed class ContentRepository(ICosmosService cosmos, IMockDataService mock) : IContentRepository
{
    public async Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, CancellationToken ct)
    {
        if (!cosmos.IsConfigured)
        {
            IEnumerable<Article> source = mock.Articles;
            if (status is not null)
            {
                source = source.Where(a => string.Equals(a.Status, status, StringComparison.OrdinalIgnoreCase));
            }
            return source.OrderByDescending(a => a.PublishedAt).ToList();
        }

        var query = status is null
            ? new QueryDefinition("SELECT * FROM c ORDER BY c.publishedAt DESC")
            : new QueryDefinition("SELECT * FROM c WHERE c.status = @status ORDER BY c.publishedAt DESC")
                .WithParameter("@status", status);

        var options = new QueryRequestOptions
        {
            PartitionKey = status is null ? null : new PartitionKey(status),
        };
        return await CollectAsync<Article>(cosmos.Articles, query, options, ct);
    }

    public async Task<Article?> GetArticleBySlugAsync(string slug, CancellationToken ct)
    {
        if (!cosmos.IsConfigured)
        {
            return mock.Articles.FirstOrDefault(a =>
                string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
        }

        var query = new QueryDefinition("SELECT * FROM c WHERE c.slug = @slug")
            .WithParameter("@slug", slug);
        var results = await CollectAsync<Article>(cosmos.Articles, query, new QueryRequestOptions(), ct);
        return results.FirstOrDefault();
    }

    public async Task<IReadOnlyList<CommunityEvent>> ListEventsAsync(bool upcomingOnly, CancellationToken ct)
    {
        if (!cosmos.IsConfigured)
        {
            IEnumerable<CommunityEvent> source = mock.Events;
            if (upcomingOnly)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                source = source.Where(e => e.StartsAt >= nowUtc);
            }
            return source.OrderBy(e => e.StartsAt).ToList();
        }

        var query = upcomingOnly
            ? new QueryDefinition("SELECT * FROM c WHERE c.startsAt >= @now ORDER BY c.startsAt")
                .WithParameter("@now", DateTimeOffset.UtcNow)
            : new QueryDefinition("SELECT * FROM c ORDER BY c.startsAt");
        return await CollectAsync<CommunityEvent>(cosmos.Events, query, new QueryRequestOptions(), ct);
    }

    public async Task<IReadOnlyList<Resource>> ListResourcesAsync(CancellationToken ct)
    {
        if (!cosmos.IsConfigured)
        {
            return mock.Resources;
        }

        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.category, c.title");
        return await CollectAsync<Resource>(cosmos.Resources, query, new QueryRequestOptions(), ct);
    }

    public async Task<IReadOnlyList<Newsletter>> ListNewslettersAsync(CancellationToken ct)
    {
        if (!cosmos.IsConfigured)
        {
            return mock.Newsletters.OrderByDescending(n => n.IssueDate).ToList();
        }

        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.issueDate DESC");
        return await CollectAsync<Newsletter>(cosmos.Newsletters, query, new QueryRequestOptions(), ct);
    }

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(
        Container container, QueryDefinition query, QueryRequestOptions options, CancellationToken ct)
    {
        var results = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page);
        }
        return results;
    }
}
