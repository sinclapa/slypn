using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Slypn.Seed;

// Args:
//   [<docx-path>]  --connection-string <cs>  [--table <name>]  [--container <name>]
//   [--dir <folder>]  [--demo]
//
// At least one of <docx-path> (seed one newsletter), --dir (bulk-import a folder
// of YYYY-MM.pdf/.docx issues), or --demo (seed demo content) is required. Each
// newsletter's file is uploaded to the content blob container under
// newsletters/{id}; its metadata row is upserted into the newsletters table.
// Defaults: table = "newsletters", container = "content".
// Exit codes: 0 success, 1 bad args, 2 file error, 3 storage error.

const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

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
var container = named.GetValueOrDefault("container", "content");
named.TryGetValue("dir", out var importDir);

if (docxPath is null && importDir is null && !demo)
{
    await Console.Error.WriteLineAsync("usage: dotnet run -- [<docx-path>] --connection-string <cs> [--table <name>] [--container <name>] [--dir <folder>] [--demo]");
    await Console.Error.WriteLineAsync("Pass a docx path or --dir to seed newsletters, and/or --demo to seed demo content.");
    return 1;
}

// ----- newsletter (single docx) -----------------------------------------------
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

    try
    {
        await UploadFileAsync(connectionString, container, newsletter.Id, docxPath, DocxContentType);
        await UpsertAsync(connectionString, table, newsletter);
        await Console.Out.WriteLineAsync($"Seeded {newsletter.Id}: '{newsletter.Title}' (issue {newsletter.IssueDate}, {newsletter.Topics.Count} topics) + file.");
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Seed failed: {ex.Message}");
        return 3;
    }
}

// ----- newsletters (bulk import from a folder) --------------------------------
if (importDir is not null)
{
    if (!Directory.Exists(importDir))
    {
        await Console.Error.WriteLineAsync($"dir not found: {importDir}");
        return 2;
    }

    var files = Directory.EnumerateFiles(importDir)
        .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (files.Count == 0)
    {
        await Console.Error.WriteLineAsync($"No .pdf/.docx files in {importDir}.");
        return 2;
    }

    var ok = 0;
    foreach (var path in files)
    {
        try
        {
            var (newsletter, contentType) = BuildFromImportFile(path);
            await UploadFileAsync(connectionString, container, newsletter.Id, path, contentType);
            await UpsertAsync(connectionString, table, newsletter);
            await Console.Out.WriteLineAsync($"  {newsletter.Id}  <- {Path.GetFileName(path)}  ({newsletter.Topics.Count} topics)");
            ok++;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  FAILED {Path.GetFileName(path)}: {ex.Message}");
            return 3;
        }
    }
    await Console.Out.WriteLineAsync($"Imported {ok}/{files.Count} newsletters into table '{table}' + container '{container}'.");
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

// A single docx (e.g. brief/SLYPN_Newsletter_MAY_2026.docx). Date from the
// SLYPN_Newsletter_<MONTH>_<YEAR> filename; metadata is derived, same as import.
static SeedNewsletter BuildFromDocx(string path)
    => NewsletterFor(IssueDateFromMonthName(path), ".docx");

/// <summary>
/// Bulk-import build: issue date comes from the YYYY-MM filename. Metadata is
/// derived (not mined from the document) — these real-world PDFs/DOCX have no
/// reliable title/heading structure (embedded shape text leaks digits into
/// InnerText), so a clean, predictable "Month YYYY" title reads far better than
/// scraped noise.
/// </summary>
static (SeedNewsletter Newsletter, string ContentType) BuildFromImportFile(string path)
{
    var stem = Path.GetFileNameWithoutExtension(path);
    var m = Regex.Match(stem, @"(?<y>\d{4})-(?<m>\d{2})");
    if (!m.Success)
        throw new InvalidOperationException($"filename '{stem}' is not YYYY-MM");

    var issueDate = new DateOnly(
        int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
        int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture), 1);
    var ext = Path.GetExtension(path).ToLowerInvariant();
    var contentType = ext == ".docx" ? DocxContentType : "application/pdf";
    return (NewsletterFor(issueDate, ext), contentType);
}

/// <summary>Clean, predictable metadata for one issue, keyed by its month.</summary>
static SeedNewsletter NewsletterFor(DateOnly issueDate, string ext)
{
    var monthYear = issueDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
    return new SeedNewsletter(
        Id:        $"newsletter-{issueDate:yyyy-MM}",
        Title:     $"SLYPN newsletter — {monthYear}.",
        IssueDate: issueDate,
        Summary:   $"The SLYPN community newsletter for {monthYear}.",
        Topics:    new List<string> { "Newsletter" },
        FileName:  $"SLYPN-Newsletter-{issueDate:yyyy-MM}{ext}");
}

static DateOnly IssueDateFromMonthName(string path)
{
    // Expected filename shape: SLYPN_Newsletter_<MONTH>_<YEAR>.docx
    var name = Path.GetFileNameWithoutExtension(path);
    var match = Regex.Match(name, @"_(?<m>[A-Za-z]+)_(?<y>\d{4})$");
    if (match.Success)
    {
        var month = DateTime.ParseExact(match.Groups["m"].Value, "MMMM", CultureInfo.InvariantCulture).Month;
        var year  = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        return new DateOnly(year, month, 1);
    }
    return DateOnly.FromDateTime(DateTime.UtcNow);
}

static async Task UploadFileAsync(string connectionString, string container, string id, string localPath, string contentType)
{
    var blobs = new BlobContainerClient(connectionString, container);
    await blobs.CreateIfNotExistsAsync(PublicAccessType.None);
    // Idempotent: the blob key is the deterministic newsletter id, and passing
    // BlobUploadOptions (no If-None-Match) overwrites any existing blob, so a
    // re-run replaces the file in place rather than failing on "already exists".
    var blob = blobs.GetBlobClient($"newsletters/{id}");
    await using var stream = File.OpenRead(localPath);
    await blob.UploadAsync(stream, new BlobUploadOptions
    {
        HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
    });
}

static async Task UpsertAsync(string connectionString, string table, SeedNewsletter newsletter)
{
    // Mirror the API's serialization (System.Text.Json web defaults / camelCase) so
    // ContentRepository can deserialise the Json column straight into a Newsletter.
    var json = JsonSerializer.Serialize(newsletter, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    var client = new TableClient(connectionString, table);
    await client.CreateIfNotExistsAsync();

    // Idempotent: newsletters use a single constant partition (mirrors the API's
    // ContentRepository.NewslettersPartition), and Upsert/Replace insert-or-replaces
    // the row keyed by Id — re-running never duplicates newsletters.
    var entity = new TableEntity("newsletter", newsletter.Id) { ["Json"] = json };
    await client.UpsertEntityAsync(entity, TableUpdateMode.Replace);
}
