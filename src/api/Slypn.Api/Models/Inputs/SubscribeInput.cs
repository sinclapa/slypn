using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class SubscribeInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [StringLength(120)]
    public string? DisplayName { get; set; }
}
