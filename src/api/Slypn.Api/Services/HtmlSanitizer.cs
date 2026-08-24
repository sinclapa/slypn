using AngleSharp.Dom;
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

    /// <summary>
    /// Forced onto every <c>target="_blank"</c> link. Without <c>noopener</c> the
    /// opened page gets a <c>window.opener</c> handle back to this tab and can
    /// navigate it elsewhere (reverse tabnabbing).
    /// </summary>
    private static readonly string[] RequiredBlankRel = { "noopener", "noreferrer" };

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
        // No CSS at-rules either (@import, @media and friends). data: URIs are
        // already refused by the AllowedSchemes allowlist above, not by this.
        _inner.AllowedAtRules.Clear();

        // Enforced after the allowlist has run, so it applies to whatever
        // survives. Forcing rel is preferred over dropping target from the
        // allowlist, which would change how already-published links behave.
        _inner.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement element) return;
            if (!element.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(element.GetAttribute("target"), "_blank", StringComparison.OrdinalIgnoreCase)) return;

            // Merge rather than overwrite: an author's existing rel may carry
            // something meaningful like nofollow, and a token already present
            // must not be added twice.
            var tokens = (element.GetAttribute("rel") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            tokens.AddRange(RequiredBlankRel
                .Where(required => !tokens.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToList());

            element.SetAttribute("rel", string.Join(' ', tokens));
        };
    }

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return _inner.Sanitize(html);
    }
}
