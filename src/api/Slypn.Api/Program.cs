using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    })
    .Build();

await host.RunAsync();
