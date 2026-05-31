namespace Slypn.Api.Infrastructure;

public sealed class EntraOptions
{
    public const string SectionName = "AzureAd";

    /// <summary>Entra External ID authority, e.g. https://slypn.ciamlogin.com/&lt;tenant-id&gt;/v2.0.</summary>
    public string? Authority { get; set; }

    /// <summary>Expected audience claim, e.g. api://&lt;api-client-id&gt;.</summary>
    public string? Audience { get; set; }

    /// <summary>Acceptable issuers. If empty, defaults to a single issuer matching the authority.</summary>
    public string[] ValidIssuers { get; set; } = [];

    /// <summary>Tenant id (kept separately so the OpenID metadata document is reachable).</summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Local-dev escape hatch — bypasses JWT validation and synthesises an Admin principal for
    /// any [RequireRole] endpoint. Set to true in local.settings.json when working without Entra.
    /// MUST be false in any deployed environment.
    /// </summary>
    public bool SkipAuth { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority) &&
        !string.IsNullOrWhiteSpace(Audience);
}
