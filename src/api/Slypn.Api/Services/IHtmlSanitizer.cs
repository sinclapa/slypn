namespace Slypn.Api.Services;

public interface IHtmlSanitizer
{
    /// <summary>
    /// Strips disallowed tags / attributes / schemes from the supplied HTML.
    /// Returns an empty string when input is null/empty so callers can safely
    /// persist the result without an extra null check.
    /// </summary>
    string Sanitize(string? html);
}
