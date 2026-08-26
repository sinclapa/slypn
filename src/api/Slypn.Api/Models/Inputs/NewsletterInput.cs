using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class NewsletterInput
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = "";

    [Required]
    public DateOnly IssueDate { get; set; }

    [Required, StringLength(1_000, MinimumLength = 10)]
    public string Summary { get; set; } = "";

    /// <summary>Free-text tags, entered as one comma-separated field and split client-side.
    /// Capped per topic and in number: unbounded here would let a single issue carry
    /// arbitrarily large strings, and nothing downstream truncates them.</summary>
    [MaxLength(20, ErrorMessage = "An issue can carry at most 20 topics.")]
    [ItemLength(60)]
    public List<string> Topics { get; set; } = new();
}
