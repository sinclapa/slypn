using Slypn.Api.Services;

namespace Slypn.Api.Tests;

internal sealed class FakeInviteService : IInviteService
{
    public bool IsConfigured => true;
    public InviteResult Result = new(true, "https://redeem/abc", null);
    public Task<InviteResult> SendInviteAsync(string email, string displayName, CancellationToken ct)
        => Task.FromResult(Result);
}

internal sealed class FakeEntraUserService : IEntraUserService
{
    public bool IsConfigured => true;
    public List<string> Deleted = new();
    public Task DeleteUserAsync(string oid, CancellationToken ct) { Deleted.Add(oid); return Task.CompletedTask; }
}

internal sealed class FakeBlobService : IBlobService
{
    public bool Configured = true;
    public bool IsConfigured => Configured;
    public IReadOnlySet<string> AllowedContentTypes { get; } =
        new HashSet<string> { "image/png", "image/jpeg", "image/webp" };
    public Task<string> UploadMediaAsync(Stream content, string contentType, CancellationToken ct)
        => Task.FromResult("media/uploaded.png");
    public Uri GetMediaReadUrl(string blobName, TimeSpan? validFor = null)
        => new($"https://blob.example/{blobName}?sas=token");
}
