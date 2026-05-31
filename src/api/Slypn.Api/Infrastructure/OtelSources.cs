using System.Diagnostics;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Activity sources owned by the SLYPN API. Names are stable and end up as
/// the `otel.scope.name` attribute on every span — use them to filter.
/// </summary>
public static class OtelSources
{
    public const string ApiName = "Slypn.Api";
    public static readonly ActivitySource Api = new(ApiName);
}
