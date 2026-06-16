using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slypn.Api.Services;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Creates the six SLYPN content tables on startup. Uses the singleton
/// <see cref="ITableStore"/> so we don't open a second client. The body-blob
/// container is created lazily by <see cref="ContentBodyStore"/>. No-op when
/// storage is not configured.
/// </summary>
public sealed class TableBootstrapper(
    ITableStore store,
    ILogger<TableBootstrapper> logger) : IHostedService
{
    private static readonly string[] Tables =
        ["articles", "drafts", "events", "resources", "newsletters", "members"];

    public async Task StartAsync(CancellationToken ct)
    {
        if (!store.IsConfigured)
        {
            logger.LogInformation(
                "Table storage not configured; skipping table bootstrap. The API continues with mock data.");
            return;
        }

        foreach (var name in Tables)
        {
            await TableFor(name).CreateIfNotExistsAsync(ct);
            logger.LogInformation("Table ready: {Table}", name);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private Azure.Data.Tables.TableClient TableFor(string name) => name switch
    {
        "articles"    => store.Articles,
        "drafts"      => store.Drafts,
        "events"      => store.Events,
        "resources"   => store.Resources,
        "newsletters" => store.Newsletters,
        "members"     => store.Members,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown table"),
    };
}
