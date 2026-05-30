using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<IMockDataService, MockDataService>();

        services
            .AddOptions<CosmosOptions>()
            .Bind(context.Configuration.GetSection(CosmosOptions.SectionName));

        services.AddSingleton<ICosmosService, CosmosService>();
        services.AddHostedService<CosmosBootstrapper>();
    })
    .Build();

await host.RunAsync();
