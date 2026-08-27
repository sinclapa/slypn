using System.ComponentModel.DataAnnotations;
using Slypn.Api.Models.Inputs;
using Xunit;

namespace Slypn.Api.Tests;

public class InputValidationTests
{
    private static (bool Ok, List<ValidationResult> Errors) Validate(object o)
    {
        var ctx = new ValidationContext(o);
        var errors = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(o, ctx, errors, validateAllProperties: true);
        return (ok, errors);
    }

    private static ArticleInput ValidArticle() => new()
    {
        Slug = "a-valid-slug", Title = "A good title", Summary = "A sufficiently long summary.",
        Body = "Body content that is long enough.", Author = "Jane", ReadingMinutes = 5,
        Category = "Community", Status = "draft",
    };

    [Fact]
    public void ArticleInput_valid_passes()
    {
        Assert.True(Validate(ValidArticle()).Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    public void ArticleInput_rejects_bad_status(string status)
    {
        var input = ValidArticle();
        input.Status = status;
        Assert.False(Validate(input).Ok);
    }

    [Fact]
    public void ArticleInput_rejects_short_title()
    {
        var input = ValidArticle();
        input.Title = "no";
        Assert.False(Validate(input).Ok);
    }

    [Fact]
    public void EventInput_rejects_non_url_signup()
    {
        var input = new EventInput
        {
            Title = "Coffee", Type = "Coffee meet-up", StartsAt = DateTimeOffset.UtcNow,
            EndsAt = DateTimeOffset.UtcNow.AddHours(1), Location = "Brixton", Description = "Come",
            SignupUrl = "not-a-url",
        };
        Assert.False(Validate(input).Ok);
    }

    [Fact]
    public void EventInput_valid_passes_with_no_signup()
    {
        var input = new EventInput
        {
            Title = "Coffee", Type = "Coffee meet-up", StartsAt = DateTimeOffset.UtcNow,
            EndsAt = DateTimeOffset.UtcNow.AddHours(1), Location = "Brixton", Description = "Come",
        };
        Assert.True(Validate(input).Ok);
    }

    [Fact]
    public void MemberInviteInput_requires_exactly_one_role()
    {
        var valid = new MemberInviteInput { Email = "a@b.com", DisplayName = "A", Roles = { "Admin" } };
        Assert.True(Validate(valid).Ok);

        var none = new MemberInviteInput { Email = "a@b.com", DisplayName = "A" };
        Assert.False(Validate(none).Ok);

        var two = new MemberInviteInput { Email = "a@b.com", DisplayName = "A", Roles = { "Admin", "Member" } };
        Assert.False(Validate(two).Ok);
    }

    [Fact]
    public void MemberInviteInput_rejects_bad_email()
    {
        var input = new MemberInviteInput { Email = "nope", DisplayName = "A", Roles = { "Admin" } };
        Assert.False(Validate(input).Ok);
    }

    [Fact]
    public void MemberRolesInput_requires_one_role()
    {
        Assert.True(Validate(new MemberRolesInput { Roles = { "Contributor" } }).Ok);
        Assert.False(Validate(new MemberRolesInput()).Ok);
    }

    [Fact]
    public void NewsletterInput_validates_length()
    {
        var valid = new NewsletterInput { Title = "May 2026", IssueDate = new DateOnly(2026, 5, 1), Summary = "A long enough summary." };
        Assert.True(Validate(valid).Ok);

        var bad = new NewsletterInput { Title = "x", IssueDate = new DateOnly(2026, 5, 1), Summary = "short" };
        Assert.False(Validate(bad).Ok);
    }

    [Fact]
    public void SubscribeInput_requires_valid_email()
    {
        Assert.True(Validate(new SubscribeInput { Email = "a@b.com" }).Ok);
        Assert.False(Validate(new SubscribeInput { Email = "bad" }).Ok);
    }

    // ── Newsletter topics ───────────────────────────────────────────────────────
    // DataAnnotations validates the property, not its elements, so [StringLength] on a
    // List<string> checks nothing. Topics were unbounded per item until ItemLength.

    private static NewsletterInput NewsletterWith(params string[] topics) => new()
    {
        Title = "An issue",
        IssueDate = new DateOnly(2026, 5, 1),
        Summary = "A summary long enough to pass.",
        Topics = topics.ToList(),
    };

    [Fact]
    public void Newsletter_accepts_topics_within_the_per_item_limit()
    {
        Assert.True(Validate(NewsletterWith("Research", new string('x', 60))).Ok);
    }

    [Fact]
    public void Newsletter_rejects_a_topic_that_is_too_long()
    {
        var (ok, errors) = Validate(NewsletterWith("Research", new string('x', 61)));
        Assert.False(ok);
        Assert.Contains(errors, e => e.ErrorMessage!.Contains("characters or fewer"));
    }

    [Fact]
    public void Newsletter_rejects_too_many_topics()
    {
        var (ok, errors) = Validate(NewsletterWith(Enumerable.Range(0, 21).Select(i => $"t{i}").ToArray()));
        Assert.False(ok);
        Assert.Contains(errors, e => e.ErrorMessage!.Contains("at most 20 topics"));
    }

    [Fact]
    public void ItemLength_ignores_a_collection_it_does_not_understand()
    {
        // Shape is another rule's problem; this one only has an opinion on string length.
        Assert.True(new ItemLengthAttribute(5).IsValid(new List<int> { 1, 2, 3 }));
        Assert.True(new ItemLengthAttribute(5).IsValid(null));
    }

    [Fact]
    public void ItemLength_tolerates_a_null_entry()
    {
        Assert.True(new ItemLengthAttribute(5).IsValid(new List<string?> { null, "ok" }));
    }

}
