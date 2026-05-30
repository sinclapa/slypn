using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Creates the SLYPN database and the six content containers on startup
/// if Cosmos is configured. No-op when Endpoint/Key are absent so the API
/// keeps starting on a fresh checkout (still serves mock data until #14).
/// </summary>
public sealed class CosmosBootstrapper(
    IOptions<CosmosOptions> options,
    ILogger<CosmosBootstrapper> logger) : IHostedService
{
    // Partition keys per docs/data-model.md.
    private static readonly (string Container, string PartitionKey)[] Containers =
    [
        ("articles",    "/status"),
        ("drafts",      "/authorId"),
        ("events",      "/yearMonth"),
        ("resources",   "/category"),
        ("newsletters", "/year"),
        ("members",     "/id"),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Endpoint) || string.IsNullOrWhiteSpace(opts.Key))
        {
            logger.LogInformation(
                "Cosmos endpoint/key not configured; skipping bootstrap. The API will continue with mock data.");
            return;
        }

        var isLocalEmulator =
            opts.Endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            opts.Endpoint.Contains("127.0.0.1", StringComparison.Ordinal);

        var clientOptions = new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            ApplicationName = "Slypn.Api",
        };
        if (isLocalEmulator)
        {
            // Cosmos DB Linux Emulator uses a self-signed cert in dev.
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        using var client = new CosmosClient(opts.Endpoint, opts.Key, clientOptions);

        var dbResponse = await client.CreateDatabaseIfNotExistsAsync(opts.Database, cancellationToken: ct);
        logger.LogInformation("Cosmos database ready: {Database}", opts.Database);

        foreach (var (container, partitionKey) in Containers)
        {
            await dbResponse.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(container, partitionKey),
                cancellationToken: ct);
            logger.LogInformation("Cosmos container ready: {Container} ({PartitionKey})", container, partitionKey);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
