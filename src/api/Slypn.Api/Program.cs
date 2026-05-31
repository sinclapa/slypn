using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<JwtMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<WorkerOptions>(options =>
        {
            options.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        });

        services.AddSingleton<IMockDataService, MockDataService>();
        services.AddSingleton<IContentRepository, ContentRepository>();
        services.AddSingleton<IHtmlSanitizer, HtmlSanitizer>();

        services
            .AddOptions<CosmosOptions>()
            .Bind(context.Configuration.GetSection(CosmosOptions.SectionName));
        services.AddSingleton<ICosmosService, CosmosService>();
        services.AddHostedService<CosmosBootstrapper>();

        services
            .AddOptions<StorageOptions>()
            .Bind(context.Configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IBlobService, BlobService>();

        services
            .AddOptions<EntraOptions>()
            .Bind(context.Configuration.GetSection(EntraOptions.SectionName));
        services.AddSingleton<IJwtValidator, EntraJwtValidator>();

        services
            .AddOptions<GraphOptions>()
            .Bind(context.Configuration.GetSection(GraphOptions.SectionName));
        services.AddHttpClient();
        services.AddSingleton<IInviteService>(sp =>
        {
            var graphOpts = sp.GetRequiredService<IOptions<GraphOptions>>().Value;
            return graphOpts.IsConfigured
                ? ActivatorUtilities.CreateInstance<GraphInviteService>(sp)
                : ActivatorUtilities.CreateInstance<LoggingInviteService>(sp);
        });

        // ---- OpenTelemetry ---------------------------------------------------
        services
            .AddOptions<OtelOptions>()
            .Bind(context.Configuration.GetSection(OtelOptions.SectionName));

        var otelOpts = new OtelOptions();
        context.Configuration.GetSection(OtelOptions.SectionName).Bind(otelOpts);

        if (otelOpts.IsConfigured)
        {
            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(otelOpts.ServiceName, serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.1")
                .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", otelOpts.Env) });

            void ConfigureExporter(OpenTelemetry.Exporter.OtlpExporterOptions options)
            {
                options.Endpoint = new Uri(otelOpts.Endpoint!);
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                if (!string.IsNullOrWhiteSpace(otelOpts.Headers))
                {
                    options.Headers = otelOpts.Headers;
                }
            }

            services
                .AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(otelOpts.ServiceName)
                    .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", otelOpts.Env) }))
                .WithTracing(tracing => tracing
                    .AddSource(OtelSources.ApiName)
                    .AddSource("Azure.*")  // captures Cosmos + Storage SDK activities
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(ConfigureExporter))
                .WithMetrics(metrics => metrics
                    .AddMeter("Slypn.Api")
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(ConfigureExporter));

            services.AddLogging(logging => logging.AddOpenTelemetry(o =>
            {
                o.SetResourceBuilder(resourceBuilder);
                o.IncludeScopes = true;
                o.IncludeFormattedMessage = true;
                o.AddOtlpExporter(ConfigureExporter);
            }));
        }
    })
    .Build();

await host.RunAsync();
