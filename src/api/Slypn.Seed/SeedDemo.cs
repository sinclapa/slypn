using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Slypn.Seed;

// Seed records mirror the API models' JSON shape (System.Text.Json web/camelCase)
// so ContentRepository can deserialise the Json column straight back into them.
public sealed record SeedEvent(
    string Id, string Title, string Type, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string Location, string Description, string? SignupUrl,
    string? CreatedBy = null, string? CreatedByName = null);

public sealed record SeedArticle(
    string Id, string Slug, string Title, string Summary, string Body, string Author,
    DateTime PublishedAt, int ReadingMinutes, string Category, IReadOnlyList<string> Tags,
    string Status, string Type, string? AuthorId);

public sealed record SeedResource(
    string Id, string Title, string Description, string Url, string Category);

/// <summary>
/// Seeds demo content into the local emulator: a rolling 4-year run of monthly
/// coffee meet-ups, 10 articles, 10 blog posts and 5 resources. Idempotent —
/// stable ids mean re-running replaces rather than duplicates.
/// </summary>
public static class SeedDemo
{
    private const string ContentContainer  = "content";
    private const string ArticleBodyPrefix = "articles";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task SeedAsync(string connectionString, TextWriter log)
    {
        await SeedEventsAsync(connectionString, log);
        await SeedExtraEventsAsync(connectionString, log);
        await SeedArticlesAsync(connectionString, "article", DemoContent.Articles, log);
        await SeedArticlesAsync(connectionString, "blog",    DemoContent.Blogs,    log);
        await SeedResourcesAsync(connectionString, log);
    }

    // ---- Events: last Saturday of each month, 2 years back to 2 years ahead ----
    private static async Task SeedEventsAsync(string connectionString, TextWriter log)
    {
        var events = new TableClient(connectionString, "events");
        await events.CreateIfNotExistsAsync();

        var tz    = ResolveLondon();
        var today = DateTime.UtcNow.Date;
        var first = new DateOnly(today.Year - 2, today.Month, 1);
        var last  = new DateOnly(today.Year + 2, today.Month, 1);
        var month = first;
        var count = 0;

        for (; month <= last; month = month.AddMonths(1))
        {
            var day    = LastSaturday(month.Year, month.Month);
            var offset = tz.GetUtcOffset(new DateTime(day.Year, day.Month, day.Day, 10, 0, 0, DateTimeKind.Unspecified));
            var starts = new DateTimeOffset(day.Year, day.Month, day.Day, 10, 0, 0, offset);
            var ends   = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, offset);

            var ev = new SeedEvent(
                Id:          $"evt-coffee-{day:yyyy-MM-dd}",
                Title:       $"Coffee Meet-up — {day:MMMM yyyy}",
                Type:        "Coffee Meet-up",
                StartsAt:    starts,
                EndsAt:      ends,
                Location:    "Royal Festival Hall, South Bank",
                Description: "Our regular monthly coffee meet-up by the river. Drop in any time between "
                             + "10:00 and 12:00 — partners, carers and the newly diagnosed all welcome.",
                SignupUrl:   null);

            var yearMonth = starts.UtcDateTime.ToString("yyyy-MM");
            await events.UpsertEntityAsync(
                new TableEntity(yearMonth, ev.Id) { ["Json"] = JsonSerializer.Serialize(ev, Json) },
                TableUpdateMode.Replace);
            count++;
        }
        log.WriteLine($"Seeded {count} coffee meet-up events (last Saturday, {first.Year}–{last.Year}).");
    }

    // ---- Extra one-off events (varied types, past + future) --------------------
    private static async Task SeedExtraEventsAsync(string connectionString, TextWriter log)
    {
        var events = new TableClient(connectionString, "events");
        await events.CreateIfNotExistsAsync();

        var tz        = ResolveLondon();
        var thisMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var count     = 0;

        for (var i = 0; i < DemoContent.ExtraEvents.Count; i++)
        {
            var x     = DemoContent.ExtraEvents[i];
            var month = thisMonth.AddMonths(x.MonthOffset);
            var day   = Math.Min(x.Day, DateTime.DaysInMonth(month.Year, month.Month));

            var startLocal = new DateTime(month.Year, month.Month, day, x.StartHour, x.StartMinute, 0, DateTimeKind.Unspecified);
            var offset     = tz.GetUtcOffset(startLocal);
            var starts     = new DateTimeOffset(startLocal, offset);
            var ends       = new DateTimeOffset(month.Year, month.Month, day, x.EndHour, x.EndMinute, 0, offset);

            var ev = new SeedEvent(
                Id:          $"evt-extra-{i + 1:D2}",
                Title:       x.Title,
                Type:        x.Type,
                StartsAt:    starts,
                EndsAt:      ends,
                Location:    x.Location,
                Description: x.Description,
                SignupUrl:   x.SignupUrl);

            await events.UpsertEntityAsync(
                new TableEntity(starts.UtcDateTime.ToString("yyyy-MM"), ev.Id) { ["Json"] = JsonSerializer.Serialize(ev, Json) },
                TableUpdateMode.Replace);
            count++;
        }
        log.WriteLine($"Seeded {count} extra events (varied types, past + future).");
    }

    // ---- Articles + blog posts (shared "articles" table, body in a blob) -------
    private static async Task SeedArticlesAsync(
        string connectionString, string type, IReadOnlyList<DemoArticle> source, TextWriter log)
    {
        var table = new TableClient(connectionString, "articles");
        await table.CreateIfNotExistsAsync();
        var blobs = new BlobContainerClient(connectionString, ContentContainer);
        await blobs.CreateIfNotExistsAsync(PublicAccessType.None);

        var prefix = type == "blog" ? "seedblog" : "seedart";
        for (var i = 0; i < source.Count; i++)
        {
            var c    = source[i];
            var id   = $"{prefix}{i + 1:D2}";
            var body = ToHtml(c.Paragraphs);
            var article = new SeedArticle(
                Id:             id,
                Slug:           BuildSlug(c.Title, id),
                Title:          c.Title,
                Summary:        c.Summary,
                Body:           body,
                Author:         c.Author,
                PublishedAt:    DateTime.UtcNow.AddDays(-(i * 18 + 3)),
                ReadingMinutes: ReadingMinutes(body),
                Category:       c.Category,
                Tags:           c.Tags,
                Status:         "published",
                Type:           type,
                AuthorId:       null);

            // Table row mirrors ArticleEntity: body blanked (it lives in the blob), slug column.
            await table.UpsertEntityAsync(
                new TableEntity("published", id)
                {
                    ["Json"] = JsonSerializer.Serialize(article with { Body = string.Empty }, Json),
                    ["Slug"] = article.Slug,
                },
                TableUpdateMode.Replace);

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            await blobs.GetBlobClient($"{ArticleBodyPrefix}/{id}").UploadAsync(
                stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "text/html; charset=utf-8" } });
        }
        log.WriteLine($"Seeded {source.Count} {type} item(s).");
    }

    // ---- Resources -------------------------------------------------------------
    private static async Task SeedResourcesAsync(string connectionString, TextWriter log)
    {
        var table = new TableClient(connectionString, "resources");
        await table.CreateIfNotExistsAsync();

        for (var i = 0; i < DemoContent.Resources.Count; i++)
        {
            var r  = DemoContent.Resources[i];
            var id = $"seedres{i + 1:D2}";
            var resource = new SeedResource(id, r.Title, r.Description, r.Url, r.Category);
            await table.UpsertEntityAsync(
                new TableEntity(r.Category, id) { ["Json"] = JsonSerializer.Serialize(resource, Json) },
                TableUpdateMode.Replace);
        }
        log.WriteLine($"Seeded {DemoContent.Resources.Count} resources.");
    }

    // ---- helpers ---------------------------------------------------------------
    private static DateOnly LastSaturday(int year, int month)
    {
        var d = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(-1);
        return d;
    }

    private static TimeZoneInfo ResolveLondon()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static string ToHtml(IReadOnlyList<string> paragraphs) =>
        string.Concat(paragraphs.Select(p => $"<p>{System.Net.WebUtility.HtmlEncode(p)}</p>"));

    private static int ReadingMinutes(string html)
    {
        var text  = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Round(words / 200.0));
    }

    private static string BuildSlug(string title, string id)
    {
        var sb = new StringBuilder(title.Length);
        var lastDash = false;
        foreach (var ch in title.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) { sb.Append(ch); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        var baseSlug = sb.ToString().Trim('-');
        if (baseSlug.Length == 0) baseSlug = "post";
        var shortId = id.Length >= 8 ? id[..8] : id;
        return $"{baseSlug}-{shortId}";
    }
}
