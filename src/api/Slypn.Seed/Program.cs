using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Slypn.Seed;

// Args:
//   <docx-path>  --connection-string <cs>  [--table <name>]
//
// Defaults: table = "newsletters".
// Exit codes: 0 success, 1 bad args, 2 docx error, 3 storage error.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run -- <docx-path> --connection-string <cs> [--table <name>]");
    return 1;
}

string docxPath = args[0];
var named = ParseNamed(args.Skip(1).ToArray());
if (!named.TryGetValue("connection-string", out var connectionString))
{
    Console.Error.WriteLine("Missing --connection-string.");
    return 1;
}
var table = named.GetValueOrDefault("table", "newsletters");

if (!File.Exists(docxPath))
{
    Console.Error.WriteLine($"docx not found: {docxPath}");
    return 2;
}

SeedNewsletter newsletter;
try
{
    newsletter = BuildFromDocx(docxPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"docx parse failed: {ex.Message}");
    return 2;
}

Console.WriteLine($"Parsed newsletter: id={newsletter.Id} title='{newsletter.Title}' issue={newsletter.IssueDate} year={newsletter.Year} topics={newsletter.Topics.Count}");

try
{
    await UpsertAsync(connectionString, table, newsletter);
    Console.WriteLine($"Upserted into table {table}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Table upsert failed: {ex.Message}");
    return 3;
}

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
        Title:     derivedTitle,
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
