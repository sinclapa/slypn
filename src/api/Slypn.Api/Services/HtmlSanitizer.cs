using Ganss.Xss;

namespace Slypn.Api.Services;

/// <summary>
/// Allowlist matches the TipTap editor's toolbar exactly (StarterKit
/// headings 1-3, lists, blockquote, code, plus the Image and Link
/// extensions). Anything outside this list is dropped; the editor can
/// never emit it, so any drift implies tampering and we ignore it.
/// </summary>
public sealed class HtmlSanitizer : IHtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        // Block-level
        "p", "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "blockquote", "pre", "hr",
        // Inline
        "strong", "b", "em", "i", "u", "s", "code", "br", "span",
        // Media + links
        "a", "img",
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "rel", "target",  // <a>
        "src", "alt", "title",    // <img>, <a>
        "class",                  // TipTap occasionally attaches utility classes
    };

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto",
    };

    private readonly Ganss.Xss.HtmlSanitizer _inner;

    public HtmlSanitizer()
    {
        _inner = new Ganss.Xss.HtmlSanitizer();
        _inner.AllowedTags.Clear();
        foreach (var t in AllowedTags) _inner.AllowedTags.Add(t);
        _inner.AllowedAttributes.Clear();
        foreach (var a in AllowedAttributes) _inner.AllowedAttributes.Add(a);
        _inner.AllowedSchemes.Clear();
        foreach (var s in AllowedSchemes) _inner.AllowedSchemes.Add(s);
        // Defence in depth — no CSS even if a span sneaks one in.
        _inner.AllowedCssProperties.Clear();
        // Refuse data: URIs everywhere (we upload images to Blob and link by URL).
        _inner.AllowedAtRules.Clear();
    }

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return _inner.Sanitize(html);
    }
}
