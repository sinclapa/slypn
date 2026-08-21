using Azure;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;

namespace Slypn.Api.Tests;

/// <summary>
/// Hand-rolled in-memory IContentRepository for Function unit tests. Reads return
/// the seeded lists; writes echo a constructed entity. Toggle <see cref="Writes"/>
/// to exercise the "writes disabled" branches.
/// </summary>
internal sealed class FakeContentRepository : IContentRepository
{
    public bool Writes = true;
    public bool SupportsWrites => Writes;

    public List<Article> Articles = new();
    public List<Article> Blogs = new();
    public Article? ArticleBySlug;
    public List<CommunityEvent> Events = new();
    public CommunityEvent? EventById;
    public List<Resource> Resources = new();
    public List<Newsletter> Newsletters = new();
    public List<Member> Members = new();
    public Member? MemberByEmail;
    public Member? MemberByOid;
    public Member? MemberById;
    public List<Draft> Drafts = new();
    public Draft? DraftById;

    /// <summary>Set to throw from writes to exercise storage error mapping.</summary>
    public Exception? ThrowOnWrite { get; set; }

    /// <summary>Set to throw from read operations that have catch (RequestFailedException) blocks.</summary>
    public Exception? ThrowOnRead { get; set; }

    private T Guard<T>(T value)
    {
        if (ThrowOnWrite is not null) throw ThrowOnWrite;
        return value;
    }

    /// <summary>Status the last list call asked for. The public list endpoints must
    /// pin this to "published" regardless of the query string, so recording it is
    /// how those tests assert the filter rather than just the status code.</summary>
    public string? LastArticlesStatus { get; private set; }
    public string? LastBlogStatus { get; private set; }

    public Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, CancellationToken ct)
    {
        LastArticlesStatus = status;
        return Task.FromResult<IReadOnlyList<Article>>(Articles);
    }

    public Task<IReadOnlyList<Article>> ListBlogPostsAsync(string? status, CancellationToken ct)
    {
        LastBlogStatus = status;
        return Task.FromResult<IReadOnlyList<Article>>(Blogs);
    }
    public Task<Article?> GetArticleBySlugAsync(string slug, CancellationToken ct)
        => Task.FromResult(ArticleBySlug);
    public Task<Article?> GetArticleWithNeighboursAsync(string slugOrId, CancellationToken ct)
        => Task.FromResult(ArticleBySlug);
    public Task<IReadOnlyList<CommunityEvent>> ListEventsAsync(bool upcomingOnly, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CommunityEvent>>(Events);
    public Task<CommunityEvent?> GetEventByIdAsync(string id, CancellationToken ct)
        => Task.FromResult(EventById);
    public Task<CommunityEvent?> GetEventWithNeighboursAsync(string id, CancellationToken ct)
    {
        if (ThrowOnRead is not null) throw ThrowOnRead;
        return Task.FromResult(EventById);
    }
    public Task<IReadOnlyList<Resource>> ListResourcesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Resource>>(Resources);
    public Task<IReadOnlyList<Newsletter>> ListNewslettersAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Newsletter>>(Newsletters);

    public Task<Article> CreateArticleAsync(ArticleInput input, CancellationToken ct)
        => Task.FromResult(Guard(MakeArticle("new-id", input)));
    public Task<Article> ReplaceArticleAsync(string id, ArticleInput input, string? ifMatch, CancellationToken ct)
        => Task.FromResult(Guard(MakeArticle(id, input)));
    public Task DeleteArticleAsync(string id, string status, string? ifMatch, CancellationToken ct)
        => Guard(Task.CompletedTask);

    public Task<CommunityEvent> CreateEventAsync(EventInput input, string? oid, string? name, CancellationToken ct)
        => Task.FromResult(Guard(MakeEvent("new-id", input, oid, name)));
    public Task<CommunityEvent> ReplaceEventAsync(string id, string oldYm, EventInput input, string? oid, string? name, string? ifMatch, CancellationToken ct)
        => Task.FromResult(Guard(MakeEvent(id, input, oid, name)));
    public Task DeleteEventAsync(string id, string yearMonth, string? ifMatch, CancellationToken ct)
        => Guard(Task.CompletedTask);

    public Task<Resource> CreateResourceAsync(ResourceInput input, CancellationToken ct)
        => Task.FromResult(Guard(new Resource("new-id", input.Title, input.Description, input.Url, input.Category)));
    public Task<Resource> ReplaceResourceAsync(string id, ResourceInput input, string? ifMatch, CancellationToken ct)
        => Task.FromResult(Guard(new Resource(id, input.Title, input.Description, input.Url, input.Category)));
    public Task DeleteResourceAsync(string id, string category, string? ifMatch, CancellationToken ct)
        => Guard(Task.CompletedTask);

    public Task<Newsletter> CreateNewsletterAsync(NewsletterInput input, CancellationToken ct)
        => Task.FromResult(Guard(new Newsletter("new-id", input.Title, input.IssueDate, input.Summary, input.Topics)));
    public Task<Newsletter> ReplaceNewsletterAsync(string id, NewsletterInput input, string? ifMatch, CancellationToken ct)
    {
        var existingFileName = Newsletters.FirstOrDefault(n => n.Id == id)?.FileName;
        return Task.FromResult(Guard(new Newsletter(id, input.Title, input.IssueDate, input.Summary, input.Topics) { FileName = existingFileName }));
    }
    public Task DeleteNewsletterAsync(string id, string? ifMatch, CancellationToken ct)
        => Guard(Task.CompletedTask);

    /// <summary>Stubbed newsletter file, keyed by newsletter id, returned by OpenNewsletterFileAsync.</summary>
    public Dictionary<string, BlobDownload> NewsletterFiles = new();
    public Task<BlobDownload?> OpenNewsletterFileAsync(string id, CancellationToken ct)
        => Task.FromResult(NewsletterFiles.TryGetValue(id, out var f) ? f : null);

    public Task<Newsletter> PutNewsletterFileAsync(string id, Stream content, string contentType, string fileName, string? ifMatch, CancellationToken ct)
    {
        var idx = Newsletters.FindIndex(n => n.Id == id);
        if (idx < 0) throw new RequestFailedException(404, $"Newsletter {id} not found.");

        using var ms = new MemoryStream();
        content.CopyTo(ms);
        NewsletterFiles[id] = new BlobDownload(new MemoryStream(ms.ToArray()), contentType);

        var updated = Newsletters[idx] with { FileName = fileName };
        Newsletters[idx] = updated;
        return Task.FromResult(Guard(updated));
    }

    /// <summary>Set to throw from the next GetMemberByEmailAsync call (e.g. to test lookup-error branches).</summary>
    public Exception? ThrowOnMemberEmailLookup { get; set; }

    public Task<Member?> GetMemberByEmailAsync(string email, CancellationToken ct)
    {
        if (ThrowOnMemberEmailLookup is not null) throw ThrowOnMemberEmailLookup;
        return Task.FromResult(MemberByEmail);
    }
    public Task<Member?> GetMemberByIdAsync(string id, CancellationToken ct)
    {
        if (ThrowOnRead is not null) throw ThrowOnRead;
        return Task.FromResult(MemberById);
    }
    /// <summary>Set to fail the OID member lookup specifically, without disturbing the
    /// other reads. Exercises JwtMiddleware's "storage is down" fallback.</summary>
    public Exception? ThrowOnMemberLookup { get; set; }

    public Task<Member?> GetMemberByOidAsync(string oid, CancellationToken ct)
    {
        if (ThrowOnMemberLookup is not null) throw ThrowOnMemberLookup;
        return Task.FromResult(MemberByOid);
    }
    public Task<IReadOnlyList<Member>> ListMembersAsync(CancellationToken ct)
    {
        if (ThrowOnRead is not null) throw ThrowOnRead;
        return Task.FromResult<IReadOnlyList<Member>>(Members);
    }
    /// <summary>Count of attempted member upserts. ThrowOnWrite is no use for asserting
    /// "nothing was written" on paths that catch their own exceptions, such as the
    /// re-link in MeSelfFunctions — this records the attempt itself.</summary>
    public int MemberUpserts { get; private set; }

    public Task<Member> UpsertMemberAsync(Member member, string? ifMatch, CancellationToken ct)
    {
        MemberUpserts++;
        return Task.FromResult(Guard(member));
    }
    public Task DeleteMemberAsync(string id, string? ifMatch, CancellationToken ct) => Guard(Task.CompletedTask);

    public Task<Draft?> GetDraftAsync(string id, string authorId, CancellationToken ct)
    {
        if (ThrowOnRead is not null) throw ThrowOnRead;
        return Task.FromResult(DraftById);
    }
    public Task<IReadOnlyList<Draft>> ListDraftsByAuthorAsync(string authorId, CancellationToken ct)
    {
        if (ThrowOnRead is not null) throw ThrowOnRead;
        return Task.FromResult<IReadOnlyList<Draft>>(Drafts);
    }
    public Task<Draft> UpsertDraftAsync(Draft draft, string? ifMatch, CancellationToken ct) => Task.FromResult(Guard(draft));
    public Task DeleteDraftAsync(string id, string authorId, string? ifMatch, CancellationToken ct) => Guard(Task.CompletedTask);

    public Task<Article> SubmitDraftAsync(string draftId, string authorId, CancellationToken ct)
        => Task.FromResult(Guard(MakeArticle(draftId, null)));
    public Task<Draft> CreateRevisionDraftAsync(string articleId, string oid, string name, CancellationToken ct)
        => Task.FromResult(Guard(MakeDraft(articleId, oid, name)));
    public Task<Article> RequestArticleDeletionAsync(string id, string oid, CancellationToken ct)
        => Task.FromResult(Guard(MakeArticle(id, null)));
    public Task<Article> CancelArticleDeletionAsync(string id, CancellationToken ct)
        => Task.FromResult(Guard(MakeArticle(id, null)));
    /// <summary>Article returned by <see cref="GetArticleAsync"/>. Null by default so a
    /// lookup means "no article in that partition" — set it to make one exist, e.g. to
    /// stand up a published article that a non-Admin must not be allowed to replace.</summary>
    public Article? ArticleByIdAndStatus;

    /// <summary>Status the last <see cref="GetArticleAsync"/> call asked for.</summary>
    public string? LastArticleLookupStatus { get; private set; }

    public Task<Article?> GetArticleAsync(string id, string status, CancellationToken ct)
    {
        LastArticleLookupStatus = status;
        return Task.FromResult(ArticleByIdAndStatus);
    }
    public Task<Article> PublishArticleAsync(string id, CancellationToken ct) => Task.FromResult(Guard(MakeArticle(id, null)));
    public Task<Draft> ReviseArticleAsync(string id, string feedback, CancellationToken ct)
        => Task.FromResult(Guard(MakeDraft(id, "oid", "name") with { RevisionFeedback = feedback }));

    private static Article MakeArticle(string id, ArticleInput? input) => new(
        id, input?.Slug ?? "slug", input?.Title ?? "Title", input?.Summary ?? "Summary",
        input?.Body ?? "Body", input?.Author ?? "Author", DateTime.UtcNow,
        input?.ReadingMinutes ?? 5, input?.Category ?? "Community",
        input?.Status ?? "published") { Etag = "etag-1" };

    private static CommunityEvent MakeEvent(string id, EventInput input, string? oid, string? name) => new(
        id, input.Title, input.Type, input.StartsAt, input.EndsAt, input.Location,
        input.Description, input.SignupUrl, oid, name) { Etag = "etag-1" };

    private static Draft MakeDraft(string id, string oid, string name) => new(
        id, oid, name, "article", "Title", "slug", "Summary", "Body", "Community",
        5, DateTime.UtcNow, DateTime.UtcNow) { Etag = "etag-1" };
}
