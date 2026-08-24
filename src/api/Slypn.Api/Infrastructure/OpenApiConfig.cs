using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;

namespace Slypn.Api.Infrastructure;

public sealed class OpenApiConfig : IOpenApiConfigurationOptions
{
    public OpenApiConfig(IConfiguration config)
    {
        var env = config[$"{OtelOptions.SectionName}:Env"] ?? "dev";
        var version = config["APP_VERSION"] ?? "0.0.0";
        Info = new OpenApiInfo { Version = version, Title = $"Slypn API [{env}]" };
        DocumentFilters = [new SwaggerOAuthFilter(config)];
    }

    public OpenApiInfo Info { get; set; }
    public List<OpenApiServer> Servers { get; set; } = [];
    public OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V3;
    public bool IncludeRequestingHostName { get; set; } = false;
    public bool ForceHttp { get; set; } = false;
    public bool ForceHttps { get; set; } = false;
    public List<IDocumentFilter> DocumentFilters { get; set; }
}
