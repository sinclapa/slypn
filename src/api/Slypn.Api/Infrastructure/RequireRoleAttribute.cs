namespace Slypn.Api.Infrastructure;

/// <summary>
/// Marks a Function as requiring a signed-in caller. If <see cref="Roles"/> is non-empty,
/// the caller must hold at least one of the listed roles (logical OR). An empty role list
/// means "authenticated, no specific role".
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequireRoleAttribute(params string[] roles) : Attribute
{
    public string[] Roles { get; } = roles;
}
