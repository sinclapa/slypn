using Microsoft.Azure.Cosmos;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;

namespace Slypn.Api.Services;

public sealed class ContentRepository(ICosmosService cosmos, IMockDataService mock) : IContentRepository
{
    public bool SupportsWrites => cosmos.IsConfigured;

    // ---- Reads ----------------------------------------------------------------
    public async Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, CancellationToken ct)
    {
        if (!cosmos.IsConfigured)
        {
            IEnumerable<Article> source = mock.Articles;
            if (status is not null)
                source = source.Where(a => string.Equals(a.Status, status, StringComparison.OrdinalIgnoreCase));
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
            return mock.Articles.FirstOrDefault(a =>
                string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

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
                source = source.Where(e => e.StartsAt >= DateTimeOffset.UtcNow);
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
        if (!cosmos.IsConfigured) return mock.Resources;
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.category, c.title");
        return await CollectAsync<Resource>(cosmos.Resources, query, new QueryRequestOptions(), ct);
    }

    public async Task<IReadOnlyList<Newsletter>> ListNewslettersAsync(CancellationToken ct)
    {
        if (!cosmos.IsConfigured) return mock.Newsletters.OrderByDescending(n => n.IssueDate).ToList();
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.issueDate DESC");
        return await CollectAsync<Newsletter>(cosmos.Newsletters, query, new QueryRequestOptions(), ct);
    }

    // ---- Articles writes ------------------------------------------------------
    public async Task<Article> CreateArticleAsync(ArticleInput input, CancellationToken ct)
    {
        EnsureWrites();
        var article = new Article(
            Id:             Guid.NewGuid().ToString("N"),
            Slug:           input.Slug,
            Title:          input.Title,
            Summary:        input.Summary,
            Body:           input.Body,
            Author:         input.Author,
            PublishedAt:    DateTime.UtcNow,
            ReadingMinutes: input.ReadingMinutes,
            Category:       input.Category,
            Tags:           input.Tags,
            Status:         input.Status);
        var resp = await cosmos.Articles.CreateItemAsync(article, new PartitionKey(article.Status), cancellationToken: ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task<Article> ReplaceArticleAsync(string id, ArticleInput input, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var article = new Article(
            Id:             id,
            Slug:           input.Slug,
            Title:          input.Title,
            Summary:        input.Summary,
            Body:           input.Body,
            Author:         input.Author,
            PublishedAt:    DateTime.UtcNow,
            ReadingMinutes: input.ReadingMinutes,
            Category:       input.Category,
            Tags:           input.Tags,
            Status:         input.Status);
        var resp = await cosmos.Articles.ReplaceItemAsync(article, id,
            new PartitionKey(article.Status),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task DeleteArticleAsync(string id, string status, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await cosmos.Articles.DeleteItemAsync<Article>(id,
            new PartitionKey(status),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
    }

    // ---- Events writes --------------------------------------------------------
    public async Task<CommunityEvent> CreateEventAsync(EventInput input, CancellationToken ct)
    {
        EnsureWrites();
        var ev = new CommunityEvent(
            Id:          Guid.NewGuid().ToString("N"),
            Title:       input.Title,
            Type:        input.Type,
            StartsAt:    input.StartsAt,
            EndsAt:      input.EndsAt,
            Location:    input.Location,
            Description: input.Description,
            SignupUrl:   input.SignupUrl);
        var resp = await cosmos.Events.CreateItemAsync(ev, new PartitionKey(ev.YearMonth), cancellationToken: ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task<CommunityEvent> ReplaceEventAsync(string id, EventInput input, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var ev = new CommunityEvent(
            Id:          id,
            Title:       input.Title,
            Type:        input.Type,
            StartsAt:    input.StartsAt,
            EndsAt:      input.EndsAt,
            Location:    input.Location,
            Description: input.Description,
            SignupUrl:   input.SignupUrl);
        var resp = await cosmos.Events.ReplaceItemAsync(ev, id,
            new PartitionKey(ev.YearMonth),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task DeleteEventAsync(string id, string yearMonth, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await cosmos.Events.DeleteItemAsync<CommunityEvent>(id,
            new PartitionKey(yearMonth),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
    }

    // ---- Resources writes -----------------------------------------------------
    public async Task<Resource> CreateResourceAsync(ResourceInput input, CancellationToken ct)
    {
        EnsureWrites();
        var r = new Resource(Guid.NewGuid().ToString("N"), input.Title, input.Description, input.Url, input.Category);
        var resp = await cosmos.Resources.CreateItemAsync(r, new PartitionKey(r.Category), cancellationToken: ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task<Resource> ReplaceResourceAsync(string id, ResourceInput input, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var r = new Resource(id, input.Title, input.Description, input.Url, input.Category);
        var resp = await cosmos.Resources.ReplaceItemAsync(r, id,
            new PartitionKey(r.Category),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task DeleteResourceAsync(string id, string category, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await cosmos.Resources.DeleteItemAsync<Resource>(id,
            new PartitionKey(category),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
    }

    // ---- Newsletters writes ---------------------------------------------------
    public async Task<Newsletter> CreateNewsletterAsync(NewsletterInput input, CancellationToken ct)
    {
        EnsureWrites();
        var n = new Newsletter(Guid.NewGuid().ToString("N"), input.Title, input.IssueDate, input.Summary, input.Topics);
        var resp = await cosmos.Newsletters.CreateItemAsync(n, new PartitionKey(n.Year), cancellationToken: ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task<Newsletter> ReplaceNewsletterAsync(string id, NewsletterInput input, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var n = new Newsletter(id, input.Title, input.IssueDate, input.Summary, input.Topics);
        var resp = await cosmos.Newsletters.ReplaceItemAsync(n, id,
            new PartitionKey(n.Year),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task DeleteNewsletterAsync(string id, string year, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await cosmos.Newsletters.DeleteItemAsync<Newsletter>(id,
            new PartitionKey(year),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
    }

    // ---- helpers --------------------------------------------------------------
    private void EnsureWrites()
    {
        if (!cosmos.IsConfigured)
            throw new InvalidOperationException(
                "ContentRepository cannot write: Cosmos is not configured. Run setup.ps1 + start.ps1 after Phase 2 emulator wiring (#17), or supply Cosmos__Endpoint/Cosmos__Key.");
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
