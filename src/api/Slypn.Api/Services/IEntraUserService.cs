namespace Slypn.Api.Services;

public interface IEntraUserService
{
    bool IsConfigured { get; }
    /// <summary>
    /// Deletes the Entra External ID account for the given OID. Best-effort —
    /// logs warnings on failure but does not throw.
    /// </summary>
    Task DeleteUserAsync(string oid, CancellationToken ct);
}
