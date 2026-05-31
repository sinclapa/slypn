using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class RejectionInput
{
    [Required, StringLength(1_000, MinimumLength = 5,
        ErrorMessage = "Feedback must be 5-1000 characters so the author understands the rejection.")]
    public string Feedback { get; set; } = "";
}
