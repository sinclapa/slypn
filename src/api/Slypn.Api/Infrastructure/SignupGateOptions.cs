namespace Slypn.Api.Infrastructure;

/// <summary>
/// Secures the CIAM sign-up gate (<c>/api/auth/allow-signup</c>). The CIAM OAuth
/// bearer can't reach a SWA-managed Function (SWA replaces the Authorization
/// header), so instead we authenticate the callout with a shared secret passed
/// in the extension's Target URL query string, plus verify the callout body's
/// tenant + extension id.
///
/// Each check is skipped when its expected value is unset, so local dev can call
/// the endpoint without configuring anything (mirrors EntraOptions.SkipAuth).
/// </summary>
public sealed class SignupGateOptions
{
    public const string SectionName = "SignupGate";

    /// <summary>Shared secret expected in the <c>?k=</c> query param of the callout URL.</summary>
    public string? Secret { get; set; }

    /// <summary>Expected CIAM tenant id in the callout body (data.tenantId).</summary>
    public string? TenantId { get; set; }

    /// <summary>Expected custom-extension id in the callout body (data.customAuthenticationExtensionId).</summary>
    public string? ExtensionId { get; set; }
}
