using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slypn.Api.Services;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Creates the SLYPN database and the six content containers on startup.
/// Uses the singleton <see cref="ICosmosService"/> so we don't open a second
/// client. No-op when Cosmos is not configured.
/// </summary>
public sealed class CosmosBootstrapper(
    ICosmosService cosmos,
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
        if (!cosmos.IsConfigured)
        {
            logger.LogInformation(
                "Cosmos not configured; skipping container bootstrap. The API continues with mock data.");
            return;
        }

        var dbResponse = await cosmos.Client.CreateDatabaseIfNotExistsAsync(
            cosmos.Database.Id, cancellationToken: ct);
        logger.LogInformation("Cosmos database ready: {Database}", cosmos.Database.Id);

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
