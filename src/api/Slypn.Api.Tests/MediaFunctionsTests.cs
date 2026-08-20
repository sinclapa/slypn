using System.Net;
using Slypn.Api.Functions;
using Xunit;

namespace Slypn.Api.Tests;

public class MediaFunctionsTests
{
    private static readonly FakeBlobService Blob = new();

    [Fact]
    public async Task Returns_service_unavailable_when_blob_not_configured()
    {
        var blob = new FakeBlobService { Configured = false };
        var fn = new MediaFunctions(blob);
        var req = TestHttp.Get(new TestFunctionContext(), "http://localhost/api/media");
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Returns_unsupported_media_type_when_content_type_missing()
    {
        var fn = new MediaFunctions(Blob);
        var req = TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/media", "data");
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [Fact]
    public async Task Returns_bad_request_on_malformed_multipart_body()
    {
        var fn = new MediaFunctions(Blob);
        var req = TestHttp.Raw(
            new TestFunctionContext(), "POST", "http://localhost/api/media",
            "this body has no multipart boundary markers at all",
            new Dictionary<string, string> { ["Content-Type"] = "multipart/form-data; boundary=xbnd" });
        var resp = (TestHttpResponseData)await fn.Upload(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Could not parse", resp.ReadBodyAsString());
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
