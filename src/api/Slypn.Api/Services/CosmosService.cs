using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

/// <summary>
/// Singleton wrapper around <see cref="CosmosClient"/> for the SLYPN API.
///
/// Auth modes:
///   - dev    : key-based (emulator + cert bypass when endpoint is localhost)
///   - prod   : key-based for now; Managed Identity wiring lands in #38 (Phase 6)
///              — swap to `new CosmosClient(endpoint, new DefaultAzureCredential(), options)`.
///
/// If the configuration section is empty the service exposes IsConfigured=false
/// and every other property throws. Callers must check the flag first.
/// </summary>
public sealed class CosmosService : ICosmosService, IDisposable
{
    private readonly CosmosClient? _client;
    private readonly Database?     _database;
    private readonly string?       _databaseId;

    public CosmosService(IOptions<CosmosOptions> options, ILogger<CosmosService> logger)
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.Endpoint) || string.IsNullOrWhiteSpace(opts.Key))
        {
            logger.LogInformation(
                "CosmosService: endpoint/key not configured. " +
                "Cosmos-backed code paths will be unavailable until configuration is supplied.");
            IsConfigured = false;
            return;
        }

        _databaseId = opts.Database;
        _client     = BuildClient(opts);
        _database   = _client.GetDatabase(_databaseId);
        IsConfigured = true;
    }

    public bool IsConfigured { get; }

    public CosmosClient Client   => _client   ?? throw NotConfigured();
    public Database     Database => _database ?? throw NotConfigured();

    public Container Articles    => Database.GetContainer("articles");
    public Container Drafts      => Database.GetContainer("drafts");
    public Container Events      => Database.GetContainer("events");
    public Container Resources   => Database.GetContainer("resources");
    public Container Newsletters => Database.GetContainer("newsletters");
    public Container Members     => Database.GetContainer("members");

    private static CosmosClient BuildClient(CosmosOptions opts)
    {
        var isLocalEmulator =
            opts.Endpoint!.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            opts.Endpoint!.Contains("127.0.0.1", StringComparison.Ordinal);

        var clientOptions = new CosmosClientOptions
        {
            ConnectionMode  = ConnectionMode.Gateway,
            ApplicationName = "Slypn.Api",
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
            },
        };

        if (isLocalEmulator)
        {
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        return new CosmosClient(opts.Endpoint, opts.Key, clientOptions);
    }

    private static InvalidOperationException NotConfigured() =>
        new("Cosmos is not configured. Check IsConfigured before accessing handles.");

    public void Dispose() => _client?.Dispose();
}
