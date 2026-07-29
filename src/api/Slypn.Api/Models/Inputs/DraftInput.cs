using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

/// <summary>
/// Most fields are optional during drafting — autosave fires while the
/// author is still filling things in. Server-side validation tightens
/// when the draft moves to "in-review" in #28.
/// </summary>
public sealed class DraftInput
{
    [Required, RegularExpression("^(article|blog)$", ErrorMessage = "Type must be 'article' or 'blog'.")]
    public string Type { get; set; } = "article";

    [StringLength(200)] public string Title { get; set; } = "";
    [StringLength(120)] public string Slug { get; set; } = "";
    [StringLength(500)] public string Summary { get; set; } = "";
    [StringLength(50_000)] public string Body { get; set; } = "";
    [StringLength(60)]  public string Category { get; set; } = "";

    [Range(0, 60)]
    public int ReadingMinutes { get; set; }

    [StringLength(1_000)]
    public string? RevisionFeedback { get; set; }
}
