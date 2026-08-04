using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Services;

public sealed class BlobService : IBlobService
{
    private readonly BlobContainerClient? _container;
    private readonly StorageOptions _opts;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
    };

    public BlobService(IOptions<StorageOptions> options, ILogger<BlobService> logger)
    {
        _opts = options.Value;

        if (string.IsNullOrWhiteSpace(_opts.ConnectionString))
        {
            logger.LogInformation(
                "BlobService: connection string not configured. Media upload endpoints will return 503 until configured.");
            IsConfigured = false;
            return;
        }

        var serviceClient = new BlobServiceClient(_opts.ConnectionString);
        _container = serviceClient.GetBlobContainerClient(_opts.MediaContainer);
        _container.CreateIfNotExists(PublicAccessType.None);
        IsConfigured = true;
    }

    public bool IsConfigured { get; }
    public IReadOnlySet<string> AllowedContentTypes => Allowed;

    public async Task<string> UploadMediaAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!IsConfigured || _container is null)
        {
            throw new InvalidOperationException("BlobService is not configured.");
        }
        if (!Allowed.Contains(contentType))
        {
            throw new ArgumentException($"Content type '{contentType}' is not allowed.", nameof(contentType));
        }

        var ext = contentType.ToLowerInvariant() switch
        {
            "image/png"  => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("unreachable"),
        };
        var blobName = $"{Guid.NewGuid():N}{ext}";

        var blob = _container.GetBlobClient(blobName);
        await blob.UploadAsync(content,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: ct);

        return blobName;
    }

    public Uri GetMediaReadUrl(string blobName, TimeSpan? validFor = null)
    {
        if (!IsConfigured || _container is null)
        {
            throw new InvalidOperationException("BlobService is not configured.");
        }

        var blob = _container.GetBlobClient(blobName);
        if (!blob.CanGenerateSasUri)
        {
            // Connection-string clients always support shared-key SAS, so this
            // shouldn't be reachable in practice — kept as a defensive guard.
            throw new InvalidOperationException("Cannot generate shared-key SAS for this blob client.");
        }

        var lifetime = validFor ?? _opts.ReadSasLifetime;
        var sas = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName          = blobName,
            Resource          = "b",
            ExpiresOn         = DateTimeOffset.UtcNow.Add(lifetime),
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(sas);
    }
}
