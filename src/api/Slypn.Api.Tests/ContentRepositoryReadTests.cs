using Azure.Data.Tables;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

// Storage is unconfigured, so ContentRepository serves reads from MockDataService
// and rejects all writes / member / subscriber / draft access via EnsureWrites.
file sealed class UnconfiguredTableStore : ITableStore
{
    public bool IsConfigured => false;
    public TableClient Articles    => throw new NotSupportedException();
    public TableClient Drafts      => throw new NotSupportedException();
    public TableClient Events      => throw new NotSupportedException();
    public TableClient Resources   => throw new NotSupportedException();
    public TableClient Newsletters => throw new NotSupportedException();
    public TableClient Members     => throw new NotSupportedException();
    public TableClient Subscribers => throw new NotSupportedException();
}

file sealed class NoopBodyStore : IContentBodyStore
{
    public bool IsConfigured => false;
    public Task PutAsync(string prefix, string id, string html, CancellationToken ct) => Task.CompletedTask;
    public Task<string> GetAsync(string prefix, string id, CancellationToken ct) => Task.FromResult("");
    public Task<BlobDownload?> TryOpenFileAsync(string prefix, string id, CancellationToken ct) => Task.FromResult<BlobDownload?>(null);
    public Task PutFileAsync(string prefix, string id, Stream content, string contentType, CancellationToken ct) => Task.CompletedTask;
    public Task DeleteAsync(string prefix, string id, CancellationToken ct) => Task.CompletedTask;
}

public class ContentRepositoryReadTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static ContentRepository Repo() =>
        new(new UnconfiguredTableStore(), new NoopBodyStore(), new MockDataService());

    [Fact]
    public void SupportsWrites_is_false_when_unconfigured()
    {
        Assert.False(Repo().SupportsWrites);
    }

    [Fact]
    public async Task Lists_articles_from_mock_data()
    {
        var articles = await Repo().ListArticlesAsync(null, Ct);
        Assert.NotEmpty(articles);
        Assert.All(articles, a => Assert.Equal("article", a.Type));
    }

    [Fact]
    public async Task Filters_articles_by_status()
    {
        var published = await Repo().ListArticlesAsync("published", Ct);
        Assert.All(published, a => Assert.Equal("published", a.Status));
    }

    [Fact]
    public async Task Lists_blog_posts_from_mock_data()
    {
        var blogs = await Repo().ListBlogPostsAsync(null, Ct);
        Assert.All(blogs, b => Assert.Equal("blog", b.Type));
    }

    [Fact]
    public async Task Gets_article_by_slug_or_id()
    {
        var bySlug = await Repo().GetArticleBySlugAsync("working-with-parkinsons", Ct);
        Assert.NotNull(bySlug);
        var byId = await Repo().GetArticleBySlugAsync("a1", Ct);
        Assert.NotNull(byId);
        var missing = await Repo().GetArticleBySlugAsync("does-not-exist", Ct);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Lists_events_resources_newsletters_from_mock()
    {
        var repo = Repo();
        Assert.NotEmpty(await repo.ListEventsAsync(false, Ct));
        Assert.NotEmpty(await repo.ListResourcesAsync(Ct));
        var newsletters = await repo.ListNewslettersAsync(Ct);
        Assert.NotEmpty(newsletters);
        // newsletters come back newest-first
        Assert.True(newsletters[0].IssueDate >= newsletters[^1].IssueDate);
    }

    [Fact]
    public async Task GetEventById_returns_null_when_unconfigured()
    {
        Assert.Null(await Repo().GetEventByIdAsync("e1", Ct));
    }

    [Fact]
    public async Task Article_with_neighbours_middle_has_both_prev_and_next()
    {
        // List order is newest-first: a1(May-12) a2(Apr-28) a3(Apr-10) a4(Mar-22) a5(Mar-04)
        // a3 is at index 2: prev = a2 (newer, above in list), next = a4 (older, below in list).
        var article = await Repo().GetArticleWithNeighboursAsync("support-network-at-any-age", Ct);
        Assert.NotNull(article);
        Assert.NotNull(article.Prev);
        Assert.NotNull(article.Next);
        Assert.Equal("medication-side-effects", article.Prev!.Slug);
        Assert.Equal("sleep-exercise-parkinsons", article.Next!.Slug);
    }

    [Fact]
    public async Task Article_with_neighbours_newest_has_no_prev()
    {
        // a1 is first in the list (newest) — nothing above it, so no prev.
        var article = await Repo().GetArticleWithNeighboursAsync("working-with-parkinsons", Ct);
        Assert.NotNull(article);
        Assert.Null(article.Prev);
        Assert.NotNull(article.Next);
    }

    [Fact]
    public async Task Event_with_neighbours_uses_sorted_list_in_mock_mode()
    {
        var events = await Repo().ListEventsAsync(false, Ct);
        var first = events.OrderBy(e => e.StartsAt).First();
        var detail = await Repo().GetEventWithNeighboursAsync(first.Id, Ct);
        Assert.NotNull(detail);
        Assert.Null(detail.Prev);
        Assert.NotNull(detail.Next);
    }

    [Fact]
    public async Task Writes_throw_when_unconfigured()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo().CreateArticleAsync(new ArticleInput(), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo().CreateResourceAsync(new ResourceInput(), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Repo().PutNewsletterFileAsync("n1", new MemoryStream(), "application/pdf", "issue.pdf", null, Ct));
    }

    [Fact]
    public async Task Member_and_draft_reads_throw_when_unconfigured()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo().ListMembersAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo().GetMemberByEmailAsync("a@b.com", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo().ListDraftsByAuthorAsync("oid", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo().ListSubscribersAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo().GetSubscriberByEmailAsync("a@b.com", Ct));
    }

    // ── No-op revision guard ────────────────────────────────────────────────────
    // A revision draft starts as a copy of the article it replaces, so submitting one
    // untouched would queue an identical item for an admin to approve for nothing.

    private static Draft RevisionDraft(string title = "T", string summary = "S", string category = "Community", int minutes = 4) =>
        new("d1", "oid", "Ann", "article", title, "slug", summary, "", category, minutes,
            DateTime.UtcNow, DateTime.UtcNow, null, "a1");

    private static Article PublishedTarget(string title = "T", string summary = "S", string body = "<p>hi</p>", string category = "Community", int minutes = 4) =>
        new("a1", "slug", title, summary, body, "Ann", DateTime.UtcNow, minutes, category);

    [Fact]
    public void IsSameContent_is_true_for_an_untouched_revision()
    {
        Assert.True(ContentRepository.IsSameContent(RevisionDraft(), "<p>hi</p>", PublishedTarget()));
    }

    [Theory]
    [InlineData("Changed", "S", "Community", 4, "<p>hi</p>")]
    [InlineData("T", "Changed", "Community", 4, "<p>hi</p>")]
    [InlineData("T", "S", "Treatment", 4, "<p>hi</p>")]
    [InlineData("T", "S", "Community", 9, "<p>hi</p>")]
    [InlineData("T", "S", "Community", 4, "<p>edited</p>")]
    public void IsSameContent_is_false_when_any_authored_field_differs(
        string title, string summary, string category, int minutes, string draftBody)
    {
        Assert.False(ContentRepository.IsSameContent(
            RevisionDraft(title, summary, category, minutes), draftBody, PublishedTarget()));
    }

    [Fact]
    public void IsSameContent_ignores_surrounding_whitespace()
    {
        // Trailing newlines from the editor are not an edit.
        Assert.True(ContentRepository.IsSameContent(
            RevisionDraft(title: "  T  "), "\n<p>hi</p>\n", PublishedTarget()));
    }


    [Fact]
    public void ContentUnchanged_is_its_own_exception_type()
    {
        // The Submit endpoint answers 409 for this and 400 for every other
        // InvalidOperationException, so it must not be one of those.
        var ex = new Slypn.Api.Services.ContentUnchangedException("nothing to review");
        Assert.IsNotType<InvalidOperationException>(ex);
        Assert.Equal("nothing to review", ex.Message);
    }

}
