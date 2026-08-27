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

/// <summary>Community events: CRUD, ownership, and the validation branches.</summary>
public class EventsFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static CommunityEvent Event(string id = "e1", string? createdBy = "owner") => new(
        id, "Coffee", "Coffee meet-up",
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
        "Brixton", "Come along", null, createdBy, "Owner") { Etag = "e1" };

    private static object ValidEvent() => new
    {
        title = "Coffee morning", type = "Coffee meet-up",
        startsAt = "2026-06-01T10:00:00Z", endsAt = "2026-06-01T12:00:00Z",
        location = "Brixton", description = "Come along",
    };

    // ── Events ──────────────────────────────────────────────────────────────────
    private static EventsFunctions EventsFn(FakeContentRepository repo) =>
        new(repo, NullLogger<EventsFunctions>.Instance);

    [Fact]
    public async Task Events_get_returns_404_then_200()
    {
        var repo = new FakeContentRepository();
        var fn = EventsFn(repo);
        var missing = (TestHttpResponseData)await fn.GetEvent(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events/x"), "x", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        repo.EventById = Event();
        var found = (TestHttpResponseData)await fn.GetEvent(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events/e1"), "e1", Ct);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    [Fact]
    public async Task Events_get_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Storage error") };
        var fn = EventsFn(repo);
        var resp = (TestHttpResponseData)await fn.GetEvent(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events/e1"), "e1", Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task Events_list_returns_200()
    {
        var repo = new FakeContentRepository { Events = { Event() } };
        var fn = EventsFn(repo);
        var resp = (TestHttpResponseData)await fn.GetEvents(TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events?upcoming=true"), Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Events_create_disabled_then_valid()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = EventsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid", "U", "Contributor");
        var disabled = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/events", ValidEvent()), ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/events", ValidEvent()), ctx, Ct);
        Assert.Contains((int)ok.StatusCode, new[] { 200, 201 });
    }

    [Fact]
    public async Task Events_create_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = EventsFn(repo);
        var ctx = new TestFunctionContext().WithUser("oid", "U", "Contributor");
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/events", ValidEvent()), ctx, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { EventById = Event(createdBy: "admin"), ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = EventsFn(repo);
        var admin = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(admin, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", admin, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Events_delete_forbidden_for_non_owner_and_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { EventById = Event(createdBy: "owner") };
        var fn = EventsFn(repo);

        var stranger = new TestFunctionContext().WithUser("stranger", "S", "Contributor");
        var forbidden = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(stranger, "DELETE", "http://localhost/api/events/e1", ""), "e1", stranger, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        repo.ThrowOnWrite = new RequestFailedException(412, "Precondition failed");
        var admin = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var err = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(admin, "DELETE", "http://localhost/api/events/e1", ""), "e1", admin, Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err.StatusCode);
    }

    [Fact]
    public async Task Events_replace_enforces_ownership()
    {
        var repo = new FakeContentRepository { EventById = Event(createdBy: "owner") };
        var fn = EventsFn(repo);
        // Non-admin, non-owner → forbidden
        var stranger = new TestFunctionContext().WithUser("stranger", "S", "Contributor");
        var forbidden = (TestHttpResponseData)await fn.Replace(TestHttp.Json(stranger, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", stranger, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Admin → allowed
        var admin = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(admin, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", admin, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Events_delete_owner_succeeds_and_missing_404()
    {
        var repo = new FakeContentRepository();
        var fn = EventsFn(repo);
        var owner = new TestFunctionContext().WithUser("owner", "O", "Contributor");
        var missing = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(owner, "DELETE", "http://localhost/api/events/e1", ""), "e1", owner, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        repo.EventById = Event(createdBy: "owner");
        var ok = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(owner, "DELETE", "http://localhost/api/events/e1", ""), "e1", owner, Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    // ── Events: validation error and missing-event branches ──────────────────

    [Fact]
    public async Task Events_create_400_on_invalid_input()
    {
        var fn = EventsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext().WithUser("oid", "U", "Contributor");
        // Empty object fails required-field validation
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(ctx, "POST", "http://localhost/api/events", new { }), ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_503_when_writes_disabled()
    {
        var fn = EventsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_400_on_invalid_input()
    {
        var fn = EventsFn(new FakeContentRepository());
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/events/e1", new { }), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Events_replace_404_when_event_not_found()
    {
        var fn = EventsFn(new FakeContentRepository()); // EventById = null
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(ctx, "PUT", "http://localhost/api/events/e1", ValidEvent()), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Events_delete_503_when_writes_disabled()
    {
        var fn = EventsFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext().WithUser("admin", "A", "Admin");
        var resp = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/events/e1", ""), "e1", ctx, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    // ── Events: GetEvents upcoming=false branch ───────────────────────────────

    [Fact]
    public async Task Events_list_without_upcoming_param_returns_200()
    {
        var fn = EventsFn(new FakeContentRepository { Events = { Event() } });
        var resp = (TestHttpResponseData)await fn.GetEvents(
            TestHttp.Get(new TestFunctionContext(), "http://localhost/api/events"), Ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
