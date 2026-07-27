using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

file sealed class StubHttpFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

public class ServicesTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static IOptions<StorageOptions> EmptyStorage => Options.Create(new StorageOptions());

    [Fact]
    public async Task LoggingInviteService_reports_unconfigured()
    {
        var svc = new LoggingInviteService(NullLogger<LoggingInviteService>.Instance);
        Assert.False(svc.IsConfigured);
        var result = await svc.SendInviteAsync("a@b.com", "Alice", Ct);
        Assert.False(result.Sent);
        Assert.Null(result.RedeemUrl);
        Assert.Equal("sign-up-url-not-configured", result.Reason);
    }

    [Fact]
    public async Task BlobService_unconfigured_blocks_writes()
    {
        var svc = new BlobService(EmptyStorage, NullLogger<BlobService>.Instance);
        Assert.False(svc.IsConfigured);
        Assert.Equal(3, svc.AllowedContentTypes.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UploadMediaAsync(new MemoryStream(), "image/png", Ct));
        Assert.Throws<InvalidOperationException>(() => svc.GetMediaReadUrl("x.png"));
    }

    [Fact]
    public async Task ContentBodyStore_unconfigured_throws_on_access()
    {
        var svc = new ContentBodyStore(EmptyStorage, NullLogger<ContentBodyStore>.Instance);
        Assert.False(svc.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PutAsync("articles", "a1", "<p>x</p>", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetAsync("articles", "a1", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync("articles", "a1", Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.PutFileAsync("newsletters", "n1", new MemoryStream(), "application/pdf", Ct));
    }

    [Fact]
    public void TableStore_unconfigured_throws_on_handle_access()
    {
        var svc = new TableStore(EmptyStorage, NullLogger<TableStore>.Instance);
        Assert.False(svc.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => svc.Articles);
        Assert.Throws<InvalidOperationException>(() => svc.Members);
    }

    [Fact]
    public async Task EntraUserService_unconfigured_is_noop()
    {
        var svc = new EntraUserService(
            Options.Create(new GraphOptions()), // no client secret
            Options.Create(new EntraOptions()),
            new StubHttpFactory(),
            NullLogger<EntraUserService>.Instance);
        Assert.False(svc.IsConfigured);
        await svc.DeleteUserAsync("oid-1", Ct); // returns without throwing or calling Graph
    }
}
