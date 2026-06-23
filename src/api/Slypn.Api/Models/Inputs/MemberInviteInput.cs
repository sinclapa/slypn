using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class MemberInviteInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = "";

    /// <summary>Exactly one of: Admin, Contributor, Member. A member holds a single role.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "A role is required.")]
    [MaxLength(1, ErrorMessage = "A member can have only one role.")]
    public List<string> Roles { get; set; } = new();
}
