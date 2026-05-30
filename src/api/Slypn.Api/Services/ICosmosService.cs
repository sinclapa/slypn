using Microsoft.Azure.Cosmos;

namespace Slypn.Api.Services;

/// <summary>
/// Single point of access to the SLYPN Cosmos DB account. Container handles
/// are lazy SDK references — they don't require the container to exist
/// (creation happens in CosmosBootstrapper on startup).
///
/// Consumers must check <see cref="IsConfigured"/> before accessing handles;
/// otherwise an InvalidOperationException is thrown.
/// </summary>
public interface ICosmosService
{
    bool IsConfigured { get; }

    CosmosClient Client   { get; }
    Database     Database { get; }

    Container Articles    { get; }
    Container Drafts      { get; }
    Container Events      { get; }
    Container Resources   { get; }
    Container Newsletters { get; }
    Container Members     { get; }
}
