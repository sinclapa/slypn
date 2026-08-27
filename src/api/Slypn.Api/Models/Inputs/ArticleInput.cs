using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class ArticleInput
{
    [Required, StringLength(120, MinimumLength = 3)]
    public string Slug { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 10)]
    public string Summary { get; set; } = "";

    // See DraftInput.Body — 200,000 is the HTML backstop, not the authoring limit.
    [Required, StringLength(200_000, MinimumLength = 10)]
    public string Body { get; set; } = "";

    [Required, StringLength(120)]
    public string Author { get; set; } = "";

    [Range(1, 60)]
    public int ReadingMinutes { get; set; } = 5;

    [Required, StringLength(60)]
    public string Category { get; set; } = "";

    /// <summary>"article" or "blog". Required on create — the endpoint is type-agnostic, so the
    /// body has to say what it is making. Optional on replace, where the stored type is preserved;
    /// sending one that disagrees is refused rather than silently converting the item.
    ///
    /// Nullable and not [Required] precisely so "omitted" stays distinguishable from "article".
    /// A default here would reintroduce the bug this was added to fix.</summary>
    [RegularExpression("^(article|blog)$", ErrorMessage = "Type must be one of: article, blog.")]
    public string? Type { get; set; }

    [Required, RegularExpression("^(draft|in-review|published|rejected)$",
        ErrorMessage = "Status must be one of: draft, in-review, published, rejected.")]
    public string Status { get; set; } = "draft";
}
