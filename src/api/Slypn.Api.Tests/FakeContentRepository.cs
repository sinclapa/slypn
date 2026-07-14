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

    public Task<IReadOnlyList<Article>> ListArticlesAsync(string? status, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Article>>(Articles);
    public Task<IReadOnlyList<Article>> ListBlogPostsAsync(string? status, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Article>>(Blogs);
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
        => Task.FromResult(Guard(new Newsletter(id, input.Title, input.IssueDate, input.Summary, input.Topics)));
    public Task DeleteNewsletterAsync(string id, string year, string? ifMatch, CancellationToken ct)
        => Guard(Task.CompletedTask);

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
    public Task<Member?> GetMemberByOidAsync(string oid, CancellationToken ct) => Task.FromResult(MemberByOid);
    public Task<IReadOnlyList<Member>> ListMembersAsync(CancellationToken ct)
    {
        if (ThrowOnRead is not null) throw ThrowOnRead;
        return Task.FromResult<IReadOnlyList<Member>>(Members);
    }
    public Task<Member> UpsertMemberAsync(Member member, string? ifMatch, CancellationToken ct) => Task.FromResult(Guard(member));
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
    public Task<Article?> GetArticleAsync(string id, string status, CancellationToken ct) => Task.FromResult<Article?>(MakeArticle(id, null));
    public Task<Article> PublishArticleAsync(string id, CancellationToken ct) => Task.FromResult(Guard(MakeArticle(id, null)));
    public Task<Draft> ReviseArticleAsync(string id, string feedback, CancellationToken ct)
        => Task.FromResult(Guard(MakeDraft(id, "oid", "name") with { RevisionFeedback = feedback }));

    private static Article MakeArticle(string id, ArticleInput? input) => new(
        id, input?.Slug ?? "slug", input?.Title ?? "Title", input?.Summary ?? "Summary",
        input?.Body ?? "Body", input?.Author ?? "Author", DateTime.UtcNow,
        input?.ReadingMinutes ?? 5, input?.Category ?? "Community",
        input?.Tags ?? new List<string>(), input?.Status ?? "published") { Etag = "etag-1" };

    private static CommunityEvent MakeEvent(string id, EventInput input, string? oid, string? name) => new(
        id, input.Title, input.Type, input.StartsAt, input.EndsAt, input.Location,
        input.Description, input.SignupUrl, oid, name) { Etag = "etag-1" };

    private static Draft MakeDraft(string id, string oid, string name) => new(
        id, oid, name, "article", "Title", "slug", "Summary", "Body", "Community",
        new List<string>(), 5, DateTime.UtcNow, DateTime.UtcNow) { Etag = "etag-1" };
}
