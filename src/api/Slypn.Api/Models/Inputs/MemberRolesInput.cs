using System.ComponentModel.DataAnnotations;

namespace Slypn.Api.Models.Inputs;

public sealed class MemberRolesInput
{
    /// <summary>One or more of: Admin, Contributor, Member.</summary>
    [Required, MinLength(1)]
    public List<string> Roles { get; set; } = new();
}
