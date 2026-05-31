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

    /// <summary>Where invitees land after accepting. Should be the production SWA URL in prod.</summary>
    public string InviteRedirectUrl { get; set; } = "http://localhost:5173/";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId)     &&
        !string.IsNullOrWhiteSpace(ClientId)     &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
