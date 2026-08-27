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

/// <summary>Newsletter subscribers: the public create, and the admin list and remove.</summary>
public class SubscribersFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Subscribers_create_412_when_lookup_fails_and_412_when_upsert_fails()
    {
        var ctx = new TestFunctionContext();
        var payload = TestHttp.Json(ctx, "POST", "http://localhost/api/subscribers", new { email = "me@example.com" });

        // GetSubscriberByEmailAsync throws
        var repo1 = new FakeContentRepository { ThrowOnSubscriberLookup = new RequestFailedException(500, "Lookup failed") };
        var err1 = (TestHttpResponseData)await SubscribersFn(repo1).Subscribe(payload, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, err1.StatusCode);

        // UpsertSubscriberAsync throws
        var repo2 = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var err2 = (TestHttpResponseData)await SubscribersFn(repo2).Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/subscribers", new { email = "me@example.com" }), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, err2.StatusCode);
    }

    [Fact]
    public async Task Subscribers_create_503_when_writes_disabled()
    {
        var fn = SubscribersFn(new FakeContentRepository { Writes = false });
        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/subscribers",
                new { email = "me@example.com" }), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Subscribers_create_400_on_invalid_input()
    {
        var fn = SubscribersFn(new FakeContentRepository());
        // Empty object → email field fails [Required, EmailAddress] validation
        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/subscribers",
                new { }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // SEC-5: subscribing must never touch the members table. That conflation is what let an
    // anonymous subscribe buy its way past the CIAM sign-up gate (SEC-1).
    [Fact]
    public async Task Subscribers_create_writes_a_subscriber_and_never_a_member()
    {
        var repo = new FakeContentRepository();
        var fn = SubscribersFn(repo);

        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/subscribers",
                new { email = "  New@Example.com  " }), Ct);

        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Equal(0, repo.MemberUpserts);

        var saved = Assert.Single(repo.SubscriberUpserts);
        Assert.Equal("new@example.com", saved.Email);              // trimmed + lower-cased
        Assert.Equal("new@example.com", saved.DisplayName);        // falls back to the address
        Assert.Equal(Subscriber.KeyFor("new@example.com"), saved.Id);
        Assert.Contains("new@example.com", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Subscribers_create_is_idempotent_and_keeps_the_original_date()
    {
        // The row key is derived from the address, so a repeat subscribe upserts the same row.
        var firstSeen = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var existing = new Subscriber(Subscriber.KeyFor("sub@example.com"), "sub@example.com", "Old Display", firstSeen);
        var repo = new FakeContentRepository { SubscriberByEmail = existing };
        var fn = SubscribersFn(repo);

        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/subscribers",
                new { email = "sub@example.com" }), Ct);

        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        var saved = Assert.Single(repo.SubscriberUpserts);
        Assert.Equal(existing.Id, saved.Id);
        Assert.Equal(firstSeen, saved.SubscribedAt);    // not reset by the resubmit
        Assert.Equal("Old Display", saved.DisplayName); // no new name supplied -> keep theirs
    }

    [Fact]
    public async Task Subscribers_create_applies_a_supplied_display_name()
    {
        var existing = new Subscriber(Subscriber.KeyFor("sub@example.com"), "sub@example.com", "Old Display", DateTime.UtcNow);
        var repo = new FakeContentRepository { SubscriberByEmail = existing };
        var fn = SubscribersFn(repo);

        var resp = (TestHttpResponseData)await fn.Subscribe(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/subscribers",
                new { email = "sub@example.com", displayName = "  New Display  " }), Ct);

        Assert.Contains((int)resp.StatusCode, new[] { 200, 201 });
        Assert.Equal("New Display", Assert.Single(repo.SubscriberUpserts).DisplayName);
    }

    // ───── Subscribers: admin list + remove ─────

    private static SubscribersFunctions SubscribersFn(FakeContentRepository repo) =>
        new(repo, NullLogger<SubscribersFunctions>.Instance);

    [Fact]
    public async Task Subscribers_list_returns_rows_and_delete_removes_one()
    {
        var repo = new FakeContentRepository
        {
            Subscribers = { new Subscriber("s1", "sub@example.com", "Subby", DateTime.UtcNow) },
        };
        var fn = SubscribersFn(repo);
        var ctx = new TestFunctionContext();

        var list = (TestHttpResponseData)await fn.List(TestHttp.Get(ctx, "http://localhost/api/subscribers"), Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("sub@example.com", list.ReadBodyAsString());

        var del = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/subscribers/s1", ""), "s1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Subscribers_503_when_writes_disabled()
    {
        var fn = SubscribersFn(new FakeContentRepository { Writes = false });
        var ctx = new TestFunctionContext();

        var list = (TestHttpResponseData)await fn.List(TestHttp.Get(ctx, "http://localhost/api/subscribers"), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);

        var del = (TestHttpResponseData)await fn.Delete(
            TestHttp.Raw(ctx, "DELETE", "http://localhost/api/subscribers/s1", ""), "s1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, del.StatusCode);
    }

    [Fact]
    public async Task Subscribers_map_storage_failures()
    {
        var listRepo = new FakeContentRepository { ThrowOnRead = new RequestFailedException(500, "Table down") };
        var list = (TestHttpResponseData)await SubscribersFn(listRepo).List(
            TestHttp.Get(new TestFunctionContext(), "http://localhost/api/subscribers"), Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, list.StatusCode);

        var delRepo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var del = (TestHttpResponseData)await SubscribersFn(delRepo).Delete(
            TestHttp.Raw(new TestFunctionContext(), "DELETE", "http://localhost/api/subscribers/s1", ""), "s1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, del.StatusCode);
    }
}
