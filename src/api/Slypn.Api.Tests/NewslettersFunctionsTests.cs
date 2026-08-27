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

/// <summary>Newsletter issues, including file upload and the topic limits.</summary>
public class NewslettersFunctionsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // Borrowed for the cross-surface smoke test below, which ends with a subscribe.
    private static SubscribersFunctions SubscribersFn(FakeContentRepository repo) =>
        new(repo, NullLogger<SubscribersFunctions>.Instance);

    [Fact]
    public async Task Newsletters_replace_503_then_200()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };

        var disabled = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Newsletters_delete_503_then_204()
    {
        var repo = new FakeContentRepository { Writes = false };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();

        var disabled = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/newsletters/n1", ""), "n1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);

        repo.Writes = true;
        var ok = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/newsletters/n1", ""), "n1", Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    // ── Newsletters ──────────────────────────────────────────────────────────────
    private static NewslettersFunctions NewslettersFn(FakeContentRepository repo) =>
        new(repo, NullLogger<NewslettersFunctions>.Instance, Options.Create(new StorageOptions()));

    [Fact]
    public async Task Newsletters_create_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var resp = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_replace_preserves_fileName_when_input_has_none()
    {
        // Editing title/date/summary/topics goes through the JSON-only Replace
        // endpoint, which never carries a file. It must not clear an already
        // attached file's FileName off the row.
        var repo = new FakeContentRepository
        {
            Newsletters = { new Newsletter("n1", "May", new DateOnly(2026, 5, 1), "summary text", new[] { "t" }) { FileName = "issue.pdf" } },
        };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };

        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("issue.pdf", resp.ReadBodyAsString());
    }

    [Fact]
    public async Task Newsletters_replace_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var resp = (TestHttpResponseData)await fn.Replace(TestHttp.Json(ctx, "PUT", "http://localhost/api/newsletters/n1", valid), "n1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_delete_412_when_storage_fails()
    {
        var repo = new FakeContentRepository { ThrowOnWrite = new RequestFailedException(412, "Precondition failed") };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.Delete(TestHttp.Raw(ctx, "DELETE", "http://localhost/api/newsletters/n1", ""), "n1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_file_streams_bytes_with_headers()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var repo = new FakeContentRepository
        {
            NewsletterFiles = { ["newsletter-2020-11"] = new BlobDownload(new MemoryStream(bytes), "application/pdf") },
        };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();

        var resp = (TestHttpResponseData)await fn.GetFile(
            TestHttp.Get(ctx, "http://localhost/api/newsletters/newsletter-2020-11/file"), "newsletter-2020-11", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Headers.GetValues("Content-Type").Single());
        Assert.Contains("SLYPN-Newsletter-2020-11.pdf", resp.Headers.GetValues("Content-Disposition").Single());
        resp.Body.Position = 0;
        Assert.Equal(bytes, ((MemoryStream)resp.Body).ToArray());
    }

    [Fact]
    public async Task Newsletters_file_404_when_absent()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var ctx = new TestFunctionContext();
        var resp = (TestHttpResponseData)await fn.GetFile(
            TestHttp.Get(ctx, "http://localhost/api/newsletters/newsletter-1999-01/file"), "newsletter-1999-01", Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_file_download_name_for_docx()
    {
        // Non-"newsletter-"-prefixed id: the shape CreateNewsletterAsync actually mints
        // for admin-created newsletters (a GUID), so the stamp falls back to the raw id.
        var id = "a1b2c3";
        var repo = new FakeContentRepository
        {
            NewsletterFiles = { [id] = new BlobDownload(new MemoryStream([]),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document") },
        };
        var resp = (TestHttpResponseData)await NewslettersFn(repo).GetFile(
            TestHttp.Get(new TestFunctionContext(), $"http://localhost/api/newsletters/{id}/file"), id, Ct);

        Assert.Contains($"SLYPN-Newsletter-{id}.docx", resp.Headers.GetValues("Content-Disposition").Single());
    }

    [Fact]
    public async Task Newsletters_file_download_name_for_doc()
    {
        var id = "newsletter-2021-03";
        var repo = new FakeContentRepository
        {
            NewsletterFiles = { [id] = new BlobDownload(new MemoryStream([]), "application/msword") },
        };
        var resp = (TestHttpResponseData)await NewslettersFn(repo).GetFile(
            TestHttp.Get(new TestFunctionContext(), $"http://localhost/api/newsletters/{id}/file"), id, Ct);

        Assert.Contains("SLYPN-Newsletter-2021-03.doc", resp.Headers.GetValues("Content-Disposition").Single());
    }

    [Fact]
    public async Task Newsletters_file_download_name_for_unknown_content_type()
    {
        var id = "newsletter-2021-04";
        var repo = new FakeContentRepository
        {
            NewsletterFiles = { [id] = new BlobDownload(new MemoryStream([]), "application/octet-stream") },
        };
        var resp = (TestHttpResponseData)await NewslettersFn(repo).GetFile(
            TestHttp.Get(new TestFunctionContext(), $"http://localhost/api/newsletters/{id}/file"), id, Ct);

        Assert.Contains("SLYPN-Newsletter-2021-04.bin", resp.Headers.GetValues("Content-Disposition").Single());
    }

    [Fact]
    public async Task Newsletters_uploadFile_200_sets_fileName()
    {
        var repo = new FakeContentRepository
        {
            Newsletters = { new Newsletter("n1", "May", new DateOnly(2026, 5, 1), "summary text", new[] { "t" }) },
        };
        var fn = NewslettersFn(repo);

        var resp = (TestHttpResponseData)await fn.UploadFile(
            NewsletterFileMultipart("n1", "issue.pdf", "application/pdf"), "n1", Ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("issue.pdf", resp.ReadBodyAsString());
        Assert.Equal("issue.pdf", repo.Newsletters.Single(n => n.Id == "n1").FileName);
        Assert.True(repo.NewsletterFiles.ContainsKey("n1"));
    }

    [Fact]
    public async Task Newsletters_uploadFile_415_when_not_multipart()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var req = TestHttp.Raw(new TestFunctionContext(), "PUT", "http://localhost/api/newsletters/n1/file", "x",
            new Dictionary<string, string> { ["Content-Type"] = "application/json" });
        var resp = (TestHttpResponseData)await fn.UploadFile(req, "n1", Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_uploadFile_400_when_no_file_part()
    {
        const string boundary = "testboundary";
        var body = $"--{boundary}\r\nContent-Disposition: form-data; name=\"field\"\r\n\r\nvalue\r\n--{boundary}--\r\n";
        var req = TestHttp.Raw(new TestFunctionContext(), "PUT", "http://localhost/api/newsletters/n1/file", body,
            new Dictionary<string, string>
            {
                ["Content-Type"] = $"multipart/form-data; boundary={boundary}",
                // Real clients declare a length, and the upload endpoints now
                // require one so an oversized body is refused before buffering.
                ["Content-Length"] = System.Text.Encoding.UTF8.GetByteCount(body).ToString(),
            });
        var resp = (TestHttpResponseData)await NewslettersFn(new FakeContentRepository()).UploadFile(req, "n1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_uploadFile_415_when_content_type_not_allowed()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.UploadFile(
            NewsletterFileMultipart("n1", "photo.png", "image/png"), "n1", Ct);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_uploadFile_503_when_writes_disabled()
    {
        var fn = NewslettersFn(new FakeContentRepository { Writes = false });
        var resp = (TestHttpResponseData)await fn.UploadFile(
            NewsletterFileMultipart("n1", "issue.pdf", "application/pdf"), "n1", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_uploadFile_404_when_unknown_id()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.UploadFile(
            NewsletterFileMultipart("missing", "issue.pdf", "application/pdf"), "missing", Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_uploadFile_412_when_storage_fails()
    {
        var repo = new FakeContentRepository
        {
            Newsletters = { new Newsletter("n1", "May", new DateOnly(2026, 5, 1), "summary text", new[] { "t" }) },
            ThrowOnWrite = new RequestFailedException(412, "Precondition failed"),
        };
        var fn = NewslettersFn(repo);
        var resp = (TestHttpResponseData)await fn.UploadFile(
            NewsletterFileMultipart("n1", "issue.pdf", "application/pdf"), "n1", Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, resp.StatusCode);
    }

    private static TestHttpRequestData NewsletterFileMultipart(string id, string fileName, string mimeType, string content = "bytes")
    {
        const string boundary = "testboundary";
        var body = $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {mimeType}\r\n\r\n{content}\r\n--{boundary}--\r\n";
        return TestHttp.Raw(
            new TestFunctionContext(), "PUT", $"http://localhost/api/newsletters/{id}/file", body,
            new Dictionary<string, string>
            {
                ["Content-Type"] = $"multipart/form-data; boundary={boundary}",
                // Real clients declare a length, and the upload endpoints now
                // require one so an oversized body is refused before buffering.
                ["Content-Length"] = System.Text.Encoding.UTF8.GetByteCount(body).ToString(),
            });
    }

    // ── Newsletters: writes-disabled and validation-error branches ────────────

    [Fact]
    public async Task Newsletters_create_503_when_writes_disabled()
    {
        var fn = NewslettersFn(new FakeContentRepository { Writes = false });
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_create_400_on_invalid_input()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", new { }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Newsletters_replace_400_on_invalid_input()
    {
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Replace(
            TestHttp.Json(new TestFunctionContext(), "PUT", "http://localhost/api/newsletters/n1", new { }), "n1", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── FunctionHelpers: null body and storage exception mapping ─────────────

    [Fact]
    public async Task ReadValidatedAsync_returns_400_on_null_body()
    {
        // JSON "null" deserialises to null → triggers "Empty request body" branch
        var fn = NewslettersFn(new FakeContentRepository());
        var resp = (TestHttpResponseData)await fn.Create(
            TestHttp.Raw(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", "null",
                new Dictionary<string, string> { ["Content-Type"] = "application/json" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task MapStorageException_maps_404_and_409_status_codes()
    {
        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };

        var fn404 = NewslettersFn(new FakeContentRepository { ThrowOnWrite = new Azure.RequestFailedException(404, "Not found") });
        var resp404 = (TestHttpResponseData)await fn404.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.NotFound, resp404.StatusCode);

        var fn409 = NewslettersFn(new FakeContentRepository { ThrowOnWrite = new Azure.RequestFailedException(409, "Conflict") });
        var resp409 = (TestHttpResponseData)await fn409.Create(
            TestHttp.Json(new TestFunctionContext(), "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Equal(HttpStatusCode.Conflict, resp409.StatusCode);
    }

    [Fact]
    public async Task Newsletters_list_create_subscribe()
    {
        var repo = new FakeContentRepository { Newsletters = { new Newsletter("n1", "May", new DateOnly(2026, 5, 1), "summary text", new[] { "t" }) } };
        var fn = NewslettersFn(repo);
        var ctx = new TestFunctionContext();

        var list = (TestHttpResponseData)await fn.GetNewsletters(TestHttp.Get(ctx, "http://localhost/api/newsletters"), Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var valid = new { title = "June 2026", issueDate = "2026-06-01", summary = "A long enough summary.", topics = new[] { "x" } };
        var created = (TestHttpResponseData)await fn.Create(TestHttp.Json(ctx, "POST", "http://localhost/api/newsletters", valid), Ct);
        Assert.Contains((int)created.StatusCode, new[] { 200, 201 });

        var sub = (TestHttpResponseData)await SubscribersFn(repo).Subscribe(TestHttp.Json(ctx, "POST", "http://localhost/api/subscribers", new { email = "me@example.com" }), Ct);
        Assert.Contains((int)sub.StatusCode, new[] { 200, 201 });
    }
}
