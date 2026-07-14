using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Slypn.Seed;

// Args:
//   [<docx-path>]  --connection-string <cs>  [--table <name>]  [--demo]
//
// At least one of <docx-path> (seed the newsletter) or --demo (seed demo
// events/articles/blogs/resources) is required.
// Defaults: table = "newsletters".
// Exit codes: 0 success, 1 bad args, 2 docx error, 3 storage error.

var argList = args.ToList();
var demo = argList.Remove("--demo");

// Optional positional docx path = first arg that isn't a --flag.
string? docxPath = argList.Count > 0 && !argList[0].StartsWith("--") ? argList[0] : null;
var named = ParseNamed((docxPath is null ? argList : argList.Skip(1)).ToArray());

if (!named.TryGetValue("connection-string", out var connectionString))
{
    await Console.Error.WriteLineAsync("Missing --connection-string.");
    return 1;
}
var table = named.GetValueOrDefault("table", "newsletters");

if (docxPath is null && !demo)
{
    await Console.Error.WriteLineAsync("usage: dotnet run -- [<docx-path>] --connection-string <cs> [--table <name>] [--demo]");
    await Console.Error.WriteLineAsync("Pass a docx path to seed the newsletter and/or --demo to seed demo content.");
    return 1;
}

// ----- newsletter (from docx) -------------------------------------------------
if (docxPath is not null)
{
    if (!File.Exists(docxPath))
    {
        await Console.Error.WriteLineAsync($"docx not found: {docxPath}");
        return 2;
    }

    SeedNewsletter newsletter;
    try
    {
        newsletter = BuildFromDocx(docxPath);
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"docx parse failed: {ex.Message}");
        return 2;
    }

    await Console.Out.WriteLineAsync($"Parsed newsletter: id={newsletter.Id} title='{newsletter.Title}' issue={newsletter.IssueDate} year={newsletter.Year} topics={newsletter.Topics.Count}");

    try
    {
        await UpsertAsync(connectionString, table, newsletter);
        await Console.Out.WriteLineAsync($"Upserted into table {table}.");
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Table upsert failed: {ex.Message}");
        return 3;
    }
}

// ----- demo content -----------------------------------------------------------
if (demo)
{
    try
    {
        await SeedDemo.SeedAsync(connectionString, Console.Out);
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Demo seed failed: {ex.Message}");
        return 3;
    }
}

return 0;

// ----- helpers ----------------------------------------------------------------

static Dictionary<string, string> ParseNamed(string[] args)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length - 1; i += 2)
    {
        if (args[i].StartsWith("--")) d[args[i].Substring(2)] = args[i + 1];
    }
    return d;
}

static SeedNewsletter BuildFromDocx(string path)
{
    using var doc = WordprocessingDocument.Open(path, false);
    var body = doc.MainDocumentPart?.Document.Body
        ?? throw new InvalidOperationException("document has no body");

    var paragraphs = body.Descendants<Paragraph>()
        .Select(p => p.InnerText.Trim())
        .Where(t => t.Length > 0)
        .ToList();

    var headings = body.Descendants<Paragraph>()
        .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true)
        .Select(p => p.InnerText.Trim())
        .Where(t => t.Length > 0)
        .Distinct()
        .Take(8)
        .ToList();

    var (issueDate, derivedTitle) = ExtractIssueDateAndTitle(path);
    var title = paragraphs.FirstOrDefault(p => p.Length is > 3 and < 100) ?? derivedTitle;

    var summary = string.Join(' ', paragraphs.Skip(1).Take(4));
    if (summary.Length > 800) summary = summary.Substring(0, 800) + "...";
    if (summary.Length == 0) summary = $"SLYPN newsletter — {derivedTitle}.";

    return new SeedNewsletter(
        Id:        $"newsletter-{issueDate:yyyy-MM}",
        Title:     title,
        IssueDate: issueDate,
        Summary:   summary,
        Topics:    headings.Count > 0 ? headings : new List<string> { "Newsletter" },
        Year:      issueDate.Year.ToString("D4", CultureInfo.InvariantCulture));
}

static (DateOnly IssueDate, string Title) ExtractIssueDateAndTitle(string path)
{
    // Expected filename shape: SLYPN_Newsletter_<MONTH>_<YEAR>.docx
    var name = Path.GetFileNameWithoutExtension(path);
    var match = Regex.Match(name, @"_(?<m>[A-Za-z]+)_(?<y>\d{4})$");
    if (match.Success)
    {
        var month = DateTime.ParseExact(match.Groups["m"].Value, "MMMM", CultureInfo.InvariantCulture).Month;
        var year  = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        var title = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups["m"].Value.ToLowerInvariant())} {year}";
        return (new DateOnly(year, month, 1), title);
    }
    return (DateOnly.FromDateTime(DateTime.UtcNow), name);
}

static async Task UpsertAsync(string connectionString, string table, SeedNewsletter newsletter)
{
    // Mirror the API's serialization (System.Text.Json web defaults / camelCase) so
    // ContentRepository can deserialise the Json column straight into a Newsletter.
    var json = JsonSerializer.Serialize(newsletter, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    var client = new TableClient(connectionString, table);
    await client.CreateIfNotExistsAsync();

    var entity = new TableEntity(newsletter.Year, newsletter.Id) { ["Json"] = json };
    await client.UpsertEntityAsync(entity, TableUpdateMode.Replace);
}
