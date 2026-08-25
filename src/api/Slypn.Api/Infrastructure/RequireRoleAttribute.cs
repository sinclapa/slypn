namespace Slypn.Api.Infrastructure;

/// <summary>
/// Marks a Function as requiring a signed-in caller. If <see cref="Roles"/> is non-empty,
/// the caller must hold at least one of the listed roles (logical OR). An empty role list
/// means "authenticated, no specific role".
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class RequireRoleAttribute(params string[] roles) : Attribute
{
    public string[] Roles { get; } = roles;

    /// <summary>Whether a caller we cannot authenticate is served anyway. See
    /// <see cref="OptionalAuthAttribute"/>; false for a plain [RequireRole].</summary>
    public virtual bool Optional => false;
}

/// <summary>
/// Authenticate when the caller presents a usable token, but never refuse — for public
/// endpoints whose *response* varies by caller (e.g. a canEdit flag) while the content
/// itself is public. Without this the middleware populates no principal at all on an
/// unattributed Function, so such an endpoint cannot tell who is asking.
///
/// Deliberately a subclass rather than a flag on [RequireRole]: the middleware resolves
/// the attribute with GetCustomAttribute&lt;RequireRoleAttribute&gt;(), which already matches
/// subclasses, so nothing else changes — and it makes [RequireRole("Admin", Optional = true)]
/// unexpressible, which would otherwise silently downgrade a valid non-Admin to anonymous.
///
/// Note: when the token validator is not configured, these endpoints serve anonymously
/// rather than failing. That is intended — an auth misconfiguration should not take the
/// public site down. Do not "fix" it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OptionalAuthAttribute : RequireRoleAttribute
{
    public override bool Optional => true;
}
