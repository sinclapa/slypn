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
        Info = new OpenApiInfo { Version = "1.0.0", Title = $"Slypn API [{env}]" };
    }

    public OpenApiInfo Info { get; set; }
    public List<OpenApiServer> Servers { get; set; } = [];
    public OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V3;
    public bool IncludeRequestingHostName { get; set; } = false;
    public bool ForceHttp { get; set; } = false;
    public bool ForceHttps { get; set; } = false;
    public List<IDocumentFilter> DocumentFilters { get; set; } = [];
}
