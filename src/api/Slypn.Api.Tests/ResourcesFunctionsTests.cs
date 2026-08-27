using Azure;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Functions;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

/// <summary>Resource links: CRUD, plus the writes-disabled and validation branches.</summary>
public class ResourcesFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // ── Resources ────────────────────────────────────────────────────────────────
    private static ResourcesFunctions ResourcesFn(FakeContentRepository repo) =>
        new(repo, NullLogger<ResourcesFunctions>.Instance);

    [Fact]
    public async Task Resources_list_and_create_and_delete()
    {
        var repo = new FakeContentRepository { Resources = { new Resource("r1", "T", "D", "https://x.org", "NHS") } };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();

        var list = (TestHttpResponseData)await fn.GetResources(TestHttp.Get(ctx, "http://localhost/api/resources"), Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var valid = new { title = "Helpline", description = "Support line", url = "https://x.org/a", category = "NHS" };
        var created = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/resources", valid), Ct);
        Assert.Contains((int)created.StatusCode, new[] { 200, 201 });

        var noCat = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/resources/r1", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noCat.StatusCode);

        var deleted = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/resources/r1?category=NHS", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Resources_replace_503_then_200()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "Helpline", description = "Support line for members", url = "https://example.org/support", category = "NHS" };

        var disabled = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/resources/r1", valid), "r1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/resources/r1", valid), "r1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Resources_create_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "Helpline", description = "Support line for members", url = "https://example.org/support", category = "NHS" };
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/resources", valid), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_replace_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "Helpline", description = "Support line for members", url = "https://example.org/support", category = "NHS" };
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/resources/r1", valid), "r1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_delete_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = ResourcesFn(repo);
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/resources/r1?category=NHS", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    // ── Resources: writes-disabled and validation-error branches ─────────────

    [Fact]
    public async Task Resources_create_503_when_writes_disabled()
    {
        var fn = ResourcesFn(new FakeContentRepository { Writes = false });
        var valid = new { title = "T", description = "Support line for members", url = "https://x.org/a", category = "NHS" };
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/resources", valid), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_create_400_on_invalid_input()
    {
        var fn = ResourcesFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/resources", new { }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_replace_400_on_invalid_input()
    {
        var fn = ResourcesFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(new TestFunctionContext(), "PUT", "http://localhost/api/resources/r1", new { }), "r1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Resources_delete_503_when_writes_disabled()
    {
        var fn = ResourcesFn(new FakeContentRepository { Writes = false });
        var resp = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(new TestFunctionContext(), "DELETE", "http://localhost/api/resources/r1?category=NHS", ""), "r1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }
}
