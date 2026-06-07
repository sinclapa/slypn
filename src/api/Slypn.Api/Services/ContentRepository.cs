using Microsoft.Azure.Cosmos;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;

namespace Slypn.Api.Services;

public sealed class ContentRepository(ICosmosService cosmos, IMockDataService mock) : IContentRepository
{
    public bool SupportsWrites => cosmos.IsConfigured;

    // ---- Reads ----------------------------------------------------------------
    public async Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, CancellationToken ct)
        => await ListArticlesAsync(status, type: "article", ct);

    public async Task<IReadOnlyList<Article>> ListBlogPostsAsync(string? status, CancellationToken ct)
        => await ListArticlesAsync(status, type: "blog", ct);

    private async Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, string type, CancellationToken ct)
    {
        // Rows that predate the Type field deserialise with Type="article" (record default),
        // so we treat IS_DEFINED missing-or-equals-"article" as the article bucket.
        var typeClause = type == "article"
            ? "(c.type = @type OR NOT IS_DEFINED(c.type))"
            : "c.type = @type";

        if (!cosmos.IsConfigured)
        {
            IEnumerable<Article> source = mock.Articles.Where(a =>
                string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase));
            if (status is not null)
                source = source.Where(a => string.Equals(a.Status, status, StringComparison.OrdinalIgnoreCase));
            return source.OrderByDescending(a => a.PublishedAt).ToList();
        }

        var sql = status is null
            ? $"SELECT * FROM c WHERE {typeClause} ORDER BY c.publishedAt DESC"
            : $"SELECT * FROM c WHERE c.status = @status AND {typeClause} ORDER BY c.publishedAt DESC";
        var query = new QueryDefinition(sql).WithParameter("@type", type);
        if (status is not null) query = query.WithParameter("@status", status);

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

    public async Task<CommunityEvent?> GetEventByIdAsync(string id, CancellationToken ct)
    {
        if (!cosmos.IsConfigured) return null;
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id);
        var results = await CollectAsync<CommunityEvent>(cosmos.Events, query, new QueryRequestOptions(), ct);
        return results.FirstOrDefault();
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
    public async Task<CommunityEvent> CreateEventAsync(EventInput input, string? createdByOid, string? createdByName, CancellationToken ct)
    {
        EnsureWrites();
        var ev = new CommunityEvent(
            Id:            Guid.NewGuid().ToString("N"),
            Title:         input.Title,
            Type:          input.Type,
            StartsAt:      input.StartsAt,
            EndsAt:        input.EndsAt,
            Location:      input.Location,
            Description:   input.Description,
            SignupUrl:     input.SignupUrl,
            CreatedBy:     createdByOid,
            CreatedByName: createdByName);
        var resp = await cosmos.Events.CreateItemAsync(ev, new PartitionKey(ev.YearMonth), cancellationToken: ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task<CommunityEvent> ReplaceEventAsync(string id, string oldYearMonth, EventInput input, string? createdByOid, string? createdByName, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var ev = new CommunityEvent(
            Id:            id,
            Title:         input.Title,
            Type:          input.Type,
            StartsAt:      input.StartsAt,
            EndsAt:        input.EndsAt,
            Location:      input.Location,
            Description:   input.Description,
            SignupUrl:     input.SignupUrl,
            CreatedBy:     createdByOid,
            CreatedByName: createdByName);

        if (ev.YearMonth == oldYearMonth)
        {
            // Same partition — in-place replace
            var resp = await cosmos.Events.ReplaceItemAsync(ev, id,
                new PartitionKey(oldYearMonth),
                new ItemRequestOptions { IfMatchEtag = ifMatch }, ct);
            return resp.Resource with { Etag = resp.ETag };
        }
        else
        {
            // Partition key changed — delete old doc, create new one
            await cosmos.Events.DeleteItemAsync<CommunityEvent>(id,
                new PartitionKey(oldYearMonth),
                new ItemRequestOptions { IfMatchEtag = ifMatch }, ct);
            var resp = await cosmos.Events.CreateItemAsync(ev, new PartitionKey(ev.YearMonth), cancellationToken: ct);
            return resp.Resource with { Etag = resp.ETag };
        }
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

    // ---- Members -------------------------------------------------------------
    public async Task<Member?> GetMemberByEmailAsync(string email, CancellationToken ct)
    {
        EnsureWrites();
        var normalized = email.Trim().ToLowerInvariant();
        var query = new QueryDefinition("SELECT * FROM c WHERE LOWER(c.email) = @email")
            .WithParameter("@email", normalized);
        var results = await CollectAsync<Member>(cosmos.Members, query, new QueryRequestOptions(), ct);
        return results.FirstOrDefault();
    }

    public async Task<Member?> GetMemberByOidAsync(string oid, CancellationToken ct)
    {
        EnsureWrites();
        var query = new QueryDefinition("SELECT * FROM c WHERE c.oid = @oid")
            .WithParameter("@oid", oid);
        var results = await CollectAsync<Member>(cosmos.Members, query, new QueryRequestOptions(), ct);
        return results.FirstOrDefault();
    }

    public async Task<IReadOnlyList<Member>> ListMembersAsync(CancellationToken ct)
    {
        EnsureWrites();
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.invitedAt DESC");
        return await CollectAsync<Member>(cosmos.Members, query, new QueryRequestOptions(), ct);
    }

    public async Task<Member?> GetMemberByIdAsync(string id, CancellationToken ct)
    {
        EnsureWrites();
        try
        {
            var resp = await cosmos.Members.ReadItemAsync<Member>(id, new PartitionKey(id), cancellationToken: ct);
            return resp.Resource with { Etag = resp.ETag };
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Member> UpsertMemberAsync(Member member, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var resp = await cosmos.Members.UpsertItemAsync(member,
            new PartitionKey(member.Id),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task DeleteMemberAsync(string id, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await cosmos.Members.DeleteItemAsync<Member>(id, new PartitionKey(id),
            new ItemRequestOptions { IfMatchEtag = ifMatch }, ct);
    }

    // ---- Drafts --------------------------------------------------------------
    public async Task<Draft?> GetDraftAsync(string id, string authorId, CancellationToken ct)
    {
        EnsureWrites();
        try
        {
            var resp = await cosmos.Drafts.ReadItemAsync<Draft>(id, new PartitionKey(authorId), cancellationToken: ct);
            return resp.Resource with { Etag = resp.ETag };
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Draft>> ListDraftsByAuthorAsync(string authorId, CancellationToken ct)
    {
        EnsureWrites();
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.updatedAt DESC");
        var options = new QueryRequestOptions { PartitionKey = new PartitionKey(authorId) };
        return await CollectAsync<Draft>(cosmos.Drafts, query, options, ct);
    }

    public async Task<Draft> UpsertDraftAsync(Draft draft, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var resp = await cosmos.Drafts.UpsertItemAsync(draft,
            new PartitionKey(draft.AuthorId),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
        return resp.Resource with { Etag = resp.ETag };
    }

    public async Task DeleteDraftAsync(string id, string authorId, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await cosmos.Drafts.DeleteItemAsync<Draft>(id,
            new PartitionKey(authorId),
            new ItemRequestOptions { IfMatchEtag = ifMatch },
            ct);
    }

    // ---- Workflow ------------------------------------------------------------
    public async Task<Article> SubmitDraftAsync(string draftId, string authorId, CancellationToken ct)
    {
        EnsureWrites();

        Draft draft;
        try
        {
            var read = await cosmos.Drafts.ReadItemAsync<Draft>(draftId, new PartitionKey(authorId), cancellationToken: ct);
            draft = read.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Draft {draftId} not found for author {authorId}.");
        }

        var article = new Article(
            Id:             draft.Id,
            Slug:           draft.Slug,
            Title:          draft.Title,
            Summary:        draft.Summary,
            Body:           draft.Body,
            Author:         draft.AuthorName,
            PublishedAt:    DateTime.UtcNow, // submission time; updated on publish
            ReadingMinutes: draft.ReadingMinutes,
            Category:       draft.Category,
            Tags:           draft.Tags,
            Status:         "in-review")
        {
            Type = string.Equals(draft.Type, "blog", StringComparison.OrdinalIgnoreCase) ? "blog" : "article",
            AuthorId = draft.AuthorId,
        };

        var upserted = await cosmos.Articles.UpsertItemAsync(article, new PartitionKey("in-review"), cancellationToken: ct);
        await cosmos.Drafts.DeleteItemAsync<Draft>(draft.Id, new PartitionKey(authorId), cancellationToken: ct);
        return upserted.Resource with { Etag = upserted.ETag };
    }

    public async Task<Article?> GetArticleAsync(string id, string status, CancellationToken ct)
    {
        EnsureWrites();
        try
        {
            var resp = await cosmos.Articles.ReadItemAsync<Article>(id, new PartitionKey(status), cancellationToken: ct);
            return resp.Resource with { Etag = resp.ETag };
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Article> PublishArticleAsync(string id, CancellationToken ct)
        => await TransitionAsync(id, fromStatus: "in-review", toStatus: "published",
            update: a => a with { PublishedAt = DateTime.UtcNow, RejectionReason = null }, ct);

    public async Task<Draft> ReviseArticleAsync(string id, string feedback, CancellationToken ct)
    {
        EnsureWrites();
        Article source;
        try
        {
            var read = await cosmos.Articles.ReadItemAsync<Article>(id, new PartitionKey("in-review"), cancellationToken: ct);
            source = read.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Article {id} not found in in-review status.");
        }

        if (string.IsNullOrEmpty(source.AuthorId))
            throw new InvalidOperationException("Article has no AuthorId — cannot create revision draft.");

        var now = DateTime.UtcNow;
        var draft = new Draft(
            Id:               source.Id,
            AuthorId:         source.AuthorId,
            AuthorName:       source.Author,
            Type:             source.Type,
            Title:            source.Title,
            Slug:             source.Slug,
            Summary:          source.Summary,
            Body:             source.Body,
            Category:         source.Category,
            Tags:             source.Tags,
            ReadingMinutes:   source.ReadingMinutes,
            CreatedAt:        now,
            UpdatedAt:        now,
            RevisionFeedback: feedback);

        var upserted = await cosmos.Drafts.UpsertItemAsync(draft, new PartitionKey(source.AuthorId), cancellationToken: ct);
        await cosmos.Articles.DeleteItemAsync<Article>(id, new PartitionKey("in-review"), cancellationToken: ct);
        return upserted.Resource with { Etag = upserted.ETag };
    }

    private async Task<Article> TransitionAsync(
        string id, string fromStatus, string toStatus, Func<Article, Article> update, CancellationToken ct)
    {
        EnsureWrites();
        // Cosmos doesn't permit changing a document's partition-key value, so
        // we read from the source partition, create in the target, then delete
        // the source. Not atomic; admin actions are rare and re-runs are
        // idempotent (re-doing publish on an already-published article is a
        // no-op because the source is gone — the 404 surfaces as a clean error).
        Article source;
        try
        {
            var read = await cosmos.Articles.ReadItemAsync<Article>(id, new PartitionKey(fromStatus), cancellationToken: ct);
            source = read.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Article {id} is not in '{fromStatus}'.");
        }

        var transitioned = update(source with { Status = toStatus, Etag = null });

        var created = await cosmos.Articles.UpsertItemAsync(transitioned, new PartitionKey(toStatus), cancellationToken: ct);
        await cosmos.Articles.DeleteItemAsync<Article>(id, new PartitionKey(fromStatus), cancellationToken: ct);
        return created.Resource with { Etag = created.ETag };
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
