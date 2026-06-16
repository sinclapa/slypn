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

        // Export our app's Information/Warning logs (e.g. AllowSignup gate decisions,
        // role enrichment) to OTLP, not just errors — the breadcrumbs we need when
        // diagnosing auth/data issues in prod.
        logging.AddFilter("Slypn", LogLevel.Information);

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
                opts.Endpoint = new Uri(otelOpts.Endpoint!.TrimEnd('/') + "/v1/logs");
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
            .AddOptions<StorageOptions>()
            .Bind(context.Configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<ITableStore, TableStore>();
        services.AddSingleton<IContentBodyStore, ContentBodyStore>();
        services.AddSingleton<IBlobService, BlobService>();
        services.AddHostedService<TableBootstrapper>();

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
            // OTel .NET 1.9+: setting Endpoint programmatically disables auto path-appending,
            // so each signal needs its explicit /v1/* suffix.
            var baseEndpoint = otelOpts.Endpoint!.TrimEnd('/');

            void ConfigureExporter(string signalPath, OpenTelemetry.Exporter.OtlpExporterOptions options)
            {
                options.Endpoint = new Uri(baseEndpoint + signalPath);
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
                    .AddSource("Azure.*")  // captures Table + Blob storage SDK activities
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opts => ConfigureExporter("/v1/traces", opts)))
                .WithMetrics(metrics => metrics
                    .AddMeter("Slypn.Api")
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opts => ConfigureExporter("/v1/metrics", opts)));
        }
    })
    .Build();

await host.RunAsync();
