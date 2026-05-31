using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class EventInput
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = "";

    [Required, StringLength(60)]
    public string Type { get; set; } = "";

    [Required]
    public DateTimeOffset StartsAt { get; set; }

    [Required]
    public DateTimeOffset EndsAt { get; set; }

    [Required, StringLength(200)]
    public string Location { get; set; } = "";

    [Required, StringLength(2_000)]
    public string Description { get; set; } = "";

    [Url, StringLength(500)]
    public string? SignupUrl { get; set; }
}
