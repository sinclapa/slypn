using System.Net;
using Microsoft.Extensions.Options;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
using Xunit;

namespace Slypn.Api.Tests;

public class MediaFunctionsTests
{
    private static readonly FakeBlobService Blob = new();

    private const long MaxBytes = 5 * 1024 * 1024;

    private static IOptions<StorageOptions> Limits(long max = MaxBytes) =>
        Options.Create(new StorageOptions { MaxMediaUploadBytes = max });

    /// <summary>Multipart headers with an explicit declared length.</summary>
    private static Dictionary<string, string> Multipart(long contentLength) => new()
    {
        ["Content-Type"]   = "multipart/form-data; boundary=xbnd",
        ["Content-Length"] = contentLength.ToString(),
    };

    [Fact]
    public async Task Returns_service_unavailable_when_blob_not_configured()
    {
        var blob = new FakeBlobService { Configured = false };
        var fn = new MediaFunctions(blob, Limits());
        var req = TestHttp.Get(new TestFunctionContext(), "http://localhost/api/media");
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Returns_unsupported_media_type_when_content_type_missing()
    {
        var fn = new MediaFunctions(Blob, Limits());
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/media", "data");
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [Fact]
    public async Task Returns_bad_request_on_malformed_multipart_body()
    {
        var fn = new MediaFunctions(Blob, Limits());
        var req = TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media",
            "this body has no multipart boundary markers at all",
            Multipart(64));
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Could not parse", resp.ReadBodyAsString());
    }

    // An oversized body used to be buffered in full before the content-type
    // allowlist could reject it. The declared length is now checked first, so
    // the parser is never reached.
    [Fact]
    public async Task Rejects_upload_larger_than_the_limit_before_parsing()
    {
        var fn = new MediaFunctions(Blob, Limits());
        var req = TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media",
            // Deliberately tiny: if the body were what mattered this would pass,
            // so reaching 413 proves the decision came from Content-Length alone.
            "x",
            Multipart(MaxBytes + 1));

        var resp = (TestHttpResponseData)await fn.Upload(req);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
        // The parser would have reported a malformed body had it run.
        Assert.DoesNotContain("Could not parse", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Accepts_upload_at_the_limit()
    {
        var fn = new MediaFunctions(Blob, Limits());
        var req = TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media",
            "still not valid multipart",
            Multipart(MaxBytes));

        var resp = (TestHttpResponseData)await fn.Upload(req);

        // Exactly at the limit is allowed through: it fails later, in the
        // parser, which is the pre-existing behaviour for a malformed body.
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Could not parse", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Rejects_upload_with_no_declared_length()
    {
        var fn = new MediaFunctions(Blob, Limits());
        var req = TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media", "x",
            new Dictionary<string, string> { ["Content-Type"] = "multipart/form-data; boundary=xbnd" });

        var resp = (TestHttpResponseData)await fn.Upload(req);

        // Refused, not waved through — accepting it would leave open the very
        // unbounded path the limit exists to close.
        Assert.Equal(HttpStatusCode.LengthRequired, resp.StatusCode);
    }

    [Fact]
    public async Task Limit_comes_from_options_not_a_constant()
    {
        var fn = new MediaFunctions(Blob, Limits(max: 10));
        var req = TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media", "x", Multipart(11));

        var resp = (TestHttpResponseData)await fn.Upload(req);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
    }

    [Fact]
    public void Upload_requires_a_role()
    {
        // The endpoint had no [RequireRole] at all, so anyone could write to
        // blob storage. The attribute IS the fix — JwtMiddleware reads it and
        // lets an unattributed function through unauthenticated — so assert it
        // directly rather than trusting it stays put.
        var attr = typeof(MediaFunctions)
            .GetMethod(nameof(MediaFunctions.Upload))!
            .GetCustomAttributes(typeof(Slypn.Api.Infrastructure.RequireRoleAttribute), inherit: false)
            .Cast<Slypn.Api.Infrastructure.RequireRoleAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(new[] { "Admin", "Contributor" }, attr!.Roles);
    }
}
