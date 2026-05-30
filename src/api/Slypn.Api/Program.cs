using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Slypn.Api.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IMockDataService, MockDataService>();
    })
    .Build();

await host.RunAsync();
