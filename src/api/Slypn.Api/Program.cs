using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // camelCase + lowercase enum names on every HttpResponseData.WriteAsJsonAsync.
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
    })
    .Build();

await host.RunAsync();
