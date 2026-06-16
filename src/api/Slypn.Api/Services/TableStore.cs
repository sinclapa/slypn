using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

/// <summary>
/// Singleton wrapper around <see cref="TableServiceClient"/> for the SLYPN API.
/// Backed by the same storage account as <see cref="BlobService"/> via
/// <see cref="StorageOptions.ConnectionString"/>.
///
/// Auth modes:
///   - dev  : connection string (Azurite emulator).
///   - prod : connection string for now; Managed Identity wiring lands in #38 (Phase 6)
///            — swap to `new TableServiceClient(endpoint, new DefaultAzureCredential())`.
///
/// If the connection string is empty the service exposes IsConfigured=false and
/// every table handle throws. Callers must check the flag first.
/// </summary>
public sealed class TableStore : ITableStore
{
    private readonly TableServiceClient? _service;

    public TableStore(IOptions<StorageOptions> options, ILogger<TableStore> logger)
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            logger.LogInformation(
                "TableStore: connection string not configured. " +
                "Table-backed code paths will be unavailable until configuration is supplied.");
            IsConfigured = false;
            return;
        }

        _service = new TableServiceClient(opts.ConnectionString);
        IsConfigured = true;
    }

    public bool IsConfigured { get; }

    public TableClient Articles    => Table("articles");
    public TableClient Drafts      => Table("drafts");
    public TableClient Events      => Table("events");
    public TableClient Resources   => Table("resources");
    public TableClient Newsletters => Table("newsletters");
    public TableClient Members     => Table("members");

    private TableClient Table(string name) =>
        (_service ?? throw NotConfigured()).GetTableClient(name);

    private static InvalidOperationException NotConfigured() =>
        new("Table storage is not configured. Check IsConfigured before accessing handles.");
}
