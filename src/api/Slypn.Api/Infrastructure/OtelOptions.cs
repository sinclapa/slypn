namespace Slypn.Api.Infrastructure;

public sealed class OtelOptions
{
    public const string SectionName = "Otel";

    /// <summary>OTLP HTTP endpoint (e.g. https://otlp-gateway-prod-eu-west-2.grafana.net/otlp).</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Headers string in the OTLP convention (key=value, comma-separated). For
    /// Grafana Cloud this looks like "Authorization=Basic &lt;base64&gt;".
    /// </summary>
    public string? Headers { get; set; }

    /// <summary>service.name resource attribute. Defaults to "slypn-api".</summary>
    public string ServiceName { get; set; } = "slypn-api";

    /// <summary>deployment.environment resource attribute (dev / prod).</summary>
    public string Env { get; set; } = "dev";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}
