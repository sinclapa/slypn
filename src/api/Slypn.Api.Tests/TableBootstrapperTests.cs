using Azure.Data.Tables;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

file sealed class UnconfiguredStore : ITableStore
{
    public bool IsConfigured => false;
    public TableClient Articles    => throw new NotSupportedException();
    public TableClient Drafts      => throw new NotSupportedException();
    public TableClient Events      => throw new NotSupportedException();
    public TableClient Resources   => throw new NotSupportedException();
    public TableClient Newsletters => throw new NotSupportedException();
    public TableClient Members     => throw new NotSupportedException();
    public TableClient Subscribers => throw new NotSupportedException();
}

public class TableBootstrapperTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task StartAsync_is_noop_when_storage_unconfigured()
    {
        var bootstrapper = new TableBootstrapper(
            new UnconfiguredStore(),
            new FakeContentRepository(),
            Options.Create(new EntraOptions()),
            NullLogger<TableBootstrapper>.Instance);

        // Must complete without throwing even though table handles throw NotSupportedException.
        await bootstrapper.StartAsync(Ct);
    }

    [Fact]
    public async Task StopAsync_completes_immediately()
    {
        var bootstrapper = new TableBootstrapper(
            new UnconfiguredStore(),
            new FakeContentRepository(),
            Options.Create(new EntraOptions()),
            NullLogger<TableBootstrapper>.Instance);

        await bootstrapper.StopAsync(Ct);
    }
}
