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
    .ConfigureLogging((context, logging) =>
    {
        // Logs must be wired through ConfigureLogging, not ConfigureServices,
        // so they attach to the Functions host's ILoggerFactory in isolated worker.
        var otelOpts = new OtelOptions();
        context.Configuration.GetSection(OtelOptions.SectionName).Bind(otelOpts);
        if (!otelOpts.IsConfigured) return;

        logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(otelOpts.ServiceName,
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.1")
                .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", otelOpts.Env) }));
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
            o.AddOtlpExporter(opts =>
            {
                opts.Endpoint = new Uri(otelOpts.Endpoint!);
                opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                if (!string.IsNullOrWhiteSpace(otelOpts.Headers))
                    opts.Headers = otelOpts.Headers;
            });
        });
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
                ? ActivatorUtilities.CreateInstance<CiamInviteService>(sp)
                : ActivatorUtilities.CreateInstance<LoggingInviteService>(sp);
        });
        services.AddSingleton<IEntraUserService, EntraUserService>();

        // ---- OpenTelemetry (traces + metrics only — logs handled in ConfigureLogging) ---
        services
            .AddOptions<OtelOptions>()
            .Bind(context.Configuration.GetSection(OtelOptions.SectionName));

        var otelOpts = new OtelOptions();
        context.Configuration.GetSection(OtelOptions.SectionName).Bind(otelOpts);

        if (otelOpts.IsConfigured)
        {
            void ConfigureExporter(OpenTelemetry.Exporter.OtlpExporterOptions options)
            {
                options.Endpoint = new Uri(otelOpts.Endpoint!);
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                if (!string.IsNullOrWhiteSpace(otelOpts.Headers))
                    options.Headers = otelOpts.Headers;
            }

            services
                .AddOpenTelemetry()
                .ConfigureResource(r => r
                    .AddService(otelOpts.ServiceName)
                    .AddAttributes(new[] { new KeyValuePair<string, object>("deployment.environment", otelOpts.Env) }))
                .WithTracing(tracing => tracing
                    .AddSource(OtelSources.ApiName)
                    .AddSource("Azure.*")
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(ConfigureExporter))
                .WithMetrics(metrics => metrics
                    .AddMeter("Slypn.Api")
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(ConfigureExporter));
        }
    })
    .Build();

await host.RunAsync();
