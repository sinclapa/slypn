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

    public List<string> Topics { get; set; } = new();
}
