using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class MemberInviteInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(120, MinimumLength = 1)]
    public string DisplayName { get; set; } = "";

    /// <summary>One or more of: Admin, Contributor, Member.</summary>
    [Required, MinLength(1)]
    public List<string> Roles { get; set; } = new();
}
