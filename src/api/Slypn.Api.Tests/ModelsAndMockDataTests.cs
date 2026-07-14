using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

public class ModelsAndMockDataTests
{
    [Fact]
    public void Article_defaults_type_and_status()
    {
        var a = new Article("id", "slug", "Title", "Summary", "Body", "Author",
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), 5, "Community", new[] { "x" });
        Assert.Equal("article", a.Type);
        Assert.Equal("published", a.Status);
        Assert.Null(a.Etag);
        Assert.Null(a.AuthorId);
    }

    [Fact]
    public void CommunityEvent_YearMonth_uses_utc_year_month()
    {
        var e = new CommunityEvent("id", "Quiz", "Q&A",
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            "Online", "desc", null);
        Assert.Equal("2026-06", e.YearMonth);
    }

    [Fact]
    public void Newsletter_Year_is_four_digit_issue_year()
    {
        var n = new Newsletter("id", "May 2026", new DateOnly(2026, 5, 1), "summary text", new[] { "t" });
        Assert.Equal("2026", n.Year);
    }

    [Fact]
    public void Draft_and_Member_records_round_trip_etag()
    {
        var d = new Draft("id", "auth", "Author", "article", "T", "s", "sum", "body", "cat",
            new[] { "x" }, 1, DateTime.UtcNow, DateTime.UtcNow) { Etag = "e1" };
        Assert.Equal("e1", d.Etag);

        var m = new Member("id", "a@b.com", "Alice", new[] { "Member" }, "active", DateTime.UtcNow);
        Assert.Equal("active", m.Status);
        Assert.Null(m.Oid);
    }

    [Fact]
    public void MockDataService_exposes_all_seed_collections()
    {
        var mock = new MockDataService();
        Assert.Equal(5, mock.Articles.Count);
        Assert.Equal(3, mock.BlogPosts.Count);
        Assert.NotEmpty(mock.Events);
        Assert.Equal(9, mock.Resources.Count);
        Assert.Equal(4, mock.Newsletters.Count);
    }

    [Fact]
    public void BlogPost_record_round_trips_all_fields()
    {
        var published = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var p = new BlogPost("id1", "my-slug", "Title", "Excerpt", "Body", "Author", published);
        Assert.Equal("id1", p.Id);
        Assert.Equal("my-slug", p.Slug);
        Assert.Equal("Title", p.Title);
        Assert.Equal("Excerpt", p.Excerpt);
        Assert.Equal("Body", p.Body);
        Assert.Equal("Author", p.Author);
        Assert.Equal(published, p.PublishedAt);
    }

    [Fact]
    public void GraphOptions_stores_tenant_and_client_ids()
    {
        var opts = new GraphOptions { TenantId = "t1", ClientId = "c1" };
        Assert.Equal("t1", opts.TenantId);
        Assert.Equal("c1", opts.ClientId);
        Assert.True(opts.IsConfigured);
    }

    [Fact]
    public void OtelOptions_stores_headers_and_reports_configured()
    {
        var opts = new OtelOptions { Endpoint = "https://otlp.example.com", Headers = "Authorization=Basic abc" };
        Assert.Equal("Authorization=Basic abc", opts.Headers);
        Assert.True(opts.IsConfigured);
    }
}
