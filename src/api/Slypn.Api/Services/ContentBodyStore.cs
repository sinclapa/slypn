using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

public sealed class ContentBodyStore : IContentBodyStore
{
    private readonly BlobContainerClient? _container;

    public ContentBodyStore(IOptions<StorageOptions> options, ILogger<ContentBodyStore> logger)
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            logger.LogInformation(
                "ContentBodyStore: connection string not configured. Article/draft bodies will not persist.");
            IsConfigured = false;
            return;
        }

        var serviceClient = new BlobServiceClient(opts.ConnectionString);
        _container = serviceClient.GetBlobContainerClient(opts.ContentContainer);
        _container.CreateIfNotExists(PublicAccessType.None);
        IsConfigured = true;
    }

    public bool IsConfigured { get; }

    public async Task PutAsync(string prefix, string id, string html, CancellationToken ct)
    {
        var blob = Blob(prefix, id);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(html ?? string.Empty));
        await blob.UploadAsync(stream,
            new BlobHttpHeaders { ContentType = "text/html; charset=utf-8" },
            cancellationToken: ct);
    }

    public async Task<string> GetAsync(string prefix, string id, CancellationToken ct)
    {
        var blob = Blob(prefix, id);
        try
        {
            // Stream + StreamReader handles zero-byte blobs cleanly (an empty draft
            // body); DownloadContentAsync().Content.ToString() throws on those.
            var resp = await blob.DownloadStreamingAsync(cancellationToken: ct);
            using var reader = new StreamReader(resp.Value.Content, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return string.Empty;
        }
    }

    public async Task PutFileAsync(string prefix, string id, Stream content, string contentType, CancellationToken ct)
    {
        var blob = Blob(prefix, id);
        await blob.UploadAsync(content,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: ct);
    }

    public async Task<BlobDownload?> TryOpenFileAsync(string prefix, string id, CancellationToken ct)
    {
        var blob = Blob(prefix, id);
        try
        {
            var resp = await blob.DownloadStreamingAsync(cancellationToken: ct);
            var contentType = resp.Value.Details.ContentType is { Length: > 0 } ct2
                ? ct2
                : "application/octet-stream";
            return new BlobDownload(resp.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string prefix, string id, CancellationToken ct) =>
        await Blob(prefix, id).DeleteIfExistsAsync(cancellationToken: ct);

    private BlobClient Blob(string prefix, string id)
    {
        if (_container is null) throw new InvalidOperationException("ContentBodyStore is not configured.");
        return _container.GetBlobClient($"{prefix}/{id}");
    }
}
