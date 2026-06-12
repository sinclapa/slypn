namespace Slypn.Api.Infrastructure;

public sealed class GraphOptions
{
    public const string SectionName = "Graph";

    /// <summary>Entra tenant id used for the token endpoint.</summary>
    public string? TenantId { get; set; }

    /// <summary>Client (application) id of the SLYPN API app registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret for client-credentials auth against Microsoft Graph.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Where invitees land after accepting. Must be an HTTPS URL — Graph rejects http://localhost.</summary>
    public string InviteRedirectUrl { get; set; } = "https://thankful-tree-090006c03.7.azurestaticapps.net/";

    /// <summary>True when a sign-up URL is available for the CIAM invite flow.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(InviteRedirectUrl);

    /// <summary>True when client credentials are available for Graph API calls (e.g. user deletion).</summary>
    public bool HasClientCredentials => !string.IsNullOrWhiteSpace(ClientSecret);
}
