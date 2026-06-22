using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Models;
using Slypn.Api.Services;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Creates the six SLYPN content tables on startup. Uses the singleton
/// <see cref="ITableStore"/> so we don't open a second client. The body-blob
/// container is created lazily by <see cref="ContentBodyStore"/>. No-op when
/// storage is not configured. When SkipAuth is on (local dev), also seeds the
/// test-persona member records so Member Management and /api/me are consistent.
/// </summary>
public sealed class TableBootstrapper(
    ITableStore store,
    IContentRepository repo,
    IOptions<EntraOptions> entra,
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

        if (entra.Value.SkipAuth)
            await SeedDevPersonasAsync(ct);
    }

    /// <summary>
    /// Idempotently upserts the local test personas (admin/contributor/member) as
    /// active member records. Local-dev only — gated on SkipAuth.
    /// </summary>
    private async Task SeedDevPersonasAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var persona in DevPersonas.All)
        {
            var member = new Member(
                Id:          persona.Key,
                Email:       persona.Email,
                DisplayName: persona.Name,
                Roles:       persona.Roles,
                Status:      "active",
                InvitedAt:   now,
                AcceptedAt:  now,
                InvitedBy:   "dev-seed",
                Oid:         persona.Oid);
            try
            {
                await repo.UpsertMemberAsync(member, ifMatch: null, ct);
                logger.LogInformation("Seeded dev persona member: {Email}", persona.Email);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to seed dev persona member {Email}", persona.Email);
            }
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
