using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class ResourceInput
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = "";

    [Required, StringLength(500, MinimumLength = 10)]
    public string Description { get; set; } = "";

    [Required, Url, StringLength(500)]
    public string Url { get; set; } = "";

    [Required, StringLength(60)]
    public string Category { get; set; } = "";
}
