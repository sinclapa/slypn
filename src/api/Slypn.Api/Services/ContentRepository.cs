using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;

namespace Slypn.Api.Services;

/// <summary>
/// Reads + writes for SLYPN content backed by Azure Table Storage (structured
/// metadata) and Blob Storage (large article/draft HTML bodies). Falls back to
/// <see cref="IMockDataService"/> for reads when storage is not configured.
///
/// Each table row stores the entity metadata as a single <c>Json</c> column;
/// articles/drafts blank their <c>Body</c> in that JSON and keep the real body
/// in a blob keyed by content id. Table Storage only orders by PartitionKey +
/// RowKey, so list ordering/filtering is done in memory. Optimistic concurrency
/// uses the native entity ETag, base64-encoded for HTTP transport.
/// </summary>
public sealed class ContentRepository(ITableStore store, IContentBodyStore body, IMockDataService mock) : IContentRepository
{
    public bool SupportsWrites => store.IsConfigured;

    private const string ArticleBodyPrefix = "articles";
    private const string DraftBodyPrefix   = "drafts";
    private const string MembersPartition  = "member";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ---- Articles reads -------------------------------------------------------
    public async Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, CancellationToken ct)
        => await ListArticlesAsync(status, type: "article", ct);

    public async Task<IReadOnlyList<Article>> ListBlogPostsAsync(string? status, CancellationToken ct)
        => await ListArticlesAsync(status, type: "blog", ct);

    private async Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, string type, CancellationToken ct)
    {
        if (!store.IsConfigured)
        {
            IEnumerable<Article> source = mock.Articles.Where(a =>
                string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase));
            if (status is not null)
                source = source.Where(a => string.Equals(a.Status, status, StringComparison.OrdinalIgnoreCase));
            return source.OrderByDescending(a => a.PublishedAt).ToList();
        }

        var filter = status is null ? null : TableClient.CreateQueryFilter($"PartitionKey eq {status}");
        var entities = await QueryAsync(store.Articles, filter, ct);

        // Rows that predate the Type field deserialise with Type="article" (record default),
        // so missing-or-"article" counts as the article bucket.
        bool MatchesType(Article a) => type == "article"
            ? string.Equals(a.Type, "article", StringComparison.OrdinalIgnoreCase)
            : string.Equals(a.Type, type, StringComparison.OrdinalIgnoreCase);

        var articles = entities
            .Select(e => Deserialize<Article>(e) with { Etag = EncodeEtag(e.ETag) })
            .Where(MatchesType)
            .OrderByDescending(a => a.PublishedAt)
            .ToList();

        return await WithBodiesAsync(articles, ArticleBodyPrefix, ct);
    }

    public async Task<Article?> GetArticleBySlugAsync(string slugOrId, CancellationToken ct)
    {
        if (!store.IsConfigured)
            return mock.Articles.FirstOrDefault(a =>
                string.Equals(a.Slug, slugOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Id,   slugOrId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(slugOrId))
        {
            var filter = TableClient.CreateQueryFilter($"Slug eq {slugOrId}");
            var entities = await QueryAsync(store.Articles, filter, ct);
            if (entities.Count > 0)
            {
                var first = entities[0];
                var article = Deserialize<Article>(first) with { Etag = EncodeEtag(first.ETag) };
                return article with { Body = await body.GetAsync(ArticleBodyPrefix, article.Id, ct) };
            }
        }

        // Fallback: treat the value as an article id. Covers content created before
        // hybrid slugs existed (empty/blank slug) so it stays addressable.
        return await GetArticleAsync(slugOrId, "published", ct);
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

        await body.PutAsync(ArticleBodyPrefix, article.Id, article.Body, ct);
        var resp = await store.Articles.AddEntityAsync(ArticleEntity(article), ct);
        return article with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
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

        await body.PutAsync(ArticleBodyPrefix, article.Id, article.Body, ct);
        var resp = await store.Articles.UpdateEntityAsync(
            ArticleEntity(article), DecodeEtag(ifMatch), TableUpdateMode.Replace, ct);
        return article with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task DeleteArticleAsync(string id, string status, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await store.Articles.DeleteEntityAsync(status, id, DecodeEtag(ifMatch), ct);
        await body.DeleteAsync(ArticleBodyPrefix, id, ct);
    }

    // ---- Events ---------------------------------------------------------------
    public async Task<IReadOnlyList<CommunityEvent>> ListEventsAsync(bool upcomingOnly, CancellationToken ct)
    {
        if (!store.IsConfigured)
        {
            IEnumerable<CommunityEvent> source = mock.Events;
            if (upcomingOnly)
                source = source.Where(e => e.StartsAt >= DateTimeOffset.UtcNow);
            return source.OrderBy(e => e.StartsAt).ToList();
        }

        var entities = await QueryAsync(store.Events, filter: null, ct);
        IEnumerable<CommunityEvent> events = entities
            .Select(e => Deserialize<CommunityEvent>(e) with { Etag = EncodeEtag(e.ETag) });
        if (upcomingOnly)
            events = events.Where(e => e.StartsAt >= DateTimeOffset.UtcNow);
        return events.OrderBy(e => e.StartsAt).ToList();
    }

    public async Task<CommunityEvent?> GetEventByIdAsync(string id, CancellationToken ct)
    {
        if (!store.IsConfigured) return null;
        var filter = TableClient.CreateQueryFilter($"RowKey eq {id}");
        var entities = await QueryAsync(store.Events, filter, ct);
        if (entities.Count == 0) return null;
        return Deserialize<CommunityEvent>(entities[0]) with { Etag = EncodeEtag(entities[0].ETag) };
    }

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
        var resp = await store.Events.AddEntityAsync(Entity(ev.YearMonth, ev.Id, ev), ct);
        return ev with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
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
            var resp = await store.Events.UpdateEntityAsync(
                Entity(oldYearMonth, id, ev), DecodeEtag(ifMatch), TableUpdateMode.Replace, ct);
            return ev with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
        }

        // Partition key changed — delete the old row, create one in the new partition.
        await store.Events.DeleteEntityAsync(oldYearMonth, id, DecodeEtag(ifMatch), ct);
        var created = await store.Events.AddEntityAsync(Entity(ev.YearMonth, id, ev), ct);
        return ev with { Etag = EncodeEtag(created.Headers.ETag!.Value) };
    }

    public async Task DeleteEventAsync(string id, string yearMonth, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await store.Events.DeleteEntityAsync(yearMonth, id, DecodeEtag(ifMatch), ct);
    }

    // ---- Resources ------------------------------------------------------------
    public async Task<IReadOnlyList<Resource>> ListResourcesAsync(CancellationToken ct)
    {
        if (!store.IsConfigured) return mock.Resources;
        var entities = await QueryAsync(store.Resources, filter: null, ct);
        return entities
            .Select(e => Deserialize<Resource>(e) with { Etag = EncodeEtag(e.ETag) })
            .OrderBy(r => r.Category).ThenBy(r => r.Title)
            .ToList();
    }

    public async Task<Resource> CreateResourceAsync(ResourceInput input, CancellationToken ct)
    {
        EnsureWrites();
        var r = new Resource(Guid.NewGuid().ToString("N"), input.Title, input.Description, input.Url, input.Category);
        var resp = await store.Resources.AddEntityAsync(Entity(r.Category, r.Id, r), ct);
        return r with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task<Resource> ReplaceResourceAsync(string id, ResourceInput input, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var r = new Resource(id, input.Title, input.Description, input.Url, input.Category);
        var resp = await store.Resources.UpdateEntityAsync(
            Entity(r.Category, r.Id, r), DecodeEtag(ifMatch), TableUpdateMode.Replace, ct);
        return r with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task DeleteResourceAsync(string id, string category, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await store.Resources.DeleteEntityAsync(category, id, DecodeEtag(ifMatch), ct);
    }

    // ---- Newsletters ----------------------------------------------------------
    public async Task<IReadOnlyList<Newsletter>> ListNewslettersAsync(CancellationToken ct)
    {
        if (!store.IsConfigured) return mock.Newsletters.OrderByDescending(n => n.IssueDate).ToList();
        var entities = await QueryAsync(store.Newsletters, filter: null, ct);
        return entities
            .Select(e => Deserialize<Newsletter>(e) with { Etag = EncodeEtag(e.ETag) })
            .OrderByDescending(n => n.IssueDate)
            .ToList();
    }

    public async Task<Newsletter> CreateNewsletterAsync(NewsletterInput input, CancellationToken ct)
    {
        EnsureWrites();
        var n = new Newsletter(Guid.NewGuid().ToString("N"), input.Title, input.IssueDate, input.Summary, input.Topics);
        var resp = await store.Newsletters.AddEntityAsync(Entity(n.Year, n.Id, n), ct);
        return n with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task<Newsletter> ReplaceNewsletterAsync(string id, NewsletterInput input, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var n = new Newsletter(id, input.Title, input.IssueDate, input.Summary, input.Topics);
        var resp = await store.Newsletters.UpdateEntityAsync(
            Entity(n.Year, n.Id, n), DecodeEtag(ifMatch), TableUpdateMode.Replace, ct);
        return n with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task DeleteNewsletterAsync(string id, string year, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await store.Newsletters.DeleteEntityAsync(year, id, DecodeEtag(ifMatch), ct);
    }

    // ---- Members --------------------------------------------------------------
    public async Task<Member?> GetMemberByEmailAsync(string email, CancellationToken ct)
    {
        EnsureWrites();
        var normalized = email.Trim();
        var members = await AllMembersAsync(ct);
        return members.FirstOrDefault(m => string.Equals(m.Email, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Member?> GetMemberByOidAsync(string oid, CancellationToken ct)
    {
        EnsureWrites();
        var members = await AllMembersAsync(ct);
        return members.FirstOrDefault(m => string.Equals(m.Oid, oid, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<Member>> ListMembersAsync(CancellationToken ct)
    {
        EnsureWrites();
        var members = await AllMembersAsync(ct);
        return members.OrderByDescending(m => m.InvitedAt).ToList();
    }

    public async Task<Member?> GetMemberByIdAsync(string id, CancellationToken ct)
    {
        EnsureWrites();
        try
        {
            var resp = await store.Members.GetEntityAsync<TableEntity>(MembersPartition, id, cancellationToken: ct);
            return Deserialize<Member>(resp.Value) with { Etag = EncodeEtag(resp.Value.ETag) };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Member> UpsertMemberAsync(Member member, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        var entity = Entity(MembersPartition, member.Id, member);
        var resp = ifMatch is null
            ? await store.Members.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct)
            : await store.Members.UpdateEntityAsync(entity, DecodeEtag(ifMatch), TableUpdateMode.Replace, ct);
        return member with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task DeleteMemberAsync(string id, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await store.Members.DeleteEntityAsync(MembersPartition, id, DecodeEtag(ifMatch), ct);
    }

    private async Task<List<Member>> AllMembersAsync(CancellationToken ct)
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {MembersPartition}");
        var entities = await QueryAsync(store.Members, filter, ct);
        return entities.Select(e => Deserialize<Member>(e) with { Etag = EncodeEtag(e.ETag) }).ToList();
    }

    // ---- Drafts ---------------------------------------------------------------
    public async Task<Draft?> GetDraftAsync(string id, string authorId, CancellationToken ct)
    {
        EnsureWrites();
        try
        {
            var resp = await store.Drafts.GetEntityAsync<TableEntity>(authorId, id, cancellationToken: ct);
            var draft = Deserialize<Draft>(resp.Value) with { Etag = EncodeEtag(resp.Value.ETag) };
            return draft with { Body = await body.GetAsync(DraftBodyPrefix, draft.Id, ct) };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Draft>> ListDraftsByAuthorAsync(string authorId, CancellationToken ct)
    {
        EnsureWrites();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {authorId}");
        var entities = await QueryAsync(store.Drafts, filter, ct);
        var drafts = entities
            .Select(e => Deserialize<Draft>(e) with { Etag = EncodeEtag(e.ETag) })
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
        return await WithBodiesAsync(drafts, DraftBodyPrefix, ct);
    }

    public async Task<Draft> UpsertDraftAsync(Draft draft, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await body.PutAsync(DraftBodyPrefix, draft.Id, draft.Body, ct);
        var entity = DraftEntity(draft);
        var resp = ifMatch is null
            ? await store.Drafts.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct)
            : await store.Drafts.UpdateEntityAsync(entity, DecodeEtag(ifMatch), TableUpdateMode.Replace, ct);
        return draft with { Etag = EncodeEtag(resp.Headers.ETag!.Value) };
    }

    public async Task DeleteDraftAsync(string id, string authorId, string? ifMatch, CancellationToken ct)
    {
        EnsureWrites();
        await store.Drafts.DeleteEntityAsync(authorId, id, DecodeEtag(ifMatch), ct);
        await body.DeleteAsync(DraftBodyPrefix, id, ct);
    }

    // ---- Workflow -------------------------------------------------------------
    public async Task<Article> SubmitDraftAsync(string draftId, string authorId, CancellationToken ct)
    {
        EnsureWrites();

        Draft draft;
        try
        {
            var read = await store.Drafts.GetEntityAsync<TableEntity>(authorId, draftId, cancellationToken: ct);
            draft = Deserialize<Draft>(read.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Draft {draftId} not found for author {authorId}.");
        }

        var draftBody = await body.GetAsync(DraftBodyPrefix, draftId, ct);

        var article = new Article(
            Id:             draft.Id,
            // Public URL is a {slug}-{shortId} hybrid: readable base from the title
            // plus a stable, collision-proof fragment of the content id.
            Slug:           BuildPublicSlug(draft.Title, draft.Id),
            Title:          draft.Title,
            Summary:        draft.Summary,
            Body:           draftBody,
            Author:         draft.AuthorName,
            PublishedAt:    DateTime.UtcNow, // submission time; updated on publish
            ReadingMinutes: draft.ReadingMinutes,
            Category:       draft.Category,
            Tags:           draft.Tags,
            Status:         "in-review")
        {
            Type = string.Equals(draft.Type, "blog", StringComparison.OrdinalIgnoreCase) ? "blog" : "article",
            AuthorId = draft.AuthorId,
            ReplacesArticleId = draft.ReplacesArticleId,
        };

        await body.PutAsync(ArticleBodyPrefix, article.Id, draftBody, ct);
        var upserted = await store.Articles.UpsertEntityAsync(ArticleEntity(article), TableUpdateMode.Replace, ct);
        await store.Drafts.DeleteEntityAsync(authorId, draft.Id, ETag.All, ct);
        await body.DeleteAsync(DraftBodyPrefix, draft.Id, ct);
        return article with { Etag = EncodeEtag(upserted.Headers.ETag!.Value) };
    }

    public async Task<Article?> GetArticleAsync(string id, string status, CancellationToken ct)
    {
        EnsureWrites();
        try
        {
            var resp = await store.Articles.GetEntityAsync<TableEntity>(status, id, cancellationToken: ct);
            var article = Deserialize<Article>(resp.Value) with { Etag = EncodeEtag(resp.Value.ETag) };
            return article with { Body = await body.GetAsync(ArticleBodyPrefix, article.Id, ct) };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Draft> CreateRevisionDraftAsync(string articleId, string editorOid, string editorName, CancellationToken ct)
    {
        EnsureWrites();

        var published = await GetArticleAsync(articleId, "published", ct)
            ?? throw new InvalidOperationException($"Published article {articleId} not found.");

        // Resume an in-progress revision the editor already started for this article,
        // otherwise mint a fresh draft id (distinct from the live article's id so their
        // body blobs never collide).
        var existing = (await ListDraftsByAuthorAsync(editorOid, ct))
            .FirstOrDefault(d => d.ReplacesArticleId == articleId);

        var now = DateTime.UtcNow;
        var draft = new Draft(
            Id:                existing?.Id ?? Guid.NewGuid().ToString("N"),
            AuthorId:          editorOid,
            AuthorName:        editorName,
            Type:              published.Type,
            Title:             published.Title,
            Slug:              published.Slug,
            Summary:           published.Summary,
            Body:              published.Body,
            Category:          published.Category,
            Tags:              published.Tags,
            ReadingMinutes:    published.ReadingMinutes,
            CreatedAt:         existing?.CreatedAt ?? now,
            UpdatedAt:         now,
            ReplacesArticleId: articleId);

        return await UpsertDraftAsync(draft, existing?.Etag, ct);
    }

    public async Task<Article> PublishArticleAsync(string id, CancellationToken ct)
    {
        EnsureWrites();

        // Read the in-review article first so we can tell a fresh publish from a revision
        // that must replace an existing published article in place.
        var review = await GetArticleAsync(id, "in-review", ct)
            ?? throw new InvalidOperationException($"Article {id} is not in 'in-review'.");

        if (string.IsNullOrEmpty(review.ReplacesArticleId))
            return await TransitionAsync(id, fromStatus: "in-review", toStatus: "published",
                update: a => a with { PublishedAt = DateTime.UtcNow, RejectionReason = null }, ct);

        // Revision: overwrite the target published article's content in place, keeping its
        // id, slug and original published date so the public URL never changes.
        var target = await GetArticleAsync(review.ReplacesArticleId, "published", ct)
            ?? throw new InvalidOperationException($"Target published article {review.ReplacesArticleId} not found.");

        var replacement = target with
        {
            Title          = review.Title,
            Summary        = review.Summary,
            Category       = review.Category,
            Tags           = review.Tags,
            ReadingMinutes = review.ReadingMinutes,
            Type           = review.Type,
            Status         = "published",
            RejectionReason     = null,
            ReplacesArticleId   = null,
            DeletionRequestedBy = null,
            DeletionRequestedAt = null,
            Etag           = null,
        };

        await body.PutAsync(ArticleBodyPrefix, target.Id, review.Body, ct);
        var saved = await store.Articles.UpsertEntityAsync(ArticleEntity(replacement), TableUpdateMode.Replace, ct);

        // Remove the in-review revision and its (now redundant) body blob.
        await store.Articles.DeleteEntityAsync("in-review", id, ETag.All, ct);
        await body.DeleteAsync(ArticleBodyPrefix, id, ct);

        return replacement with
        {
            Etag = EncodeEtag(saved.Headers.ETag!.Value),
            Body = review.Body,
        };
    }

    public Task<Article> RequestArticleDeletionAsync(string id, string requesterOid, CancellationToken ct)
        => UpdatePublishedInPlaceAsync(id,
            a => a with { DeletionRequestedBy = requesterOid, DeletionRequestedAt = DateTime.UtcNow }, ct);

    public Task<Article> CancelArticleDeletionAsync(string id, CancellationToken ct)
        => UpdatePublishedInPlaceAsync(id,
            a => a with { DeletionRequestedBy = null, DeletionRequestedAt = null }, ct);

    // Reads + rewrites a published row in place (same partition) without the
    // read-write-delete dance of TransitionAsync, which would delete the row it
    // just wrote when source and target status match. Body blob is left untouched.
    private async Task<Article> UpdatePublishedInPlaceAsync(string id, Func<Article, Article> update, CancellationToken ct)
    {
        EnsureWrites();
        Article source;
        try
        {
            var read = await store.Articles.GetEntityAsync<TableEntity>("published", id, cancellationToken: ct);
            source = Deserialize<Article>(read.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Published article {id} not found.");
        }

        var updated = update(source with { Etag = null });
        var saved = await store.Articles.UpsertEntityAsync(ArticleEntity(updated), TableUpdateMode.Replace, ct);
        return updated with { Etag = EncodeEtag(saved.Headers.ETag!.Value) };
    }

    public async Task<Draft> ReviseArticleAsync(string id, string feedback, CancellationToken ct)
    {
        EnsureWrites();
        Article source;
        try
        {
            var read = await store.Articles.GetEntityAsync<TableEntity>("in-review", id, cancellationToken: ct);
            source = Deserialize<Article>(read.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Article {id} not found in in-review status.");
        }

        if (string.IsNullOrEmpty(source.AuthorId))
            throw new InvalidOperationException("Article has no AuthorId — cannot create revision draft.");

        var articleBody = await body.GetAsync(ArticleBodyPrefix, id, ct);
        var now = DateTime.UtcNow;
        var draft = new Draft(
            Id:               source.Id,
            AuthorId:         source.AuthorId,
            AuthorName:       source.Author,
            Type:             source.Type,
            Title:            source.Title,
            Slug:             source.Slug,
            Summary:          source.Summary,
            Body:             articleBody,
            Category:         source.Category,
            Tags:             source.Tags,
            ReadingMinutes:   source.ReadingMinutes,
            CreatedAt:        now,
            UpdatedAt:        now,
            RevisionFeedback: feedback,
            ReplacesArticleId: source.ReplacesArticleId);

        await body.PutAsync(DraftBodyPrefix, draft.Id, articleBody, ct);
        var upserted = await store.Drafts.UpsertEntityAsync(DraftEntity(draft), TableUpdateMode.Replace, ct);
        await store.Articles.DeleteEntityAsync("in-review", id, ETag.All, ct);
        await body.DeleteAsync(ArticleBodyPrefix, id, ct);
        return draft with { Etag = EncodeEtag(upserted.Headers.ETag!.Value) };
    }

    private async Task<Article> TransitionAsync(
        string id, string fromStatus, string toStatus, Func<Article, Article> update, CancellationToken ct)
    {
        EnsureWrites();
        // Status is the partition key, so we read from the source partition, write
        // to the target, then delete the source. Not atomic; admin actions are rare
        // and re-runs are idempotent (the source is gone, surfacing a clean 404).
        // The body blob is keyed by id, not status, so it never moves.
        Article source;
        try
        {
            var read = await store.Articles.GetEntityAsync<TableEntity>(fromStatus, id, cancellationToken: ct);
            source = Deserialize<Article>(read.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException($"Article {id} is not in '{fromStatus}'.");
        }

        var transitioned = update(source with { Status = toStatus, Etag = null });

        var created = await store.Articles.UpsertEntityAsync(ArticleEntity(transitioned), TableUpdateMode.Replace, ct);
        await store.Articles.DeleteEntityAsync(fromStatus, id, ETag.All, ct);
        return transitioned with
        {
            Etag = EncodeEtag(created.Headers.ETag!.Value),
            Body = await body.GetAsync(ArticleBodyPrefix, id, ct),
        };
    }

    // ---- helpers --------------------------------------------------------------

    /// <summary>
    /// Builds the public URL slug as <c>{slug}-{shortId}</c> — a readable base
    /// derived from the title plus the first 8 chars of the content id. The id
    /// fragment keeps the slug unique (no collision handling needed) and stable
    /// across edits (the id never changes), while staying human-friendly.
    /// </summary>
    private static string BuildPublicSlug(string title, string id)
    {
        var baseSlug = Slugify(title);
        if (baseSlug.Length == 0) baseSlug = "post";
        var shortId = id.Length >= 8 ? id[..8] : id;
        return $"{baseSlug}-{shortId}";
    }

    /// <summary>Lower-cases, keeps a–z/0–9, collapses every other run of
    /// characters to a single dash, trims dashes, and caps the length so the
    /// final hybrid slug fits the 120-char column.</summary>
    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var lastDash = false;
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 100 ? slug[..100].Trim('-') : slug;
    }

    private void EnsureWrites()
    {
        if (!store.IsConfigured)
            throw new InvalidOperationException(
                "ContentRepository cannot write: storage is not configured. Run setup.ps1 + start.ps1, " +
                "or supply Storage__ConnectionString.");
    }

    /// <summary>Build a metadata entity. The model's <c>Etag</c> is blanked in the
    /// stored JSON (the table tracks ETag natively).</summary>
    private static TableEntity Entity<T>(string partitionKey, string rowKey, T model) =>
        new(partitionKey, rowKey) { ["Json"] = Serialize(model) };

    /// <summary>Article entity: body blanked (lives in a blob), slug exposed as a
    /// queryable column for slug lookups.</summary>
    private static TableEntity ArticleEntity(Article article)
    {
        var e = new TableEntity(article.Status, article.Id)
        {
            ["Json"] = Serialize(article with { Body = string.Empty, Etag = null }),
            ["Slug"] = article.Slug,
        };
        return e;
    }

    private static TableEntity DraftEntity(Draft draft) =>
        new(draft.AuthorId, draft.Id) { ["Json"] = Serialize(draft with { Body = string.Empty, Etag = null }) };

    private async Task<IReadOnlyList<Article>> WithBodiesAsync(List<Article> articles, string prefix, CancellationToken ct)
    {
        var bodies = await Task.WhenAll(articles.Select(a => body.GetAsync(prefix, a.Id, ct)));
        for (var i = 0; i < articles.Count; i++) articles[i] = articles[i] with { Body = bodies[i] };
        return articles;
    }

    private async Task<IReadOnlyList<Draft>> WithBodiesAsync(List<Draft> drafts, string prefix, CancellationToken ct)
    {
        var bodies = await Task.WhenAll(drafts.Select(d => body.GetAsync(prefix, d.Id, ct)));
        for (var i = 0; i < drafts.Count; i++) drafts[i] = drafts[i] with { Body = bodies[i] };
        return drafts;
    }

    private static string Serialize<T>(T model) => JsonSerializer.Serialize(model, JsonOpts);

    private static T Deserialize<T>(TableEntity entity) =>
        JsonSerializer.Deserialize<T>(entity.GetString("Json") ?? "{}", JsonOpts)!;

    private static string EncodeEtag(ETag etag) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(etag.ToString()));

    private static ETag DecodeEtag(string? ifMatch)
    {
        if (string.IsNullOrEmpty(ifMatch)) return ETag.All;
        try { return new ETag(Encoding.UTF8.GetString(Convert.FromBase64String(ifMatch))); }
        catch (FormatException) { return new ETag(ifMatch); }
    }

    private static async Task<List<TableEntity>> QueryAsync(TableClient table, string? filter, CancellationToken ct)
    {
        var results = new List<TableEntity>();
        var pageable = filter is null
            ? table.QueryAsync<TableEntity>(cancellationToken: ct)
            : table.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct);
        await foreach (var entity in pageable.WithCancellation(ct))
            results.Add(entity);
        return results;
    }
}
