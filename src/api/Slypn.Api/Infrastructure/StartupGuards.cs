namespace Slypn.Api.Infrastructure;

/// <summary>
/// Configuration checks that fail the host build rather than surface at runtime. A
/// deployment configured to serve open Admin should refuse to start, not serve.
/// </summary>
public static class StartupGuards
{
    /// <summary>
    /// Environment names that mean "a developer's machine or a CI runner". Anything else
    /// is treated as a deployment. Unset counts as local because
    /// <see cref="OtelOptions.Env"/> itself defaults to "dev" — a local.settings.json that
    /// simply omits Otel__Env must keep working.
    /// </summary>
    private static readonly HashSet<string> LocalEnvNames =
        new(StringComparer.OrdinalIgnoreCase) { "dev", "local", "development" };

    /// <summary>
    /// Variables the Azure App Service / Functions host sets on every instance it runs,
    /// and which nothing sets on a developer machine or a CI runner using Core Tools.
    ///
    /// Deliberately NOT WEBSITE_HOSTNAME: Core Tools sets that locally to emulate App
    /// Service, so treating it as a deployment marker refuses to start every `func start`
    /// and took the e2e job down with it. The two below were absent in that same run —
    /// the guard reports the first marker it finds, and it named WEBSITE_HOSTNAME — which
    /// is what makes them usable discriminators.
    /// </summary>
    private static readonly string[] AzureHostMarkers =
        ["WEBSITE_INSTANCE_ID", "WEBSITE_SITE_NAME"];

    /// <summary>
    /// Refuse to start when the auth bypass is enabled anywhere it could be reached.
    ///
    /// <c>AzureAd:SkipAuth</c> short-circuits JWT validation in <see cref="JwtMiddleware"/>
    /// before any other check, synthesising a principal from the X-Slypn-Dev-User header —
    /// so any caller can ask for and receive Admin. Until now nothing but a correctly set
    /// app setting stood between that and a live site.
    ///
    /// Two independent signals, because neither alone is sufficient:
    ///
    /// <list type="bullet">
    /// <item>Running on an Azure host at all. This is the one that matters for PR previews:
    /// they are real, publicly reachable deployments that deliberately run with
    /// Otel__Env=dev, so an environment-name check on its own would wave them through.</item>
    /// <item>An environment name that is not a local one, which catches a deployment whose
    /// Azure markers are absent or renamed.</item>
    /// </list>
    /// </summary>
    /// <param name="skipAuth">The bound <c>AzureAd:SkipAuth</c> value.</param>
    /// <param name="otelEnv">The bound <c>Otel:Env</c> value; null or blank counts as local.</param>
    /// <param name="readEnvironmentVariable">
    /// Indirection so tests can present a fake environment rather than mutating the real one.
    /// </param>
    /// <exception cref="InvalidOperationException">The bypass is enabled outside local dev.</exception>
    public static void EnsureSkipAuthIsLocalOnly(
        bool skipAuth,
        string? otelEnv,
        Func<string, string?> readEnvironmentVariable)
    {
        if (!skipAuth) return;

        var azureMarker = AzureHostMarkers
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(readEnvironmentVariable(name)));
        if (azureMarker is not null)
            throw Refuse($"the app is running on an Azure host ({azureMarker} is set)");

        var envName = string.IsNullOrWhiteSpace(otelEnv) ? "dev" : otelEnv.Trim();
        if (!LocalEnvNames.Contains(envName))
            throw Refuse($"Otel:Env is '{envName}', which is not a local environment");
    }

    private static InvalidOperationException Refuse(string reason) =>
        new($"AzureAd:SkipAuth is true but {reason}. This setting bypasses JWT validation "
            + "entirely and grants Admin to any caller sending an X-Slypn-Dev-User header, so the "
            + "host refuses to start rather than serve an open site. Set AzureAd__SkipAuth=false "
            + "(infra/setup.ps1 does this for the deployed app) and redeploy.");
}
