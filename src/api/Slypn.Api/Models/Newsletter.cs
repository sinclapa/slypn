using System.Text.Json.Serialization;

namespace Slypn.Api.Models;

public sealed record Newsletter(
    string Id,
    string Title,
    DateOnly IssueDate,
    string Summary,
    IReadOnlyList<string> Topics)
{
    /// <summary>
    /// Canonical download filename of the attached issue (PDF/DOCX), or null when
    /// no file is attached. The bytes live in the content blob container under
    /// <c>newsletters/{Id}</c>; presence of this value signals the file exists.
    /// </summary>
    public string? FileName { get; init; }

    [JsonPropertyName("_etag")]
    [Newtonsoft.Json.JsonProperty("_etag")]
    public string? Etag { get; init; }
}
