using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

/// <summary>
/// The merge behind a replace.
///
/// <see cref="ContentRepository.ReplaceArticleAsync"/> used to rebuild the row from the caller's
/// input with a positional constructor and no initialiser, so every init-only property fell to its
/// default: a blog post came back an article, authorship and workflow state were nulled, and the
/// original publish date — the ordering key for every list — was reset to now. The write is a
/// TableUpdateMode.Replace, so the loss was durable rather than cosmetic.
///
/// The repository's write path has no test seam (ITableStore hands out concrete TableClients), so
/// the merge is a pure static and is tested here directly. That is the point of it being pure.
/// </summary>
public class ContentRepositoryWriteTests
{
    private static Article Stored() => new(
        "a1", "original-slug", "Original title", "Original summary", "<p>Original body</p>",
        "Ann", new DateTime(2024, 3, 1, 9, 0, 0, DateTimeKind.Utc), 7, "Community", "published")
    {
        Type = "blog",
        AuthorId = "oid-ann",
        ReplacesArticleId = "a0",
        DeletionRequestedBy = "oid-ann",
        DeletionRequestedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        RejectionReason = "needs work",
        Etag = "etag-1",
        CanEdit = true,
    };

    /// <summary>Everything the caller may set, all different from the stored values.</summary>
    private static ArticleInput Hostile() => new()
    {
        Slug = "new-slug",
        Title = "New title",
        Summary = "A summary that is comfortably long enough.",
        Body = "<p>New body</p>",
        Author = "Someone Else",
        ReadingMinutes = 1,
        Category = "Treatment",
        Status = "published",
        Type = "article",   // ignored on merge; the handler refuses a mismatch before we get here
    };

    [Fact]
    public void ApplyInput_keeps_what_the_caller_does_not_own()
    {
        var merged = ContentRepository.ApplyInput(Stored(), Hostile());

        Assert.Equal("blog", merged.Type);                  // the headline bug
        Assert.Equal("oid-ann", merged.AuthorId);
        Assert.Equal("a0", merged.ReplacesArticleId);
        Assert.Equal("oid-ann", merged.DeletionRequestedBy);
        Assert.NotNull(merged.DeletionRequestedAt);
        Assert.Equal("needs work", merged.RejectionReason);
        Assert.Equal("a1", merged.Id);
    }

    [Fact]
    public void ApplyInput_keeps_the_original_publish_date()
    {
        // Its own test because the blast radius is the least obvious: PublishedAt orders every
        // list on the site, so resetting it silently reshuffles the front page.
        var merged = ContentRepository.ApplyInput(Stored(), Hostile());
        Assert.Equal(new DateTime(2024, 3, 1, 9, 0, 0, DateTimeKind.Utc), merged.PublishedAt);
    }

    [Fact]
    public void ApplyInput_applies_everything_the_caller_does_own()
    {
        var merged = ContentRepository.ApplyInput(Stored(), Hostile());

        Assert.Equal("new-slug", merged.Slug);
        Assert.Equal("New title", merged.Title);
        Assert.Equal("A summary that is comfortably long enough.", merged.Summary);
        Assert.Equal("<p>New body</p>", merged.Body);
        Assert.Equal("Someone Else", merged.Author);
        Assert.Equal(1, merged.ReadingMinutes);
        Assert.Equal("Treatment", merged.Category);
        Assert.Equal("published", merged.Status);
    }

    [Fact]
    public void ApplyInput_drops_the_fields_that_are_never_persisted()
    {
        var merged = ContentRepository.ApplyInput(Stored(), Hostile());

        Assert.Null(merged.Etag);
        Assert.Null(merged.CanEdit);   // a per-request projection; must never be written back
        Assert.Null(merged.Prev);
        Assert.Null(merged.Next);
    }

    [Fact]
    public void ApplyInput_carries_over_a_field_it_was_never_told_about()
    {
        // `with` preserves by default, which is why this is a `with` and not a constructor call:
        // a property added to Article next year survives without anyone remembering to list it.
        var stored = Stored() with { RejectionReason = "some future field's worth of state" };
        Assert.Equal("some future field's worth of state",
            ContentRepository.ApplyInput(stored, Hostile()).RejectionReason);
    }
}
