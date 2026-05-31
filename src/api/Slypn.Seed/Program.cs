using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Azure.Cosmos;
using Slypn.Seed;

// Args:
//   <docx-path>  --endpoint <url>  --key <key>  --database <name>  [--container <name>]
//
// Defaults: container = "newsletters".
// Exit codes: 0 success, 1 bad args, 2 docx error, 3 Cosmos error.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run -- <docx-path> --endpoint <url> --key <key> --database <name> [--container <name>]");
    return 1;
}

string docxPath = args[0];
var named = ParseNamed(args.Skip(1).ToArray());
if (!named.TryGetValue("endpoint", out var endpoint) ||
    !named.TryGetValue("key",      out var key)      ||
    !named.TryGetValue("database", out var database))
{
    Console.Error.WriteLine("Missing one of --endpoint / --key / --database.");
    return 1;
}
var container = named.GetValueOrDefault("container", "newsletters");

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
    await UpsertAsync(endpoint, key, database, container, newsletter);
    Console.WriteLine($"Upserted into {database}.{container}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cosmos upsert failed: {ex.Message}");
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

static async Task UpsertAsync(string endpoint, string key, string database, string container, SeedNewsletter newsletter)
{
    var isLocalEmulator =
        endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("127.0.0.1", StringComparison.Ordinal);

    var options = new CosmosClientOptions
    {
        ConnectionMode  = ConnectionMode.Gateway,
        ApplicationName = "Slypn.Seed",
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
    };
    if (isLocalEmulator)
    {
        options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });
    }

    using var client = new CosmosClient(endpoint, key, options);
    var db  = (await client.CreateDatabaseIfNotExistsAsync(database)).Database;
    var col = (await db.CreateContainerIfNotExistsAsync(new ContainerProperties(container, "/year"))).Container;

    await col.UpsertItemAsync(newsletter, new PartitionKey(newsletter.Year));
}
