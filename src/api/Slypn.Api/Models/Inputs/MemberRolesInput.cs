using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class MemberRolesInput
{
    /// <summary>Exactly one of: Admin, Contributor, Member. A member holds a single role.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "A role is required.")]
    [MaxLength(1, ErrorMessage = "A member can have only one role.")]
    public List<string> Roles { get; set; } = new();
}
