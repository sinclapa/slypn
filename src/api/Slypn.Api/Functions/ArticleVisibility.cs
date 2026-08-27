using Microsoft.Azure.Functions.Worker;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;

namespace Slypn.Api.Functions;

/// <summary>
/// Who may edit a published article or blog post, and how one is shaped for a
/// response that an anonymous caller can reach.
///
/// Both the <c>canEdit</c> flag on read responses and the 403 on
/// <c>POST /api/content/{id}/edit</c> go through <see cref="MayEdit"/>. Keeping
/// them on one predicate is the point of this class: if they drift apart the UI
/// offers a button that the API then refuses.
/// </summary>
internal static class ArticleVisibility
{
    public static bool MayEdit(Article article, FunctionContext context)
    {
        if (context.IsAdmin()) return true;

        // The edit endpoint is [RequireRole("Admin", "Contributor")], so a member
        // who authored something before losing the role must not be offered a
        // button that would 403.
        if (!context.IsContributor()) return false;

        // Content published before AuthorId existed carries none, which makes it
        // Admin-only by construction — a null author matches nobody. Both sides
        // are checked explicitly so an anonymous caller (null oid) can never
        // match legacy content (null author).
        return article.AuthorId is { Length: > 0 } author
            && context.GetUserOid() is { Length: > 0 } caller
            && string.Equals(author, caller, StringComparison.Ordinal);
    }

    /// <summary>
    /// Shape an article for a response an anonymous caller can reach: the author's
    /// Entra OID is stripped, and canEdit is computed for this caller instead.
    /// The OID is an internal identifier and has no business in a public payload.
    /// </summary>
    public static Article ForPublic(this Article article, FunctionContext context) =>
        article with { AuthorId = null, CanEdit = MayEdit(article, context) };

    public static IReadOnlyList<Article> VisibleInReview(
        IReadOnlyList<Article> items, FunctionContext context) =>
        context.IsAdmin()
            ? items
            : context.GetUserOid() is { Length: > 0 } oid
                ? items.Where(a => a.AuthorId == oid).ToList()
                : [];
}
