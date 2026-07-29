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

    [Required, StringLength(50_000, MinimumLength = 10)]
    public string Body { get; set; } = "";

    [Required, StringLength(120)]
    public string Author { get; set; } = "";

    [Range(1, 60)]
    public int ReadingMinutes { get; set; } = 5;

    [Required, StringLength(60)]
    public string Category { get; set; } = "";

    [Required, RegularExpression("^(draft|in-review|published|rejected)$",
        ErrorMessage = "Status must be one of: draft, in-review, published, rejected.")]
    public string Status { get; set; } = "draft";
}
